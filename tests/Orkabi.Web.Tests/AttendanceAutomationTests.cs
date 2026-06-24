using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// Task 6 — event-driven attendance automations.
/// Verifies: double-absence outbox event → drain → DoubleAbsence ActionItem;
/// idempotency-key retry path does NOT fire a second event; tryout Present → deferred
/// TryoutFollowup ActionItem; non-tryout Present → no event.
/// </summary>
public class AttendanceAutomationTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public AttendanceAutomationTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<AppUser> SeedInstructorAsync(IServiceProvider sp)
    {
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var email = $"instr-auto-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, "Passw0rd!");
        if (!result.Succeeded)
            throw new InvalidOperationException("Seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, AppRoles.Instructor);
        return user;
    }

    /// <summary>
    /// Seeds a full class + two shift instances on consecutive dates, each with their own
    /// LessonLog. Returns (classId, client, lessonLogId1, lessonLogId2). date1 < date2.
    /// </summary>
    private static async Task<(int classId, int clientId, int lessonLogId1, int lessonLogId2)>
        SeedTwoConsecutiveLessonsAsync(AppDbContext db, IServiceProvider sp)
    {
        var instructor = await SeedInstructorAsync(sp);
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));

        var school = new School { Name = $"s-{Guid.NewGuid():N}"[..12], City = "TLV" };
        var year = new AcademicYear
        {
            Label = $"y-{Guid.NewGuid():N}"[..10],
            StartDate = today.AddDays(-60),
            EndDate = today.AddDays(60),
            IsCurrent = false
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var model = new Model { Name = $"m-{Guid.NewGuid():N}"[..12], ExpectedLessonsToComplete = 10 };
        db.Models.Add(model);
        var syllabus = new Syllabus
        {
            Name = $"syl-{Guid.NewGuid():N}"[..12],
            StartDate = today.AddDays(-60),
            EndDate = today.AddDays(60),
            Status = EntityStatus.Active
        };
        db.Syllabi.Add(syllabus);
        await db.SaveChangesAsync();
        db.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabus.Id, ModelId = model.Id, OrderIndex = 1 });
        await db.SaveChangesAsync();

        var cls = new Class
        {
            Name = $"cls-{Guid.NewGuid():N}"[..12],
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
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        // Two shift instances on different dates (date ordering matters for the previous-attendance query).
        var inst1 = new ShiftInstance
        {
            TemplateId = template.Id,
            ActualInstructorId = instructor.Id,
            Date = today.AddDays(-2),
            Status = ShiftInstanceStatus.Scheduled
        };
        var inst2 = new ShiftInstance
        {
            TemplateId = template.Id,
            ActualInstructorId = instructor.Id,
            Date = today.AddDays(-1),
            Status = ShiftInstanceStatus.Scheduled
        };
        db.ShiftInstances.AddRange(inst1, inst2);
        await db.SaveChangesAsync();

        var log1 = new LessonLog
        {
            ShiftInstanceId = inst1.Id,
            ModelId = model.Id,
            Status = LessonLogStatus.InProgress,
            ExpectedLessonsSnapshot = model.ExpectedLessonsToComplete
        };
        var log2 = new LessonLog
        {
            ShiftInstanceId = inst2.Id,
            ModelId = model.Id,
            Status = LessonLogStatus.InProgress,
            ExpectedLessonsSnapshot = model.ExpectedLessonsToComplete
        };
        db.LessonLogs.AddRange(log1, log2);
        await db.SaveChangesAsync();

        var client = new Client { Name = $"cli-{Guid.NewGuid():N}"[..10], IsActive = true };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        db.Enrollments.Add(new Enrollment
        {
            ClassId = cls.Id,
            ClientId = client.Id,
            Status = EnrollmentStatus.Active,
            EnrolledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return (cls.Id, client.Id, log1.Id, log2.Id);
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// TC1: Two consecutive Absent attendances (lesson1 earlier, lesson2 later in the same class)
    /// → drain → exactly ONE DoubleAbsence ActionItem created (dedup).
    /// </summary>
    [Fact]
    public async Task TwoConsecutiveAbsences_drain_creates_exactly_one_double_absence_action_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var drainer = sp.GetRequiredService<IOutboxDrainer>();

        var (classId, clientId, logId1, logId2) = await SeedTwoConsecutiveLessonsAsync(db, sp);

        // First lesson: Absent
        await svc.RecordAttendanceAsync(logId1, clientId, AttendanceStatus.Absent, $"key-tc1a-{Guid.NewGuid():N}");
        // Second lesson: Absent
        await svc.RecordAttendanceAsync(logId2, clientId, AttendanceStatus.Absent, $"key-tc1b-{Guid.NewGuid():N}");

        await drainer.DrainAsync();

        db.ChangeTracker.Clear();
        var count = await db.ActionItems.CountAsync(a =>
            a.DeduplicationKey == $"absence_double_{clientId}_{classId}" &&
            a.Status == ActionItemStatus.Open);
        Assert.Equal(1, count);
    }

    /// <summary>
    /// TC2: Absent on lesson1 then Present on lesson2 → NO double-absence ActionItem.
    /// </summary>
    [Fact]
    public async Task AbsentThenPresent_drain_creates_no_double_absence_action_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var drainer = sp.GetRequiredService<IOutboxDrainer>();

        var (classId, clientId, logId1, logId2) = await SeedTwoConsecutiveLessonsAsync(db, sp);

        // First lesson: Absent; second: Present
        await svc.RecordAttendanceAsync(logId1, clientId, AttendanceStatus.Absent, $"key-tc2a-{Guid.NewGuid():N}");
        await svc.RecordAttendanceAsync(logId2, clientId, AttendanceStatus.Present, $"key-tc2b-{Guid.NewGuid():N}");

        await drainer.DrainAsync();

        db.ChangeTracker.Clear();
        var count = await db.ActionItems.CountAsync(a =>
            a.DeduplicationKey == $"absence_double_{clientId}_{classId}");
        Assert.Equal(0, count);
    }

    /// <summary>
    /// TC3: Idempotency-key retry path — submitting the SAME client+lessonLog+key twice must
    /// produce exactly ONE AttendanceAbsent outbox event (no double-fire).
    /// </summary>
    [Fact]
    public async Task IdempotencyRetry_does_not_produce_duplicate_outbox_event()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, clientId, logId1, _) = await SeedTwoConsecutiveLessonsAsync(db, sp);

        var idempotencyKey = $"key-idem-{Guid.NewGuid():N}";

        // First call: creates attendance row + outbox event.
        await svc.RecordAttendanceAsync(logId1, clientId, AttendanceStatus.Absent, idempotencyKey);
        // Second call with SAME key: idempotency-key retry → returns existing row, NO new event.
        await svc.RecordAttendanceAsync(logId1, clientId, AttendanceStatus.Absent, idempotencyKey);

        db.ChangeTracker.Clear();
        var eventCount = await db.OutboxEvents.CountAsync(e =>
            e.EventType == "AttendanceAbsent" && e.Payload.Contains($"\"clientId\":{clientId}") && e.Payload.Contains($"\"lessonLogId\":{logId1}"));
        Assert.Equal(1, eventCount);
    }

    /// <summary>
    /// TC4: Tryout Present → OutboxEvent with future ScheduledFor → (set to past) → drain →
    /// TryoutFollowup ActionItem created.
    /// </summary>
    [Fact]
    public async Task TryoutPresent_deferred_drain_creates_tryout_followup_action_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var drainer = sp.GetRequiredService<IOutboxDrainer>();

        var (classId, clientId, logId1, _) = await SeedTwoConsecutiveLessonsAsync(db, sp);

        // Change the enrollment to Tryout status.
        var enrollment = await db.Enrollments.FirstAsync(e => e.ClientId == clientId && e.ClassId == classId);
        enrollment.Status = EnrollmentStatus.Tryout;
        await db.SaveChangesAsync();

        // Record Present for a tryout client.
        await svc.RecordAttendanceAsync(logId1, clientId, AttendanceStatus.Present, $"key-tryout-{Guid.NewGuid():N}");

        db.ChangeTracker.Clear();
        // Verify a TryoutPresent event was written with a FUTURE ScheduledFor.
        var evt = await db.OutboxEvents.SingleAsync(e =>
            e.EventType == "TryoutPresent" && e.Payload.Contains($"\"clientId\":{clientId}"));
        Assert.NotNull(evt.ScheduledFor);
        Assert.True(evt.ScheduledFor > DateTime.UtcNow, "ScheduledFor should be in the future (tomorrow 08:00 Israel)");

        // Simulate time passing: backdate the ScheduledFor so the drainer picks it up.
        evt.ScheduledFor = DateTime.UtcNow.AddHours(-1);
        db.OutboxEvents.Update(evt);
        await db.SaveChangesAsync();

        await drainer.DrainAsync();

        db.ChangeTracker.Clear();
        var count = await db.ActionItems.CountAsync(a =>
            a.DeduplicationKey == $"tryout_followup_{clientId}_{classId}" &&
            a.Status == ActionItemStatus.Open);
        Assert.Equal(1, count);
    }

    /// <summary>
    /// TC5: Non-tryout Present → NO TryoutPresent outbox event written.
    /// </summary>
    [Fact]
    public async Task NonTryoutPresent_produces_no_tryout_outbox_event()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (classId, clientId, logId1, _) = await SeedTwoConsecutiveLessonsAsync(db, sp);

        // Enrollment stays Active (not Tryout).
        await svc.RecordAttendanceAsync(logId1, clientId, AttendanceStatus.Present, $"key-nontryout-{Guid.NewGuid():N}");

        db.ChangeTracker.Clear();
        var count = await db.OutboxEvents.CountAsync(e =>
            e.EventType == "TryoutPresent" && e.Payload.Contains($"\"clientId\":{clientId}"));
        Assert.Equal(0, count);
    }
}
