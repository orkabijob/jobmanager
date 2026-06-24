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
/// Task 4 — the Outbox drainer + Real-Gap monitor wired into LessonLog save.
/// Verifies: same-transaction outbox write on CREATE; drain → gap-ticket pacing logic;
/// idempotency; processed-at stamping; and the IgnoreQueryFilters archived-template count.
/// </summary>
public class OutboxDrainerTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public OutboxDrainerTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<AppUser> SeedInstructorAsync(IServiceProvider sp)
    {
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var email = $"instructor-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, "Test@1234!");
        if (!result.Succeeded)
            throw new InvalidOperationException("Seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, AppRoles.Instructor);
        return user;
    }

    private static async Task<(School school, AcademicYear year, Class cls)> SeedSchoolYearClassAsync(AppDbContext db)
    {
        var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "תל אביב" };
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var year = new AcademicYear
        {
            Label = $"תשפ-{Guid.NewGuid():N}"[..10],
            StartDate = today,
            EndDate = today.AddDays(60),
            IsCurrent = false
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Class
        {
            Name = $"כיתה-{Guid.NewGuid():N}",
            School = school,
            AcademicYear = year,
            Status = EntityStatus.Active
        };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        return (school, year, cls);
    }

    private static async Task<Model> SeedModelAsync(AppDbContext db, int expected = 10)
    {
        var model = new Model
        {
            Name = $"מודל-{Guid.NewGuid():N}",
            ExpectedLessonsToComplete = expected
        };
        db.Models.Add(model);
        await db.SaveChangesAsync();
        return model;
    }

    private static async Task<ShiftTemplate> SeedTemplateDirectAsync(
        AppDbContext db, Class cls, AcademicYear year, AppUser instructor, DayOfWeek dow = DayOfWeek.Monday)
    {
        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DefaultInstructorId = instructor.Id,
            DayOfWeek = dow,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    // A ShiftInstance with a distinct date per call (the (TemplateId, Date) unique index).
    private static async Task<ShiftInstance> SeedInstanceAsync(
        AppDbContext db, int templateId, DateOnly date, int? actualInstructorId = null)
    {
        var instance = new ShiftInstance
        {
            TemplateId = templateId,
            Date = date,
            ActualInstructorId = actualInstructorId,
            Status = ShiftInstanceStatus.Scheduled
        };
        db.ShiftInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveLessonLog_writes_outbox_event_in_same_transaction()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instance = await SeedInstanceAsync(db, template.Id, today, instructor.Id);
        var model = await SeedModelAsync(db, expected: 10);

        var log = await svc.SaveLessonLogAsync(instance.Id, model.Id, LessonLogStatus.InProgress, null);

        db.ChangeTracker.Clear();
        // The LessonLog row exists.
        var savedLog = await db.LessonLogs.FindAsync(log.Id);
        Assert.NotNull(savedLog);

        // Exactly one outbox event was written in the same commit. Scope by this test's unique
        // modelId — the shared class-fixture DB also holds other tests' "LessonLogSaved" events.
        var evt = await db.OutboxEvents.SingleAsync(e =>
            e.EventType == "LessonLogSaved" && e.Payload.Contains($"\"modelId\":{model.Id}"));
        Assert.Null(evt.ProcessedAt);
        Assert.Null(evt.ScheduledFor);
        // Payload carries classId + modelId.
        Assert.Contains($"\"classId\":{cls.Id}", evt.Payload);
        Assert.Contains($"\"modelId\":{model.Id}", evt.Payload);
    }

    [Fact]
    public async Task Drain_creates_gap_ticket_when_over_pace()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var drainer = sp.GetRequiredService<IOutboxDrainer>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var model = await SeedModelAsync(db, expected: 3); // expected+1 = 4 → 5 InProgress lessons trips the gap

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        for (int i = 0; i < 5; i++)
        {
            var inst = await SeedInstanceAsync(db, template.Id, today.AddDays(i), instructor.Id);
            await svc.SaveLessonLogAsync(inst.Id, model.Id, LessonLogStatus.InProgress, null);
        }

        await drainer.DrainAsync();

        db.ChangeTracker.Clear();
        var gapCount = await db.ActionItems.CountAsync(a =>
            a.DeduplicationKey == $"gap_{cls.Id}_{model.Id}" && a.Status == ActionItemStatus.Open);
        Assert.Equal(1, gapCount);
    }

    [Fact]
    public async Task Drain_no_ticket_within_pace()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var drainer = sp.GetRequiredService<IOutboxDrainer>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var model = await SeedModelAsync(db, expected: 5); // threshold is spent > 6

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        for (int i = 0; i < 4; i++) // spent = 4, not over pace
        {
            var inst = await SeedInstanceAsync(db, template.Id, today.AddDays(i), instructor.Id);
            await svc.SaveLessonLogAsync(inst.Id, model.Id, LessonLogStatus.InProgress, null);
        }

        await drainer.DrainAsync();

        db.ChangeTracker.Clear();
        var gapCount = await db.ActionItems.CountAsync(a => a.DeduplicationKey == $"gap_{cls.Id}_{model.Id}");
        Assert.Equal(0, gapCount);
    }

    [Fact]
    public async Task Drain_no_ticket_when_model_completed()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var drainer = sp.GetRequiredService<IOutboxDrainer>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var model = await SeedModelAsync(db, expected: 3); // over-pace at 5+ lessons …

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        // 5 lessons, but one is Completed → already-completed suppresses the gap ticket.
        for (int i = 0; i < 5; i++)
        {
            var inst = await SeedInstanceAsync(db, template.Id, today.AddDays(i), instructor.Id);
            var status = i == 0 ? LessonLogStatus.Completed : LessonLogStatus.InProgress;
            await svc.SaveLessonLogAsync(inst.Id, model.Id, status, null);
        }

        await drainer.DrainAsync();

        db.ChangeTracker.Clear();
        var gapCount = await db.ActionItems.CountAsync(a => a.DeduplicationKey == $"gap_{cls.Id}_{model.Id}");
        Assert.Equal(0, gapCount);
    }

    [Fact]
    public async Task Drain_idempotent_second_call_no_duplicate_ticket()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var drainer = sp.GetRequiredService<IOutboxDrainer>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var model = await SeedModelAsync(db, expected: 3);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        for (int i = 0; i < 5; i++)
        {
            var inst = await SeedInstanceAsync(db, template.Id, today.AddDays(i), instructor.Id);
            await svc.SaveLessonLogAsync(inst.Id, model.Id, LessonLogStatus.InProgress, null);
        }

        // First drain processes the 5 events → 1 gap ticket. A SECOND drain finds nothing
        // unprocessed (all events already stamped) → still exactly 1 ticket.
        await drainer.DrainAsync();
        await drainer.DrainAsync();

        db.ChangeTracker.Clear();
        var gapCount = await db.ActionItems.CountAsync(a =>
            a.DeduplicationKey == $"gap_{cls.Id}_{model.Id}" && a.Status == ActionItemStatus.Open);
        Assert.Equal(1, gapCount);
    }

    [Fact]
    public async Task Drain_marks_processed_at()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var drainer = sp.GetRequiredService<IOutboxDrainer>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var model = await SeedModelAsync(db, expected: 10);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var inst = await SeedInstanceAsync(db, template.Id, today, instructor.Id);
        await svc.SaveLessonLogAsync(inst.Id, model.Id, LessonLogStatus.InProgress, null);

        await drainer.DrainAsync();

        db.ChangeTracker.Clear();
        // Scope by this test's unique modelId — the shared class-fixture DB holds other tests' events.
        var evt = await db.OutboxEvents.SingleAsync(e =>
            e.EventType == "LessonLogSaved" && e.Payload.Contains($"\"modelId\":{model.Id}"));
        Assert.NotNull(evt.ProcessedAt);
    }

    [Fact]
    public async Task Drain_counts_lessons_under_archived_template()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();
        var drainer = sp.GetRequiredService<IOutboxDrainer>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var model = await SeedModelAsync(db, expected: 3);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        for (int i = 0; i < 5; i++)
        {
            var inst = await SeedInstanceAsync(db, template.Id, today.AddDays(i), instructor.Id);
            await svc.SaveLessonLogAsync(inst.Id, model.Id, LessonLogStatus.InProgress, null);
        }

        // Archive the template AFTER logging. The ShiftTemplate global filter would otherwise
        // drop these lessons from the spent-count — IgnoreQueryFilters in the drainer keeps them.
        template.Status = EntityStatus.Archived;
        await db.SaveChangesAsync();

        await drainer.DrainAsync();

        db.ChangeTracker.Clear();
        var gapCount = await db.ActionItems.CountAsync(a =>
            a.DeduplicationKey == $"gap_{cls.Id}_{model.Id}" && a.Status == ActionItemStatus.Open);
        Assert.Equal(1, gapCount);
    }
}
