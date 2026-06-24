using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Operations;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class OperationsEntityTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public OperationsEntityTests(SqliteFixture sqlite) => _sqlite = sqlite;

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
        AppUser instructor)
    {
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
        return template;
    }

    private static async Task<ShiftInstance> SeedShiftInstanceAsync(AppDbContext db, ShiftTemplate template)
    {
        var instance = new ShiftInstance
        {
            TemplateId = template.Id,
            Date = new DateOnly(2025, 11, 3),
            Status = ShiftInstanceStatus.Scheduled
        };
        db.ShiftInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    // ── ExtraHours tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExtraHours_round_trips_on_sqlite()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(scope.ServiceProvider);
        var template = await SeedTemplateAsync(db, cls, year, instructor);
        var instance = await SeedShiftInstanceAsync(db, template);

        var extraHours = new ExtraHours
        {
            ShiftInstanceId = instance.Id,
            InstructorId = instructor.Id,
            Hours = 1.5m,
            Reason = "Extra session for remedial students"
        };
        db.ExtraHours.Add(extraHours);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.ExtraHours.SingleAsync(e => e.Id == extraHours.Id);

        Assert.Equal(instance.Id, loaded.ShiftInstanceId);
        Assert.Equal(instructor.Id, loaded.InstructorId);
        Assert.Equal(1.5m, loaded.Hours);
        Assert.Equal("Extra session for remedial students", loaded.Reason);
        Assert.Equal(ExtraHoursStatus.Pending, loaded.Status);
        Assert.Null(loaded.ApprovedByUserId);
        Assert.Null(loaded.ApprovedAt);
    }

    [Fact]
    public async Task ExtraHours_default_status_is_Pending()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(scope.ServiceProvider);
        var template = await SeedTemplateAsync(db, cls, year, instructor);
        var instance = await SeedShiftInstanceAsync(db, template);

        var extraHours = new ExtraHours
        {
            ShiftInstanceId = instance.Id,
            InstructorId = instructor.Id,
            Hours = 1.0m,
            Reason = "Default status test"
        };

        Assert.Equal(ExtraHoursStatus.Pending, extraHours.Status);

        db.ExtraHours.Add(extraHours);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.ExtraHours.SingleAsync(e => e.Id == extraHours.Id);
        Assert.Equal(ExtraHoursStatus.Pending, loaded.Status);
    }

    [Fact]
    public async Task ExtraHours_invalid_instructor_fk_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(scope.ServiceProvider);
        var template = await SeedTemplateAsync(db, cls, year, instructor);
        var instance = await SeedShiftInstanceAsync(db, template);

        db.ExtraHours.Add(new ExtraHours
        {
            ShiftInstanceId = instance.Id,
            InstructorId = 999999, // non-existent user
            Hours = 1.0m,
            Reason = "FK violation test"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ── IncidentReport tests ─────────────────────────────────────────────────

    [Fact]
    public async Task IncidentReport_round_trips_on_sqlite()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(scope.ServiceProvider);
        var template = await SeedTemplateAsync(db, cls, year, instructor);
        var instance = await SeedShiftInstanceAsync(db, template);

        var report = new IncidentReport
        {
            ShiftInstanceId = instance.Id,
            InstructorId = instructor.Id,
            Severity = IncidentSeverity.High,
            Description = "Student fell in the hallway during break."
        };
        db.IncidentReports.Add(report);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.IncidentReports.SingleAsync(r => r.Id == report.Id);

        Assert.Equal(instance.Id, loaded.ShiftInstanceId);
        Assert.Equal(instructor.Id, loaded.InstructorId);
        Assert.Equal(IncidentSeverity.High, loaded.Severity);
        Assert.Equal("Student fell in the hallway during break.", loaded.Description);
    }

    [Fact]
    public async Task IncidentReport_invalid_shift_instance_fk_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(scope.ServiceProvider);

        db.IncidentReports.Add(new IncidentReport
        {
            ShiftInstanceId = 999999, // non-existent shift
            InstructorId = instructor.Id,
            Severity = IncidentSeverity.Low,
            Description = "FK violation test"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ── VacationRequest tests ────────────────────────────────────────────────

    [Fact]
    public async Task VacationRequest_round_trips_on_sqlite()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, year, cls) = await SeedSchoolYearClassAsync(db);
        var instructor = await SeedInstructorAsync(scope.ServiceProvider);

        var request = new VacationRequest
        {
            InstructorId = instructor.Id,
            StartDate = new DateOnly(2025, 12, 24),
            EndDate = new DateOnly(2025, 12, 31)
        };
        db.VacationRequests.Add(request);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.VacationRequests.SingleAsync(v => v.Id == request.Id);

        Assert.Equal(instructor.Id, loaded.InstructorId);
        Assert.Equal(new DateOnly(2025, 12, 24), loaded.StartDate);
        Assert.Equal(new DateOnly(2025, 12, 31), loaded.EndDate);
        Assert.Equal(VacationStatus.Pending, loaded.Status);
        Assert.Null(loaded.ApprovedByUserId);
        Assert.Null(loaded.ApprovedAt);
        Assert.Null(loaded.AdminNote);
    }

    [Fact]
    public void VacationRequest_default_status_is_Pending()
    {
        var request = new VacationRequest
        {
            InstructorId = 1,
            StartDate = new DateOnly(2025, 12, 24),
            EndDate = new DateOnly(2025, 12, 31)
        };
        Assert.Equal(VacationStatus.Pending, request.Status);
    }

    [Fact]
    public async Task VacationRequest_invalid_instructor_fk_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.VacationRequests.Add(new VacationRequest
        {
            InstructorId = 999999, // non-existent user
            StartDate = new DateOnly(2025, 12, 24),
            EndDate = new DateOnly(2025, 12, 31)
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
