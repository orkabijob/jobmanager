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

public class SchedulingServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public SchedulingServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

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

    private static async Task<Modules.Curriculum.Model> SeedModelAsync(AppDbContext db, int expected = 10)
    {
        var model = new Modules.Curriculum.Model
        {
            Name = $"מודל-{Guid.NewGuid():N}",
            ExpectedLessonsToComplete = expected
        };
        db.Models.Add(model);
        await db.SaveChangesAsync();
        return model;
    }

    // Creates a ShiftInstance with the given date and ActualInstructorId — bypasses the generator.
    private static async Task<ShiftInstance> SeedInstanceAsync(
        AppDbContext db,
        int templateId,
        DateOnly date,
        int? actualInstructorId = null,
        ShiftInstanceStatus status = ShiftInstanceStatus.Scheduled)
    {
        var instance = new ShiftInstance
        {
            TemplateId = templateId,
            Date = date,
            ActualInstructorId = actualInstructorId,
            Status = status
        };
        db.ShiftInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    private static async Task<ShiftTemplate> SeedTemplateDirectAsync(
        AppDbContext db,
        Class cls,
        AcademicYear year,
        AppUser instructor,
        DayOfWeek dow = DayOfWeek.Monday)
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

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CanAccessShift_true_only_for_assigned_instructor_on_today()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var other = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instance = await SeedInstanceAsync(db, template.Id, today, instructor.Id);

        // True: correct instructor, today.
        Assert.True(await svc.CanAccessShiftAsync(instance.Id, instructor.Id));
        // False: wrong instructor.
        Assert.False(await svc.CanAccessShiftAsync(instance.Id, other.Id));
        // False: non-existent instance.
        Assert.False(await svc.CanAccessShiftAsync(99999, instructor.Id));
    }

    [Fact]
    public async Task SaveLessonLog_captures_snapshot_and_later_model_change_does_not_alter_it()
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
        var model = await SeedModelAsync(db, expected: 7);

        // Create the lesson log — snapshot should be captured as 7.
        var log = await svc.SaveLessonLogAsync(instance.Id, model.Id, LessonLogStatus.InProgress, null);
        Assert.Equal(7, log.ExpectedLessonsSnapshot);

        // Change the model's ExpectedLessonsToComplete.
        model.ExpectedLessonsToComplete = 99;
        await db.SaveChangesAsync();

        // Update the log (same instance, same model) — snapshot must remain 7, not 99.
        var updated = await svc.SaveLessonLogAsync(instance.Id, model.Id, LessonLogStatus.Completed, "updated");

        db.ChangeTracker.Clear();
        var reloaded = await db.LessonLogs.FindAsync(updated.Id);
        Assert.Equal(7, reloaded!.ExpectedLessonsSnapshot);
    }

    [Fact]
    public async Task Approve_substitution_sets_actual_instructor_atomically()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var substitute = await SeedInstructorAsync(sp);
        var approver = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instance = await SeedInstanceAsync(db, template.Id, today, instructor.Id);

        // Create a pending substitution request.
        var request = await svc.RequestSubstitutionAsync(instance.Id, instructor.Id, substitute.Id);
        Assert.Equal(SubstitutionStatus.Pending, request.Status);

        // Approve it.
        await svc.ApproveSubstitutionAsync(request.Id, approver.Id);

        // Reload in a fresh context to verify atomic commit.
        db.ChangeTracker.Clear();
        var updatedRequest = await db.SubstitutionRequests
            .Include(r => r.ShiftInstance)
            .SingleAsync(r => r.Id == request.Id);

        Assert.Equal(SubstitutionStatus.Approved, updatedRequest.Status);
        Assert.Equal(approver.Id, updatedRequest.ApprovedByUserId);
        Assert.NotNull(updatedRequest.ApprovedAt);
        // The shift instance's actual instructor must now be the substitute.
        Assert.Equal(substitute.Id, updatedRequest.ShiftInstance.ActualInstructorId);
    }

    // ── R5: pending substitution requests surface to the Admin queue ──

    [Fact]
    public async Task RequestSubstitution_creates_admin_pending_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var substitute = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instance = await SeedInstanceAsync(db, template.Id, today, instructor.Id);

        var request = await svc.RequestSubstitutionAsync(instance.Id, instructor.Id, substitute.Id);

        db.ChangeTracker.Clear();
        var item = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == $"sub_request_{request.Id}");
        Assert.Equal(Modules.Identity.AppRoles.Admin, item.AssignedToRole);
        Assert.Equal(Modules.ActionHub.ActionItemStatus.Open, item.Status);
        Assert.Equal(request.Id, item.RelatedEntityId);
    }

    [Fact]
    public async Task Approving_substitution_resolves_the_admin_pending_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var substitute = await SeedInstructorAsync(sp);
        var approver = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instance = await SeedInstanceAsync(db, template.Id, today, instructor.Id);

        var request = await svc.RequestSubstitutionAsync(instance.Id, instructor.Id, substitute.Id);
        await svc.ApproveSubstitutionAsync(request.Id, approver.Id);

        db.ChangeTracker.Clear();
        var item = await db.ActionItems.SingleAsync(
            a => a.RelatedEntityId == request.Id && a.AssignedToRole == Modules.Identity.AppRoles.Admin);
        Assert.Equal(Modules.ActionHub.ActionItemStatus.Resolved, item.Status);   // no longer clutters the queue
    }

    [Fact]
    public async Task SubmitAttendance_idempotency_key_returns_existing_on_retry()
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
        var model = await SeedModelAsync(db);
        var log = await svc.SaveLessonLogAsync(instance.Id, model.Id, LessonLogStatus.InProgress, null);

        // Enroll one client in the class.
        var client = new Client { Name = $"תלמיד-{Guid.NewGuid():N}" };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        db.Enrollments.Add(new Enrollment { ClassId = cls.Id, ClientId = client.Id, EnrolledAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var key = "idem-" + Guid.NewGuid().ToString("N");
        var marks = new[] { (client.Id, AttendanceStatus.Present) };

        // First call — should create.
        var first = await svc.SubmitAttendanceAsync(log.Id, marks, key);
        Assert.Single(first);

        // Second call with same key — must NOT throw; should return existing.
        var second = await svc.SubmitAttendanceAsync(log.Id, marks, key);
        Assert.Single(second);
        Assert.Equal(first[0].Id, second[0].Id);
    }

    [Fact]
    public async Task Attendance_uses_class_enrollments()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var enrollmentSvc = sp.GetRequiredService<EnrollmentService>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instance = await SeedInstanceAsync(db, template.Id, today, instructor.Id);
        var model = await SeedModelAsync(db);
        var log = await svc.SaveLessonLogAsync(instance.Id, model.Id, LessonLogStatus.InProgress, null);

        // Enroll two clients in the class.
        var clientA = new Client { Name = $"א-{Guid.NewGuid():N}" };
        var clientB = new Client { Name = $"ב-{Guid.NewGuid():N}" };
        db.Clients.AddRange(clientA, clientB);
        await db.SaveChangesAsync();
        db.Enrollments.Add(new Enrollment { ClassId = cls.Id, ClientId = clientA.Id, EnrolledAt = DateTime.UtcNow });
        db.Enrollments.Add(new Enrollment { ClassId = cls.Id, ClientId = clientB.Id, EnrolledAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // The attendable roster for this lesson log == EnrollmentService.ListByClassAsync(classId) client IDs.
        var enrolledIds = (await enrollmentSvc.ListByClassAsync(cls.Id))
            .Select(e => e.ClientId)
            .OrderBy(x => x)
            .ToList();

        // Submit attendance for both enrolled clients.
        var marks = enrolledIds.Select(id => (id, AttendanceStatus.Present)).ToList();
        var attendances = await svc.SubmitAttendanceAsync(log.Id, marks, "key-" + Guid.NewGuid());

        var recordedClientIds = attendances.Select(a => a.ClientId).OrderBy(x => x).ToList();

        Assert.Equal(enrolledIds, recordedClientIds);
    }

    // ── Substitution cancel (regression coverage for the instructor self-cancel path) ──

    [Fact]
    public async Task Cancel_substitution_sets_status_cancelled()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var substitute = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var future = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz)).AddDays(7);
        var instance = await SeedInstanceAsync(db, template.Id, future, instructor.Id);

        var request = await svc.RequestSubstitutionAsync(instance.Id, instructor.Id, substitute.Id);

        await svc.CancelSubstitutionAsync(request.Id, instructor.Id);

        db.ChangeTracker.Clear();
        var reloaded = await db.SubstitutionRequests.FindAsync(request.Id);
        Assert.Equal(SubstitutionStatus.Cancelled, reloaded!.Status);
    }

    [Fact]
    public async Task Cancel_substitution_by_non_owner_throws_and_leaves_pending()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var substitute = await SeedInstructorAsync(sp);
        var stranger = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var future = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz)).AddDays(7);
        var instance = await SeedInstanceAsync(db, template.Id, future, instructor.Id);

        var request = await svc.RequestSubstitutionAsync(instance.Id, instructor.Id, substitute.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CancelSubstitutionAsync(request.Id, stranger.Id));

        db.ChangeTracker.Clear();
        var reloaded = await db.SubstitutionRequests.FindAsync(request.Id);
        Assert.Equal(SubstitutionStatus.Pending, reloaded!.Status);
    }

    [Fact]
    public async Task Cancel_substitution_non_pending_throws()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var substitute = await SeedInstructorAsync(sp);
        var approver = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var future = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz)).AddDays(7);
        var instance = await SeedInstanceAsync(db, template.Id, future, instructor.Id);

        var request = await svc.RequestSubstitutionAsync(instance.Id, instructor.Id, substitute.Id);
        await svc.ApproveSubstitutionAsync(request.Id, approver.Id);   // now Approved, no longer Pending

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CancelSubstitutionAsync(request.Id, instructor.Id));
    }

    // ── F20: "first incomplete model" resolver (progression no longer frozen at model #1) ──

    private static async Task<Syllabus> SeedTwoModelSyllabusAsync(
        AppDbContext db, Class cls, Modules.Curriculum.Model modelA, Modules.Curriculum.Model modelB)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var syllabus = new Syllabus
        {
            Name = $"syl-{Guid.NewGuid():N}"[..12],
            StartDate = today.AddDays(-30),
            EndDate = today.AddDays(120),
            Status = EntityStatus.Active
        };
        db.Syllabi.Add(syllabus);
        await db.SaveChangesAsync();
        db.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabus.Id, ModelId = modelA.Id, OrderIndex = 1 });
        db.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabus.Id, ModelId = modelB.Id, OrderIndex = 2 });
        cls.SyllabusId = syllabus.Id;
        await db.SaveChangesAsync();
        return syllabus;
    }

    [Fact]
    public async Task ResolveCurrentModel_returns_null_for_class_without_syllabus()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);   // no syllabus assigned

        var (modelId, modelName) = await svc.ResolveCurrentModelForClassAsync(cls.Id);

        Assert.Null(modelId);      // drives the attendance "טרם שובץ דגם" state + submit-disable
        Assert.Null(modelName);
    }

    [Fact]
    public async Task ResolveCurrentModel_advances_past_completed_models()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var modelA = await SeedModelAsync(db, expected: 2);
        var modelB = await SeedModelAsync(db, expected: 3);
        await SeedTwoModelSyllabusAsync(db, cls, modelA, modelB);

        // Nothing completed yet → current is the first model (A).
        var (firstId, _) = await svc.ResolveCurrentModelForClassAsync(cls.Id);
        Assert.Equal(modelA.Id, firstId);

        // Complete modelA's expected (2) lessons for this class.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        for (int i = 0; i < 2; i++)
        {
            var inst = await SeedInstanceAsync(db, template.Id, today.AddDays(-10 + i), instructor.Id);
            await svc.SaveLessonLogAsync(inst.Id, modelA.Id, LessonLogStatus.Completed, null);
        }

        // Model A is complete → current advances to B (was frozen at A before F20).
        var (nextId, _) = await svc.ResolveCurrentModelForClassAsync(cls.Id);
        Assert.Equal(modelB.Id, nextId);
    }

    [Fact]
    public async Task ResolveLessonLog_creates_log_for_first_incomplete_model()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var modelA = await SeedModelAsync(db, expected: 1);
        var modelB = await SeedModelAsync(db, expected: 3);
        await SeedTwoModelSyllabusAsync(db, cls, modelA, modelB);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var doneInst = await SeedInstanceAsync(db, template.Id, today.AddDays(-5), instructor.Id);
        await svc.SaveLessonLogAsync(doneInst.Id, modelA.Id, LessonLogStatus.Completed, null);   // model A complete (expected 1)

        // A fresh shift with no log → resolver should pick model B (A is done), not model #1.
        var newInst = await SeedInstanceAsync(db, template.Id, today, instructor.Id);
        var (logId, modelId, _) = await svc.ResolveLessonLogForAttendanceAsync(newInst.Id);
        Assert.NotNull(logId);
        Assert.Equal(modelB.Id, modelId);
    }

    [Fact]
    public async Task ResolveCurrentModel_falls_back_to_last_when_all_complete()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var modelA = await SeedModelAsync(db, expected: 1);
        var modelB = await SeedModelAsync(db, expected: 1);
        await SeedTwoModelSyllabusAsync(db, cls, modelA, modelB);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var instA = await SeedInstanceAsync(db, template.Id, today.AddDays(-4), instructor.Id);
        await svc.SaveLessonLogAsync(instA.Id, modelA.Id, LessonLogStatus.Completed, null);
        var instB = await SeedInstanceAsync(db, template.Id, today.AddDays(-3), instructor.Id);
        await svc.SaveLessonLogAsync(instB.Id, modelB.Id, LessonLogStatus.Completed, null);

        // Every model complete → fall back to the last model (B), not null.
        var (id, _) = await svc.ResolveCurrentModelForClassAsync(cls.Id);
        Assert.Equal(modelB.Id, id);
    }

    // ── F14: substitution-approval notifies the substitute + the original instructor ──

    [Fact]
    public async Task ApproveSubstitution_notifies_substitute_and_original_instructor()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SchedulingService>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(sp);
        var substitute = await SeedInstructorAsync(sp);
        var approver = await SeedInstructorAsync(sp);
        var template = await SeedTemplateDirectAsync(db, cls, year, instructor);
        var future = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz)).AddDays(7);
        var instance = await SeedInstanceAsync(db, template.Id, future, instructor.Id);

        var request = await svc.RequestSubstitutionAsync(instance.Id, instructor.Id, substitute.Id);
        await svc.ApproveSubstitutionAsync(request.Id, approver.Id);

        db.ChangeTracker.Clear();
        var subItem = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == $"sub_assigned_{request.Id}");
        Assert.NotNull(subItem);
        Assert.Equal(substitute.Id, subItem!.AssignedToUserId);
        Assert.Null(subItem.AssignedToRole);
        Assert.Equal(ActionItemStatus.Open, subItem.Status);

        var origItem = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == $"sub_reassigned_{request.Id}");
        Assert.NotNull(origItem);
        Assert.Equal(instructor.Id, origItem!.AssignedToUserId);
        Assert.Equal(ActionItemStatus.Open, origItem.Status);
    }
}
