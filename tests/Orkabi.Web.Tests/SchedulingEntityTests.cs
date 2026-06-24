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

public class SchedulingEntityTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public SchedulingEntityTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Shared arrange helpers ───────────────────────────────────────────────

    private static async Task<AppUser> SeedInstructorAsync(IServiceProvider sp)
    {
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var email = $"instructor-{Guid.NewGuid():N}@test.local";
        var instructor = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(instructor, "Test@1234!");
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "SeedInstructorAsync failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(instructor, AppRoles.Instructor);
        return instructor;
    }

    private static async Task<(School school, AcademicYear year, Class cls)> SeedSchoolYearClassAsync(AppDbContext db)
    {
        var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "תל אביב" };
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

    private static async Task<ShiftTemplate> SeedTemplateAsync(
        AppDbContext db,
        Class cls,
        AcademicYear year,
        AppUser instructor,
        EntityStatus status = EntityStatus.Active,
        TimeOnly? start = null,
        TimeOnly? end = null)
    {
        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DefaultInstructorId = instructor.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = start ?? new TimeOnly(9, 0),
            EndTime = end ?? new TimeOnly(10, 0),
            AcademicYearId = year.Id,
            Status = status
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShiftTemplate_archived_hidden_by_filter()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(scope.ServiceProvider);

        var tag = Guid.NewGuid().ToString("N");
        await SeedTemplateAsync(db, cls, year, instructor, EntityStatus.Active);
        await SeedTemplateAsync(db, cls, year, instructor, EntityStatus.Archived);

        var filtered = await db.ShiftTemplates
            .Where(t => t.ClassId == cls.Id)
            .CountAsync();
        var unfiltered = await db.ShiftTemplates
            .IgnoreQueryFilters()
            .Where(t => t.ClassId == cls.Id)
            .CountAsync();

        Assert.Equal(1, filtered);
        Assert.Equal(2, unfiltered);
    }

    [Fact]
    public async Task ShiftInstance_duplicate_template_date_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int templateId;
        var date = new DateOnly(2025, 10, 6);

        // Scope 1: seed template + first instance
        using (var scope1 = factory.Services.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            var (_, year, cls) = await SeedSchoolYearClassAsync(db1);
            var instructor = await SeedInstructorAsync(scope1.ServiceProvider);
            var template = await SeedTemplateAsync(db1, cls, year, instructor);
            templateId = template.Id;

            db1.ShiftInstances.Add(new ShiftInstance
            {
                TemplateId = templateId,
                Date = date,
                Status = ShiftInstanceStatus.Scheduled
            });
            await db1.SaveChangesAsync();
        }

        // Scope 2: attempt duplicate (TemplateId, Date)
        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.ShiftInstances.Add(new ShiftInstance
        {
            TemplateId = templateId,
            Date = date,
            Status = ShiftInstanceStatus.Scheduled
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Attendance_duplicate_lesson_log_client_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int lessonLogId, clientId;

        // Scope 1: seed all the way to LessonLog + first Attendance
        using (var scope1 = factory.Services.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            var (_, year, cls) = await SeedSchoolYearClassAsync(db1);
            var instructor = await SeedInstructorAsync(scope1.ServiceProvider);
            var template = await SeedTemplateAsync(db1, cls, year, instructor);

            var instance = new ShiftInstance
            {
                TemplateId = template.Id,
                Date = new DateOnly(2025, 10, 7),
                Status = ShiftInstanceStatus.Scheduled
            };
            db1.ShiftInstances.Add(instance);
            await db1.SaveChangesAsync();

            var model = new Modules.Curriculum.Model
            {
                Name = $"מודל-{Guid.NewGuid():N}",
                ExpectedLessonsToComplete = 5
            };
            db1.Models.Add(model);
            await db1.SaveChangesAsync();

            var log = new LessonLog
            {
                ShiftInstanceId = instance.Id,
                ModelId = model.Id,
                Status = LessonLogStatus.InProgress,
                ExpectedLessonsSnapshot = model.ExpectedLessonsToComplete
            };
            db1.LessonLogs.Add(log);
            await db1.SaveChangesAsync();
            lessonLogId = log.Id;

            var client = new Client { Name = "תלמיד בדיקה" };
            db1.Clients.Add(client);
            await db1.SaveChangesAsync();
            clientId = client.Id;

            db1.Attendances.Add(new Attendance
            {
                LessonLogId = lessonLogId,
                ClientId = clientId,
                Status = AttendanceStatus.Present,
                IdempotencyKey = Guid.NewGuid().ToString("N")
            });
            await db1.SaveChangesAsync();
        }

        // Scope 2: duplicate (LessonLogId, ClientId)
        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.Attendances.Add(new Attendance
        {
            LessonLogId = lessonLogId,
            ClientId = clientId,
            Status = AttendanceStatus.Present,
            IdempotencyKey = Guid.NewGuid().ToString("N")    // different key — not the idempotency constraint
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Attendance_duplicate_idempotency_key_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int lessonLogId, client1Id, client2Id;
        var sharedKey = "idem-" + Guid.NewGuid().ToString("N");

        // Scope 1: seed LessonLog + two clients + first Attendance with the shared key
        using (var scope1 = factory.Services.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            var (_, year, cls) = await SeedSchoolYearClassAsync(db1);
            var instructor = await SeedInstructorAsync(scope1.ServiceProvider);
            var template = await SeedTemplateAsync(db1, cls, year, instructor);

            var instance = new ShiftInstance
            {
                TemplateId = template.Id,
                Date = new DateOnly(2025, 10, 8),
                Status = ShiftInstanceStatus.Scheduled
            };
            db1.ShiftInstances.Add(instance);
            await db1.SaveChangesAsync();

            var model = new Modules.Curriculum.Model
            {
                Name = $"מודל-{Guid.NewGuid():N}",
                ExpectedLessonsToComplete = 5
            };
            db1.Models.Add(model);
            await db1.SaveChangesAsync();

            var log = new LessonLog
            {
                ShiftInstanceId = instance.Id,
                ModelId = model.Id,
                Status = LessonLogStatus.InProgress,
                ExpectedLessonsSnapshot = model.ExpectedLessonsToComplete
            };
            db1.LessonLogs.Add(log);
            await db1.SaveChangesAsync();
            lessonLogId = log.Id;

            var client1 = new Client { Name = "תלמיד א" };
            var client2 = new Client { Name = "תלמיד ב" };
            db1.Clients.Add(client1);
            db1.Clients.Add(client2);
            await db1.SaveChangesAsync();
            client1Id = client1.Id;
            client2Id = client2.Id;

            db1.Attendances.Add(new Attendance
            {
                LessonLogId = lessonLogId,
                ClientId = client1Id,
                Status = AttendanceStatus.Present,
                IdempotencyKey = sharedKey   // first use of the key
            });
            await db1.SaveChangesAsync();
        }

        // Scope 2: different (LessonLog, Client) but SAME IdempotencyKey — must be rejected
        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.Attendances.Add(new Attendance
        {
            LessonLogId = lessonLogId,
            ClientId = client2Id,
            Status = AttendanceStatus.Present,
            IdempotencyKey = sharedKey   // collision
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task TimeOnly_round_trips_on_sqlite()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(scope.ServiceProvider);

        var start = new TimeOnly(16, 0);
        var end = new TimeOnly(17, 30);

        var template = await SeedTemplateAsync(db, cls, year, instructor, start: start, end: end);

        // Reload from the database — bypass EF change tracker
        db.ChangeTracker.Clear();
        var loaded = await db.ShiftTemplates.IgnoreQueryFilters()
            .SingleAsync(t => t.Id == template.Id);

        Assert.Equal(start, loaded.StartTime);
        Assert.Equal(end, loaded.EndTime);
    }
}
