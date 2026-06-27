using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

// B2 — instructor substitution-request page (/Scheduling/Substitutions/Create).
public class SubstitutionCreatePageTests : IClassFixture<SqliteFixture>
{
    private const string Url = "/Scheduling/Substitutions/Create";
    private readonly SqliteFixture _sqlite;
    public SubstitutionCreatePageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Client helpers ──────────────────────────────────────────────────────────

    private static async Task<(OrkabiAppFactory factory, HttpClient client, int userId)>
        SignedInWithRoleAsync(SqliteFixture sqlite, string role, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        var email = $"subcreate.{role}.{suffix}@test.test";
        int userId;
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                existing = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(existing, "Passw0rd!");
                await um.AddToRoleAsync(existing, role);
            }
            userId = existing.Id;
        }
        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        return (factory, client, userId);
    }

    // ── Seed helpers ────────────────────────────────────────────────────────────

    private static async Task<int> SeedExtraInstructorAsync(OrkabiAppFactory factory)
    {
        using var s = factory.Services.CreateScope();
        var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"extra.instr.{Guid.NewGuid():N}@test.test";
        var u = new AppUser { UserName = email, Email = email };
        await um.CreateAsync(u, "Passw0rd!");
        await um.AddToRoleAsync(u, AppRoles.Instructor);
        return u.Id;
    }

    // Seeds a shift instance assigned (ActualInstructorId) to the given instructor,
    // dated `daysFromToday` relative to Israel-today. Returns (instanceId, className).
    private static async Task<(int instanceId, string className)> SeedShiftAsync(
        OrkabiAppFactory factory, int instructorId, int daysFromToday,
        ShiftInstanceStatus status = ShiftInstanceStatus.Scheduled)
    {
        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

        var school = new School { Name = $"בס-{Guid.NewGuid():N}", City = "חיפה" };
        var year = new AcademicYear
        {
            Label = $"תשפ-{Guid.NewGuid():N}"[..10],
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
            IsCurrent = false
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var className = $"כיתה-{Guid.NewGuid():N}";
        var cls = new Class { Name = className, School = school, AcademicYear = year, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DefaultInstructorId = instructorId,
            DayOfWeek = DayOfWeek.Sunday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        var todayIsrael = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instance = new ShiftInstance
        {
            TemplateId = template.Id,
            ActualInstructorId = instructorId,
            Date = todayIsrael.AddDays(daysFromToday),
            Status = status
        };
        db.ShiftInstances.Add(instance);
        await db.SaveChangesAsync();
        return (instance.Id, className);
    }

    private static async Task<int> SeedPendingRequestAsync(
        OrkabiAppFactory factory, int shiftInstanceId, int requesterId, int substituteId)
    {
        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var req = new SubstitutionRequest
        {
            ShiftInstanceId = shiftInstanceId,
            RequestingInstructorId = requesterId,
            SubstituteInstructorId = substituteId,
            Status = SubstitutionStatus.Pending
        };
        db.SubstitutionRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var resp = await client.GetAsync(Url);
        var html = await resp.Content.ReadAsStringAsync();
        return AntiForgery.Extract(html);
    }

    // ── Authorization ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_redirected_to_login()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Cs_forbidden()
    {
        var (factory, client, _) = await SignedInWithRoleAsync(_sqlite, AppRoles.CustomerService, "cs");
        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Logistics_forbidden()
    {
        var (factory, client, _) = await SignedInWithRoleAsync(_sqlite, AppRoles.Logistics, "log");
        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_can_open()
    {
        var (factory, client, _) = await SignedInWithRoleAsync(_sqlite, AppRoles.Instructor, "open");
        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    [Fact]
    public async Task Admin_can_open()
    {
        var (factory, client, _) = await SignedInWithRoleAsync(_sqlite, AppRoles.Admin, "adminopen");
        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    // ── GET listing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lists_only_my_future_shifts()
    {
        var (factory, client, myId) = await SignedInWithRoleAsync(_sqlite, AppRoles.Instructor, "list");
        var (_, mineFuture) = await SeedShiftAsync(factory, myId, daysFromToday: 7);
        var (_, minePast) = await SeedShiftAsync(factory, myId, daysFromToday: -7);
        var otherId = await SeedExtraInstructorAsync(factory);
        var (_, othersFuture) = await SeedShiftAsync(factory, otherId, daysFromToday: 7);

        var resp = await client.GetAsync(Url);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains(mineFuture, body);        // my future shift is offered
        Assert.DoesNotContain(minePast, body);    // my past shift is not
        Assert.DoesNotContain(othersFuture, body);// another instructor's shift is not
        factory.Dispose();
    }

    // ── POST create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Instructor_can_create_request()
    {
        var (factory, client, myId) = await SignedInWithRoleAsync(_sqlite, AppRoles.Instructor, "create");
        var (instanceId, _) = await SeedShiftAsync(factory, myId, daysFromToday: 7);
        var subId = await SeedExtraInstructorAsync(factory);

        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.ShiftInstanceId"] = instanceId.ToString(),
            ["Input.SubstituteInstructorId"] = subId.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync(Url, form);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var req = await db.SubstitutionRequests.SingleAsync(r => r.ShiftInstanceId == instanceId);
        Assert.Equal(SubstitutionStatus.Pending, req.Status);
        Assert.Equal(myId, req.RequestingInstructorId);
        Assert.Equal(subId, req.SubstituteInstructorId);
        factory.Dispose();
    }

    [Fact]
    public async Task Cannot_request_for_shift_not_mine()
    {
        var (factory, client, _) = await SignedInWithRoleAsync(_sqlite, AppRoles.Instructor, "notmine");
        var otherId = await SeedExtraInstructorAsync(factory);
        var (foreignInstanceId, _) = await SeedShiftAsync(factory, otherId, daysFromToday: 7);
        var subId = await SeedExtraInstructorAsync(factory);

        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.ShiftInstanceId"] = foreignInstanceId.ToString(),
            ["Input.SubstituteInstructorId"] = subId.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync(Url, form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);  // re-render with error, not redirect

        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.SubstitutionRequests.AnyAsync(r => r.ShiftInstanceId == foreignInstanceId));
        factory.Dispose();
    }

    [Fact]
    public async Task Cannot_request_for_past_shift()
    {
        var (factory, client, myId) = await SignedInWithRoleAsync(_sqlite, AppRoles.Instructor, "past");
        var (pastInstanceId, _) = await SeedShiftAsync(factory, myId, daysFromToday: -3);
        var subId = await SeedExtraInstructorAsync(factory);

        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.ShiftInstanceId"] = pastInstanceId.ToString(),
            ["Input.SubstituteInstructorId"] = subId.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync(Url, form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.SubstitutionRequests.AnyAsync(r => r.ShiftInstanceId == pastInstanceId));
        factory.Dispose();
    }

    [Fact]
    public async Task Duplicate_pending_request_blocked()
    {
        var (factory, client, myId) = await SignedInWithRoleAsync(_sqlite, AppRoles.Instructor, "dupe");
        var (instanceId, _) = await SeedShiftAsync(factory, myId, daysFromToday: 7);
        var subId = await SeedExtraInstructorAsync(factory);
        await SeedPendingRequestAsync(factory, instanceId, myId, subId);  // already pending

        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.ShiftInstanceId"] = instanceId.ToString(),
            ["Input.SubstituteInstructorId"] = subId.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync(Url, form);

        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.SubstitutionRequests.CountAsync(
            r => r.ShiftInstanceId == instanceId && r.Status == SubstitutionStatus.Pending);
        Assert.Equal(1, count);  // no duplicate created
        factory.Dispose();
    }

    // ── POST cancel ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Instructor_can_cancel_own_pending_request()
    {
        var (factory, client, myId) = await SignedInWithRoleAsync(_sqlite, AppRoles.Instructor, "cancel");
        var (instanceId, _) = await SeedShiftAsync(factory, myId, daysFromToday: 7);
        var subId = await SeedExtraInstructorAsync(factory);
        var reqId = await SeedPendingRequestAsync(factory, instanceId, myId, subId);

        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync($"{Url}?handler=Cancel&id={reqId}", form);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var req = await db.SubstitutionRequests.FindAsync(reqId);
        Assert.Equal(SubstitutionStatus.Cancelled, req!.Status);
        factory.Dispose();
    }

    [Fact]
    public async Task Cannot_cancel_another_instructors_request()
    {
        var (factory, client, _) = await SignedInWithRoleAsync(_sqlite, AppRoles.Instructor, "cancelother");
        var ownerId = await SeedExtraInstructorAsync(factory);
        var (instanceId, _) = await SeedShiftAsync(factory, ownerId, daysFromToday: 7);
        var subId = await SeedExtraInstructorAsync(factory);
        var reqId = await SeedPendingRequestAsync(factory, instanceId, ownerId, subId);

        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        await client.PostAsync($"{Url}?handler=Cancel&id={reqId}", form);

        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var req = await db.SubstitutionRequests.FindAsync(reqId);
        Assert.Equal(SubstitutionStatus.Pending, req!.Status);  // untouched
        factory.Dispose();
    }

    // ── Reachability ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Instructor_dashboard_links_to_create()
    {
        var (factory, client, _) = await SignedInWithRoleAsync(_sqlite, AppRoles.Instructor, "dashlink");
        var resp = await client.GetAsync("/Dashboard/Instructor");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(Url, body);
        factory.Dispose();
    }
}
