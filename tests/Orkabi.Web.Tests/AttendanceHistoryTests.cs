using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

// F13 — attendance history for CS (/Attendance/History).
public class AttendanceHistoryTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public AttendanceHistoryTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> ClientAsync(SqliteFixture sqlite, string role, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        var email = $"ah.{role}.{suffix}@test.test";
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            if (await um.FindByEmailAsync(email) is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, role);
            }
        }
        return (factory, await TestLogin.SignInAsync(factory, email, "Passw0rd!"));
    }

    // Seeds a class with one Completed lesson log carrying 1 Present + 1 Absent attendance.
    private static async Task<(int classId, string className)> SeedLessonWithAttendanceAsync(OrkabiAppFactory f)
    {
        using var s = f.Services.CreateScope();
        var sp = s.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var um = sp.GetRequiredService<UserManager<AppUser>>();

        var instrEmail = $"hist-instr-{Guid.NewGuid():N}@test.test";
        var instr = new AppUser { UserName = instrEmail, Email = instrEmail };
        await um.CreateAsync(instr, "Passw0rd!");
        await um.AddToRoleAsync(instr, AppRoles.Instructor);

        var school = new School { Name = $"בס-{Guid.NewGuid():N}", City = "תל אביב" };
        var year = new AcademicYear { Label = $"y{Guid.NewGuid():N}"[..8], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), IsCurrent = false };
        db.Schools.Add(school); db.AcademicYears.Add(year);
        await db.SaveChangesAsync();
        var className = $"כתה-{Guid.NewGuid():N}"[..14];
        var cls = new Class { Name = className, School = school, AcademicYear = year, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instr.Id, DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instance = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instr.Id, Date = today.AddDays(-1), Status = ShiftInstanceStatus.Scheduled };
        db.ShiftInstances.Add(instance);
        await db.SaveChangesAsync();
        var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"מ-{Guid.NewGuid():N}"[..10], ExpectedLessonsToComplete = 5 };
        db.Models.Add(model);
        await db.SaveChangesAsync();

        var log = await svc.SaveLessonLogAsync(instance.Id, model.Id, LessonLogStatus.Completed, null);

        var c1 = new Client { Name = $"א-{Guid.NewGuid():N}"[..10] };
        var c2 = new Client { Name = $"ב-{Guid.NewGuid():N}"[..10] };
        db.Clients.AddRange(c1, c2);
        await db.SaveChangesAsync();
        await svc.SubmitAttendanceAsync(log.Id,
            new[] { (c1.Id, AttendanceStatus.Present), (c2.Id, AttendanceStatus.Absent) },
            "hist-" + Guid.NewGuid().ToString("N"));

        return (cls.Id, className);
    }

    [Fact]
    public async Task Anonymous_redirected_to_login()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await c.GetAsync("/Attendance/History");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Instructor_forbidden()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Instructor, "f");
        var resp = await client.GetAsync("/Attendance/History");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_sees_attendance_history()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "v");
        var (_, className) = await SeedLessonWithAttendanceAsync(factory);
        var resp = await client.GetAsync("/Attendance/History");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains(className, body);
        factory.Dispose();
    }

    [Fact]
    public async Task ListLessonHistory_returns_attendance_counts()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var (classId, _) = await SeedLessonWithAttendanceAsync(factory);
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<SchedulingService>();

        var rows = await svc.ListLessonHistoryAsync(classId, 50);

        var row = Assert.Single(rows);
        Assert.Equal(1, row.Present);
        Assert.Equal(1, row.Absent);
        Assert.Equal(2, row.Total);
        factory.Dispose();
    }
}
