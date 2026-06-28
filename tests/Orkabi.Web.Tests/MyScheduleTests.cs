using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

// F17 — instructor read-only "my schedule" (/Scheduling/MySchedule).
public class MyScheduleTests : IClassFixture<SqliteFixture>
{
    private const string Url = "/Scheduling/MySchedule";
    private readonly SqliteFixture _sqlite;
    public MyScheduleTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client, int userId)>
        SignedInAsync(SqliteFixture sqlite, string role, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        var email = $"sched.{role}.{suffix}@test.test";
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
        return (factory, await TestLogin.SignInAsync(factory, email, "Passw0rd!"), userId);
    }

    private static async Task<(int instanceId, string className)> SeedShiftAsync(
        OrkabiAppFactory f, int instructorId, int daysFromToday)
    {
        using var s = f.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var school = new School { Name = $"בס-{Guid.NewGuid():N}", City = "חיפה" };
        var year = new AcademicYear { Label = $"y{Guid.NewGuid():N}"[..8], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), IsCurrent = false };
        db.Schools.Add(school); db.AcademicYears.Add(year);
        await db.SaveChangesAsync();
        var className = $"כתה-{Guid.NewGuid():N}"[..14];
        var cls = new Class { Name = className, School = school, AcademicYear = year, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instructorId, DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instance = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instructorId, Date = today.AddDays(daysFromToday), Status = ShiftInstanceStatus.Scheduled };
        db.ShiftInstances.Add(instance);
        await db.SaveChangesAsync();
        return (instance.Id, className);
    }

    [Fact]
    public async Task Anonymous_redirected_to_login()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await c.GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Cs_forbidden()
    {
        var (factory, client, _) = await SignedInAsync(_sqlite, AppRoles.CustomerService, "cs");
        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_sees_own_upcoming_shifts_only()
    {
        var (factory, client, myId) = await SignedInAsync(_sqlite, AppRoles.Instructor, "v");
        var (_, mineSoon) = await SeedShiftAsync(factory, myId, daysFromToday: 3);
        var (_, mineFar) = await SeedShiftAsync(factory, myId, daysFromToday: 60);   // beyond the 30-day window
        var otherId = (await SignedInAsync(_sqlite, AppRoles.Instructor, "other")).userId;
        var (_, othersSoon) = await SeedShiftAsync(factory, otherId, daysFromToday: 3);

        var body = WebUtility.HtmlDecode(await (await client.GetAsync(Url)).Content.ReadAsStringAsync());
        Assert.Contains(mineSoon, body);          // my upcoming shift shows
        Assert.DoesNotContain(mineFar, body);     // beyond the default 30-day window
        Assert.DoesNotContain(othersSoon, body);  // another instructor's shift is not mine
        factory.Dispose();
    }

    // ── F18: proactive absence report ──────────────────────────────────────────

    [Fact]
    public async Task ReportAbsence_creates_admin_action_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var (_, _, myId) = await SignedInAsync(_sqlite, AppRoles.Instructor, "abs");
        var (instanceId, _) = await SeedShiftAsync(factory, myId, daysFromToday: 4);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<SchedulingService>();

        await svc.ReportAbsenceAsync(instanceId, myId);

        var item = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == $"absence_report_{instanceId}");
        Assert.NotNull(item);
        Assert.Equal(AppRoles.Admin, item!.AssignedToRole);
        Assert.Equal(ActionItemStatus.Open, item.Status);
        factory.Dispose();
    }

    [Fact]
    public async Task ReportAbsence_for_another_instructors_shift_throws()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var (_, _, myId) = await SignedInAsync(_sqlite, AppRoles.Instructor, "absme");
        var (_, _, otherId) = await SignedInAsync(_sqlite, AppRoles.Instructor, "absother");
        var (instanceId, _) = await SeedShiftAsync(factory, otherId, daysFromToday: 4);

        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<SchedulingService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ReportAbsenceAsync(instanceId, myId));
        factory.Dispose();
    }

    [Fact]
    public async Task ReportAbsence_is_idempotent()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var (_, _, myId) = await SignedInAsync(_sqlite, AppRoles.Instructor, "absidem");
        var (instanceId, _) = await SeedShiftAsync(factory, myId, daysFromToday: 4);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<SchedulingService>();

        await svc.ReportAbsenceAsync(instanceId, myId);
        await svc.ReportAbsenceAsync(instanceId, myId);

        Assert.Equal(1, await db.ActionItems.CountAsync(a => a.DeduplicationKey == $"absence_report_{instanceId}"));
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_reports_absence_via_schedule_page()
    {
        var (factory, client, myId) = await SignedInAsync(_sqlite, AppRoles.Instructor, "abspg");
        var (instanceId, _) = await SeedShiftAsync(factory, myId, daysFromToday: 4);

        var getResp = await client.GetAsync(Url);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["shiftInstanceId"] = instanceId.ToString(),
            ["Days"] = "7",
            ["__RequestVerificationToken"] = token
        });
        var postResp = await client.PostAsync($"{Url}?handler=ReportAbsence", form);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains("days=7", postResp.Headers.Location?.ToString());   // PRG preserves the 7-day filter

        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.ActionItems.AnyAsync(a => a.DeduplicationKey == $"absence_report_{instanceId}"));
        factory.Dispose();
    }

    [Fact]
    public async Task ListUpcoming_returns_only_instructors_shifts_in_range()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var (_, _, myId) = await SignedInAsync(_sqlite, AppRoles.Instructor, "svc");
        await SeedShiftAsync(factory, myId, daysFromToday: 2);
        await SeedShiftAsync(factory, myId, daysFromToday: 100);   // out of range

        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<SchedulingService>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var rows = await svc.ListUpcomingForInstructorAsync(myId, today, today.AddDays(30));

        Assert.Single(rows);
        factory.Dispose();
    }
}
