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
    private static async Task<(int classId, string className, int lessonLogId, string presentName, string absentName)> SeedLessonWithAttendanceAsync(OrkabiAppFactory f)
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

        return (cls.Id, className, log.Id, c1.Name, c2.Name);
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
        var (_, className, _, _, _) = await SeedLessonWithAttendanceAsync(factory);
        var resp = await client.GetAsync("/Attendance/History");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains(className, body);
        factory.Dispose();
    }

    // ── R4: per-student drill-down + class filter ──────────────────────────────

    [Fact]
    public async Task ListAttendanceForLesson_returns_per_student_statuses()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var (_, _, lessonLogId, presentName, absentName) = await SeedLessonWithAttendanceAsync(factory);
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<SchedulingService>();

        var rows = await svc.ListAttendanceForLessonAsync(lessonLogId);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ClientName == presentName && r.Status == AttendanceStatus.Present);
        Assert.Contains(rows, r => r.ClientName == absentName && r.Status == AttendanceStatus.Absent);
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_sees_per_student_lesson_detail()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "detail");
        var (_, _, lessonLogId, presentName, absentName) = await SeedLessonWithAttendanceAsync(factory);

        var resp = await client.GetAsync($"/Attendance/Lesson/{lessonLogId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains(presentName, body);
        Assert.Contains(absentName, body);
        Assert.Contains("נוכח", body);   // present chip
        Assert.Contains("נעדר", body);   // absent chip
        factory.Dispose();
    }

    [Fact]
    public async Task Lesson_detail_instructor_forbidden()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Instructor, "detailf");
        var (_, _, lessonLogId, _, _) = await SeedLessonWithAttendanceAsync(factory);
        var resp = await client.GetAsync($"/Attendance/Lesson/{lessonLogId}");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task History_class_filter_narrows_to_selected_class()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "filter");
        var (classId, _, myLogId, _, _) = await SeedLessonWithAttendanceAsync(factory);
        var (_, _, otherLogId, _, _) = await SeedLessonWithAttendanceAsync(factory);   // a different class

        var body = WebUtility.HtmlDecode(await (await client.GetAsync($"/Attendance/History?ClassId={classId}")).Content.ReadAsStringAsync());

        Assert.Contains($"/Attendance/Lesson/{myLogId}", body);          // selected class's lesson shown
        Assert.DoesNotContain($"/Attendance/Lesson/{otherLogId}", body); // other class filtered out
        factory.Dispose();
    }

    [Fact]
    public async Task ListLessonHistory_returns_attendance_counts()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var (classId, _, _, _, _) = await SeedLessonWithAttendanceAsync(factory);
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
