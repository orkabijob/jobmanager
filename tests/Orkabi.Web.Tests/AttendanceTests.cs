using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class AttendanceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public AttendanceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));

    private static async Task<AppUser> SeedInstructorAsync(IServiceProvider sp)
    {
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var email = $"instr-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, "Passw0rd!");
        if (!result.Succeeded)
            throw new InvalidOperationException("Seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, AppRoles.Instructor);
        return user;
    }

    /// <summary>
    /// Seeds a class WITH a syllabus + one model, a template, and a shift instance for the
    /// given instructor on the given date. Enrolls two clients. Returns the key ids.
    /// </summary>
    private static async Task<(int shiftInstanceId, int classId, int clientA, int clientB)>
        SeedShiftWithRosterAsync(AppDbContext db, AppUser instructor, DateOnly date)
    {
        var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "תל אביב" };
        var year = new AcademicYear
        {
            Label = $"שנה-{Guid.NewGuid():N}"[..9],
            StartDate = Today.AddDays(-30),
            EndDate = Today.AddDays(120),
            IsCurrent = false
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        // Syllabus + model so the lesson-model can be resolved for attendance.
        var model = new Model { Name = $"דגם-{Guid.NewGuid():N}"[..12], ExpectedLessonsToComplete = 8 };
        db.Models.Add(model);
        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}"[..12],
            StartDate = Today.AddDays(-30),
            EndDate = Today.AddDays(120),
            Status = EntityStatus.Active
        };
        db.Syllabi.Add(syllabus);
        await db.SaveChangesAsync();
        db.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabus.Id, ModelId = model.Id, OrderIndex = 1 });
        await db.SaveChangesAsync();

        var cls = new Class
        {
            Name = $"כיתה-{Guid.NewGuid():N}"[..12],
            School = school,
            AcademicYear = year,
            SyllabusId = syllabus.Id,
            Status = EntityStatus.Active
        };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DefaultInstructorId = instructor.Id,
            DayOfWeek = date.DayOfWeek,
            StartTime = new TimeOnly(16, 0),
            EndTime = new TimeOnly(17, 30),
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        var instance = new ShiftInstance
        {
            TemplateId = template.Id,
            ActualInstructorId = instructor.Id,
            Date = date,
            Status = ShiftInstanceStatus.Scheduled
        };
        db.ShiftInstances.Add(instance);
        await db.SaveChangesAsync();

        var a = new Client { Name = $"א-{Guid.NewGuid():N}"[..10], IsActive = true };
        var b = new Client { Name = $"ב-{Guid.NewGuid():N}"[..10], IsActive = true };
        db.Clients.AddRange(a, b);
        await db.SaveChangesAsync();
        db.Enrollments.Add(new Enrollment { ClassId = cls.Id, ClientId = a.Id, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow });
        db.Enrollments.Add(new Enrollment { ClassId = cls.Id, ClientId = b.Id, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        return (instance.Id, cls.Id, a.Id, b.Id);
    }

    // ── (b)/(c) date-scope guard ───────────────────────────────────────────────

    [Fact]
    public async Task Instructor_can_open_own_today_shift()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int shiftId, clientA; string email, clientAName;
        using (var s = factory.Services.CreateScope())
        {
            var sp = s.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var me = await SeedInstructorAsync(sp);
            email = me.Email!;
            (shiftId, _, clientA, _) = await SeedShiftWithRosterAsync(db, me, Today);
            clientAName = (await db.Clients.FindAsync(clientA))!.Name;
        }

        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        var resp = await client.GetAsync($"/Attendance/{shiftId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains(clientAName, body);                          // roster row rendered
        Assert.Contains("att-row", body);                           // the signature tap row
        Assert.Contains("שמור נוכחות", body);                       // the submit button
        Assert.Contains($"data-client-id=\"{clientA}\"", body);     // JS hook present
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_forbidden_on_another_instructors_shift()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int shiftId; string meEmail;
        using (var s = factory.Services.CreateScope())
        {
            var sp = s.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var me = await SeedInstructorAsync(sp);
            meEmail = me.Email!;
            var other = await SeedInstructorAsync(sp);
            (shiftId, _, _, _) = await SeedShiftWithRosterAsync(db, other, Today);
        }

        var client = await TestLogin.SignInAsync(factory, meEmail, "Passw0rd!");
        var resp = await client.GetAsync($"/Attendance/{shiftId}");
        // Forbid() on a cookie-authed Razor page is a 302 to AccessDenied (repo convention).
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_forbidden_on_non_today_shift()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int shiftId; string email;
        using (var s = factory.Services.CreateScope())
        {
            var sp = s.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var me = await SeedInstructorAsync(sp);
            email = me.Email!;
            (shiftId, _, _, _) = await SeedShiftWithRosterAsync(db, me, Today.AddDays(1));   // tomorrow
        }

        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        var resp = await client.GetAsync($"/Attendance/{shiftId}");
        // Date-scope guard fails (tomorrow) → Forbid() → 302 to AccessDenied (repo convention).
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    // ── (d) POST /api/attendance persists + idempotency ─────────────────────────

    private sealed record MarkDto(int clientId, string status);
    private sealed record AttBody(int shiftInstanceId, MarkDto[] marks, string idempotencyKey);

    [Fact]
    public async Task Api_attendance_persists_marks_and_is_idempotent_on_repeat_key()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int shiftId, clientA, clientB; string email;
        using (var s = factory.Services.CreateScope())
        {
            var sp = s.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var me = await SeedInstructorAsync(sp);
            email = me.Email!;
            (shiftId, _, clientA, clientB) = await SeedShiftWithRosterAsync(db, me, Today);
        }

        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");

        // Open the attendance page to mint the antiforgery token (hidden input + cookie).
        var pageResp = await client.GetAsync($"/Attendance/{shiftId}");
        Assert.Equal(HttpStatusCode.OK, pageResp.StatusCode);
        var token = AntiForgery.Extract(await pageResp.Content.ReadAsStringAsync());

        var key = "att-" + Guid.NewGuid().ToString("N");
        var body = new AttBody(shiftId, new[]
        {
            new MarkDto(clientA, "Present"),
            new MarkDto(clientB, "Absent"),
        }, key);

        async Task<HttpResponseMessage> Post()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/attendance")
            {
                Content = JsonContent.Create(body)
            };
            req.Headers.Add("RequestVerificationToken", token);
            return await client.SendAsync(req);
        }

        var first = await Post();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Verify both marks persisted.
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var log = await db.LessonLogs.FirstAsync(l => l.ShiftInstanceId == shiftId);
            var rows = await db.Attendances.Where(a => a.LessonLogId == log.Id).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal(AttendanceStatus.Present, rows.Single(r => r.ClientId == clientA).Status);
            Assert.Equal(AttendanceStatus.Absent, rows.Single(r => r.ClientId == clientB).Status);
        }

        // Repeat with the SAME idempotency key — must not duplicate, must not 500.
        var second = await Post();
        Assert.NotEqual(HttpStatusCode.InternalServerError, second.StatusCode);
        Assert.True(second.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"expected 200 or 409, got {(int)second.StatusCode}");

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var log = await db.LessonLogs.FirstAsync(l => l.ShiftInstanceId == shiftId);
            var count = await db.Attendances.CountAsync(a => a.LessonLogId == log.Id);
            Assert.Equal(2, count);   // still 2, not 4
        }

        factory.Dispose();
    }

    [Fact]
    public async Task Api_attendance_forbidden_on_another_instructors_shift()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int otherShiftId, clientA; string meEmail; int myShiftId;
        using (var s = factory.Services.CreateScope())
        {
            var sp = s.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var me = await SeedInstructorAsync(sp);
            meEmail = me.Email!;
            var other = await SeedInstructorAsync(sp);
            (myShiftId, _, _, _) = await SeedShiftWithRosterAsync(db, me, Today);
            (otherShiftId, _, clientA, _) = await SeedShiftWithRosterAsync(db, other, Today);
        }

        var client = await TestLogin.SignInAsync(factory, meEmail, "Passw0rd!");
        // Mint a valid token from a page I CAN access.
        var pageResp = await client.GetAsync($"/Attendance/{myShiftId}");
        var token = AntiForgery.Extract(await pageResp.Content.ReadAsStringAsync());

        var body = new AttBody(otherShiftId, new[] { new MarkDto(clientA, "Present") }, "att-" + Guid.NewGuid().ToString("N"));
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/attendance") { Content = JsonContent.Create(body) };
        req.Headers.Add("RequestVerificationToken", token);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        factory.Dispose();
    }

    [Fact]
    public async Task Api_attendance_requires_auth()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var anon = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var body = new AttBody(1, new[] { new MarkDto(1, "Present") }, "k");
        var resp = await anon.PostAsJsonAsync("/api/attendance", body);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);   // /api/* seam → 401, not redirect
        factory.Dispose();
    }

    // ── (e) lesson-log pacing partial ───────────────────────────────────────────

    [Fact]
    public async Task Lesson_log_pacing_partial_renders_x_of_n()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int shiftId, classId; string email; int modelId;
        using (var s = factory.Services.CreateScope())
        {
            var sp = s.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var me = await SeedInstructorAsync(sp);
            email = me.Email!;
            (shiftId, classId, _, _) = await SeedShiftWithRosterAsync(db, me, Today);
            // resolve the single model on the class's syllabus
            modelId = await db.SyllabusModels
                .Where(sm => sm.Syllabus.Classes.Any(c => c.Id == classId))
                .Select(sm => sm.ModelId)
                .FirstAsync();
        }

        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");

        var logResp = await client.GetAsync($"/Attendance/{shiftId}/Log");
        Assert.Equal(HttpStatusCode.OK, logResp.StatusCode);
        var token = AntiForgery.Extract(await logResp.Content.ReadAsStringAsync());

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ModelId"] = modelId.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var paceResp = await client.PostAsync($"/Attendance/{shiftId}/Log?handler=Pace", form);
        Assert.Equal(HttpStatusCode.OK, paceResp.StatusCode);
        var body = WebUtility.HtmlDecode(await paceResp.Content.ReadAsStringAsync());
        Assert.Contains("מתוך", body);   // "שיעור X מתוך N"
        Assert.Contains("8", body);       // expected N from the seeded model

        factory.Dispose();
    }
}
