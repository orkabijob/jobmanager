using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class ClientsPageTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ClientsPageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateCsClientAsync(
        SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"cs.clients{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.CustomerService);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"cs.clients{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    [Fact]
    public async Task Clients_index_active_filter_hides_inactive_but_all_filter_shows_them()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_active_filter");

        // Arrange: seed one active client and one inactive client
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Clients.Add(new Client
            {
                Name = "תלמיד פעיל לבדיקה",
                IsActive = true
            });
            db.Clients.Add(new Client
            {
                Name = "תלמיד לא פעיל לבדיקה",
                IsActive = false
            });
            await db.SaveChangesAsync();
        }

        // Act 1: default index (activeOnly=true, the "פעילים" filter)
        var activeResp = await client.GetAsync("/People/Clients");
        Assert.Equal(HttpStatusCode.OK, activeResp.StatusCode);
        var activeRaw = await activeResp.Content.ReadAsStringAsync();
        // Razor HTML-encodes Hebrew; decode before asserting
        var activeBody = System.Net.WebUtility.HtmlDecode(activeRaw);

        Assert.Contains("תלמיד פעיל לבדיקה", activeBody);
        Assert.DoesNotContain("תלמיד לא פעיל לבדיקה", activeBody);

        // Act 2: activeOnly=false (the "כולם" filter) — both clients must appear
        var allResp = await client.GetAsync("/People/Clients?activeOnly=false");
        Assert.Equal(HttpStatusCode.OK, allResp.StatusCode);
        var allRaw = await allResp.Content.ReadAsStringAsync();
        var allBody = System.Net.WebUtility.HtmlDecode(allRaw);

        Assert.Contains("תלמיד פעיל לבדיקה", allBody);
        Assert.Contains("תלמיד לא פעיל לבדיקה", allBody);

        factory.Dispose();
    }

    // ── Clients Edit page: deactivation wiring ───────────────────────────────

    /// <summary>
    /// Proves that flipping a client from active→inactive via the Edit page POST
    /// routes through DeactivateAsync (not just UpdateAsync), so the mass-dropout
    /// Admin ActionItem is created when the threshold is met.
    ///
    /// RED before the fix: UpdateAsync set IsActive=false directly, then DeactivateAsync
    /// saw the client already inactive and returned early (idempotent no-op), so no item.
    /// GREEN after the fix: Edit page preserves priorIsActive, lets DeactivateAsync own
    /// the flip, so the dropout check runs.
    /// </summary>
    [Fact]
    public async Task Editing_client_to_inactive_triggers_mass_dropout_when_threshold_met()
    {
        var (factory, httpClient) = await CreateCsClientAsync(_sqlite, "_edit_deactivate_trigger");

        int clientToDeactivateId;
        int classId;

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            // Seed a class with 3 enrolled clients.
            var school = new School { Name = $"בי\"ס-{Guid.NewGuid():N}"[..12], City = "ירושלים" };
            var year = new AcademicYear
            {
                Label = $"תשפ\"-{Guid.NewGuid().ToString("N")[..4]}",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                IsCurrent = false
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class
            {
                Name = $"כיתה-ויסות-{Guid.NewGuid():N}"[..18],
                School = school,
                AcademicYear = year,
                Status = EntityStatus.Active
            };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            classId = cls.Id;

            var client1 = new Client { Name = $"לקוח-א-{Guid.NewGuid():N}"[..14], IsActive = true };
            var client2 = new Client { Name = $"לקוח-ב-{Guid.NewGuid():N}"[..14], IsActive = true };
            var client3 = new Client { Name = $"לקוח-ג-{Guid.NewGuid():N}"[..14], IsActive = true };
            db.Clients.AddRange(client1, client2, client3);
            await db.SaveChangesAsync();

            db.Enrollments.Add(new Enrollment { ClientId = client1.Id, ClassId = classId, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow });
            db.Enrollments.Add(new Enrollment { ClientId = client2.Id, ClassId = classId, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow });
            db.Enrollments.Add(new Enrollment { ClientId = client3.Id, ClassId = classId, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow });
            await db.SaveChangesAsync();

            // Make client1 already inactive within 7d (simulate a prior deactivation 2 days ago).
            await db.Database.ExecuteSqlAsync(
                $"UPDATE Clients SET IsActive = 0, UpdatedAt = {DateTime.UtcNow.AddDays(-2):O} WHERE Id = {client1.Id}");
            db.ChangeTracker.Clear();

            clientToDeactivateId = client2.Id;
        }

        // GET the Edit page for client2 to extract the antiforgery token.
        var getResp = await httpClient.GetAsync($"/People/Clients/Edit/{clientToDeactivateId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(getHtml);

        // POST: flip IsActive to false (deactivate client2 via the Edit page).
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "לקוח-ב-ערוך",
            ["Input.IsActive"] = "false",
            ["__RequestVerificationToken"] = token
        });
        var postResp = await httpClient.PostAsync($"/People/Clients/Edit/{clientToDeactivateId}", form);
        // Expect redirect (PRG) on success.
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        // Assert: the mass-dropout ActionItem was created (client1 + client2 = 2 within 7d → threshold met).
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var dedupKey = $"dropout_mass_{classId}";
            var item = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
            Assert.NotNull(item);
            Assert.Equal(AppRoles.Admin, item!.AssignedToRole);
        }

        factory.Dispose();
    }

    /// <summary>
    /// Editing non-IsActive fields (name/phone) while leaving the client active
    /// must NOT create a dropout ActionItem — DeactivateAsync must not be called.
    /// </summary>
    [Fact]
    public async Task Editing_client_other_fields_does_not_deactivate()
    {
        var (factory, httpClient) = await CreateCsClientAsync(_sqlite, "_edit_no_deactivate");

        int clientId;
        int classId;

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"בי\"ס-{Guid.NewGuid():N}"[..12], City = "חיפה" };
            var year = new AcademicYear
            {
                Label = $"תשפ\"-{Guid.NewGuid().ToString("N")[..4]}",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                IsCurrent = false
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class
            {
                Name = $"כיתה-לא-מושבת-{Guid.NewGuid():N}"[..18],
                School = school,
                AcademicYear = year,
                Status = EntityStatus.Active
            };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            classId = cls.Id;

            var client = new Client { Name = $"לקוח-שם-{Guid.NewGuid():N}"[..14], IsActive = true };
            db.Clients.Add(client);
            await db.SaveChangesAsync();

            db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = classId, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow });
            await db.SaveChangesAsync();

            clientId = client.Id;
        }

        var getResp = await httpClient.GetAsync($"/People/Clients/Edit/{clientId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // POST: edit name only, leave IsActive=true.
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "שם חדש",
            ["Input.IsActive"] = "true",
            ["__RequestVerificationToken"] = token
        });
        var postResp = await httpClient.PostAsync($"/People/Clients/Edit/{clientId}", form);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            // Client must still be active.
            var updated = await db.Clients.FindAsync(clientId);
            Assert.NotNull(updated);
            Assert.True(updated!.IsActive);
            Assert.Equal("שם חדש", updated.Name);

            // No dropout item must have been created.
            var dedupKey = $"dropout_mass_{classId}";
            var count = await db.ActionItems.CountAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
            Assert.Equal(0, count);
        }

        factory.Dispose();
    }
}
