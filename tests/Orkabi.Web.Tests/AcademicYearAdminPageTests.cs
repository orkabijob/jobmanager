using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

// B3 — admin academic-year management (/Admin/AcademicYears: list, create, set-current).
public class AcademicYearAdminPageTests : IClassFixture<SqliteFixture>
{
    private const string Url = "/Admin/AcademicYears";
    private readonly SqliteFixture _sqlite;
    public AcademicYearAdminPageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static OrkabiAppFactory Factory(SqliteFixture sqlite) =>
        new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();

    private static HttpClient Anon(OrkabiAppFactory f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task SeedUserAsync(IServiceProvider sp, string email, string? role)
    {
        var um = sp.GetRequiredService<UserManager<AppUser>>();
        if (await um.FindByEmailAsync(email) is not null) return;
        var u = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        await um.CreateAsync(u, "Passw0rd!");
        if (role is not null) await um.AddToRoleAsync(u, role);
    }

    private static async Task<HttpClient> AdminClientAsync(OrkabiAppFactory f, string email)
    {
        using (var s = f.Services.CreateScope())
            await SeedUserAsync(s.ServiceProvider, email, AppRoles.Admin);
        return await TestLogin.SignInAsync(f, email, "Passw0rd!");
    }

    private static async Task<int> SeedYearAsync(OrkabiAppFactory f, string label, bool current = false)
    {
        using var s = f.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var y = new AcademicYear
        {
            Label = label,
            StartDate = new DateOnly(2024, 9, 1),
            EndDate = new DateOnly(2025, 6, 30),
            IsCurrent = false
        };
        db.AcademicYears.Add(y);
        await db.SaveChangesAsync();

        // Promote via the service (clear-before-set) rather than inserting a raw IsCurrent=true row —
        // the test class shares one db, so a second raw current row would violate the partial index.
        if (current)
            await s.ServiceProvider.GetRequiredService<AcademicYearService>().SetCurrentAsync(y.Id);

        return y.Id;
    }

    private static async Task<string> TokenAsync(HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        return AntiForgery.Extract(await resp.Content.ReadAsStringAsync());
    }

    // ── R9: rollover ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_can_roll_over_structure_between_years()
    {
        using var factory = Factory(_sqlite);
        var client = await AdminClientAsync(factory, "admin.rollover@test.test");
        int fromId = await SeedYearAsync(factory, $"from-{Guid.NewGuid():N}"[..14]);
        int toId = await SeedYearAsync(factory, $"to-{Guid.NewGuid():N}"[..14]);
        string className;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var school = new School { Name = $"בס-{Guid.NewGuid():N}", City = "ת" };
            db.Schools.Add(school);
            await db.SaveChangesAsync();
            className = $"כתה-{Guid.NewGuid():N}"[..14];
            db.Classes.Add(new Class { Name = className, SchoolId = school.Id, AcademicYearId = fromId, Status = EntityStatus.Active });
            await db.SaveChangesAsync();
        }

        var token = await TokenAsync(client, Url);
        var resp = await client.PostAsync($"{Url}?handler=RollOver",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["fromYearId"] = fromId.ToString(),
                ["toYearId"] = toId.ToString(),
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Classes.AnyAsync(c => c.AcademicYearId == toId && c.Name == className));
        }
    }

    // ── Authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_redirected_to_login()
    {
        using var f = Factory(_sqlite);
        var resp = await Anon(f).GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Non_admin_denied()
    {
        using var f = Factory(_sqlite);
        using (var s = f.Services.CreateScope())
            await SeedUserAsync(s.ServiceProvider, "cs.ay@test.test", AppRoles.CustomerService);
        var client = await TestLogin.SignInAsync(f, "cs.ay@test.test", "Passw0rd!");

        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/AccessDenied", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Admin_can_open_and_see_a_year()
    {
        using var f = Factory(_sqlite);
        var label = $"שנה-{Guid.NewGuid():N}"[..14];
        await SeedYearAsync(f, label, current: true);
        var client = await AdminClientAsync(f, "admin.ay.open@test.test");

        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains(label, body);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_can_create_year()
    {
        using var f = Factory(_sqlite);
        var client = await AdminClientAsync(f, "admin.ay.create@test.test");
        var label = $"חדשה-{Guid.NewGuid():N}"[..14];

        var token = await TokenAsync(client, $"{Url}/Create");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Label"] = label,
            ["Input.StartDate"] = "2026-09-01",
            ["Input.EndDate"] = "2027-06-30",
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync($"{Url}/Create", form);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        using var s = f.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var y = await db.AcademicYears.SingleAsync(a => a.Label == label);
        Assert.False(y.IsCurrent);
        Assert.Equal(new DateOnly(2026, 9, 1), y.StartDate);
        Assert.Equal(new DateOnly(2027, 6, 30), y.EndDate);
    }

    [Fact]
    public async Task Create_rejects_end_before_start()
    {
        using var f = Factory(_sqlite);
        var client = await AdminClientAsync(f, "admin.ay.badrange@test.test");
        var label = $"גרוע-{Guid.NewGuid():N}"[..14];

        var token = await TokenAsync(client, $"{Url}/Create");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Label"] = label,
            ["Input.StartDate"] = "2027-06-30",
            ["Input.EndDate"] = "2026-09-01",  // end before start
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync($"{Url}/Create", form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);  // re-render with error

        using var s = f.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.AcademicYears.AnyAsync(a => a.Label == label));
    }

    [Fact]
    public async Task Create_missing_start_date_shows_hebrew_required_and_creates_nothing()
    {
        using var f = Factory(_sqlite);
        var client = await AdminClientAsync(f, "admin.ay.nostart@test.test");
        var label = $"חסר-{Guid.NewGuid():N}"[..14];

        var token = await TokenAsync(client, $"{Url}/Create");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Label"] = label,
            ["Input.EndDate"] = "2027-06-30",   // StartDate deliberately omitted
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync($"{Url}/Create", form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains("יש לבחור תאריך התחלה", body);  // the Hebrew Required message, not an English binder error

        using var s = f.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.AcademicYears.AnyAsync(a => a.Label == label));  // nothing created
    }

    // ── Set current ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_can_set_current_exclusively()
    {
        using var f = Factory(_sqlite);
        var client = await AdminClientAsync(f, "admin.ay.setcur@test.test");
        var oldId = await SeedYearAsync(f, $"ישנה-{Guid.NewGuid():N}"[..14], current: true);
        var newId = await SeedYearAsync(f, $"חדשה-{Guid.NewGuid():N}"[..14], current: false);

        var token = await TokenAsync(client, Url);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync($"{Url}?handler=SetCurrent&id={newId}", form);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        using var s = f.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.AcademicYears.FindAsync(newId))!.IsCurrent);
        Assert.False((await db.AcademicYears.FindAsync(oldId))!.IsCurrent);
        Assert.Single(await db.AcademicYears.Where(y => y.IsCurrent).ToListAsync());
    }

    // ── Reachability ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_dashboard_links_to_academic_years()
    {
        using var f = Factory(_sqlite);
        var client = await AdminClientAsync(f, "admin.ay.dash@test.test");
        var resp = await client.GetAsync("/Dashboard/Admin");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(Url, await resp.Content.ReadAsStringAsync());
    }
}
