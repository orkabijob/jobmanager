using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Jobs;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class DailyJobServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public DailyJobServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<AppUser> SeedInstructorAsync(IServiceProvider sp)
    {
        var um = sp.GetRequiredService<UserManager<AppUser>>();
        var email = $"dj-instr-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "מדריך בדיקה" };
        var result = await um.CreateAsync(user, "Test@1234!");
        if (!result.Succeeded)
            throw new InvalidOperationException("Seed instructor failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    /// <summary>
    /// Gets-or-creates the single IsCurrent=true AcademicYear in the shared SQLite DB.
    /// Because only one IsCurrent row is allowed (partial unique index), we reuse
    /// whichever row already exists rather than inserting a second one.
    /// </summary>
    private static async Task<AcademicYear> EnsureCurrentYearAsync(AppDbContext db)
    {
        var existing = await db.AcademicYears.IgnoreQueryFilters()
            .FirstOrDefaultAsync(y => y.IsCurrent);
        if (existing is not null)
            return existing;

        var year = new AcademicYear
        {
            Label = "שנ-שוטפת",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
            IsCurrent = true
        };
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();
        return year;
    }

    private static async Task<School> EnsureSchoolAsync(AppDbContext db)
    {
        // Reuse first school or create.
        var school = await db.Schools.FirstOrDefaultAsync();
        if (school is not null) return school;
        school = new School { Name = "בי\"ס בדיקה", City = "תל אביב" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        return school;
    }

    private static async Task<(Class cls, Client client, AppUser instructor)> SeedClientWithEnrollmentAsync(
        AppDbContext db,
        IServiceProvider sp,
        DateOnly birthday,
        bool isActive = true,
        EnrollmentStatus enrollmentStatus = EnrollmentStatus.Active)
    {
        var school = await EnsureSchoolAsync(db);
        var year = await EnsureCurrentYearAsync(db);

        var cls = new Class
        {
            Name = $"כיתה-{Guid.NewGuid():N}",
            School = school,
            AcademicYear = year,
            Status = EntityStatus.Active
        };
        db.Classes.Add(cls);

        var client = new Client
        {
            Name = $"לקוח-{Guid.NewGuid():N}",
            Birthday = birthday,
            IsActive = isActive
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var instructor = await SeedInstructorAsync(sp);

        var enrollment = new Enrollment
        {
            ClientId = client.Id,
            ClassId = cls.Id,
            Status = enrollmentStatus,
            EnrolledAt = DateTime.UtcNow
        };
        db.Enrollments.Add(enrollment);

        // Only add a template for active/tryout enrollments (the active template is what
        // the instructor-resolution query looks for).
        if (enrollmentStatus is EnrollmentStatus.Active or EnrollmentStatus.Tryout)
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
        }

        await db.SaveChangesAsync();
        return (cls, client, instructor);
    }

    private static async Task<Client> SeedClientNoEnrollmentAsync(
        AppDbContext db,
        DateOnly birthday,
        bool isActive = true)
    {
        var client = new Client
        {
            Name = $"לקוח-{Guid.NewGuid():N}",
            Birthday = birthday,
            IsActive = isActive
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    // ── Birthday day-of: instructor + admin tickets ──────────────────────────

    [Fact]
    public async Task RunBirthdayCheck_day_of_creates_instructor_and_admin_items()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var runner = sp.GetRequiredService<IDailyJobRunner>();

        var today = new DateOnly(2025, 6, 15);
        var birthday = new DateOnly(1998, 6, 15); // month+day == today

        var (_, client, instructor) = await SeedClientWithEnrollmentAsync(db, sp, birthday);

        await runner.RunBirthdayCheckAsync(today);

        var birthdayOccurrence = today;
        var instructorKey = $"birthday_dayof_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_user_{instructor.Id}";
        var adminKey = $"birthday_dayof_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_admin";

        var instructorItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == instructorKey);
        var adminItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == adminKey);

        Assert.Equal(ActionItemType.Birthday, instructorItem.Type);
        Assert.Equal(ActionItemStatus.Open, instructorItem.Status);
        Assert.Equal(instructor.Id, instructorItem.AssignedToUserId);
        Assert.Null(instructorItem.AssignedToRole);

        Assert.Equal(ActionItemType.Birthday, adminItem.Type);
        Assert.Equal(ActionItemStatus.Open, adminItem.Status);
        Assert.Equal(AppRoles.Admin, adminItem.AssignedToRole);
        Assert.Null(adminItem.AssignedToUserId);
    }

    // ── Birthday 24h-before: instructor + admin tickets ──────────────────────

    [Fact]
    public async Task RunBirthdayCheck_24h_before_creates_instructor_and_admin_items()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var runner = sp.GetRequiredService<IDailyJobRunner>();

        var today = new DateOnly(2025, 7, 14);
        var birthday = new DateOnly(1998, 7, 15); // month+day == tomorrow

        var (_, client, instructor) = await SeedClientWithEnrollmentAsync(db, sp, birthday);

        await runner.RunBirthdayCheckAsync(today);

        var birthdayOccurrence = today.AddDays(1); // 24h: tomorrow
        var instructorKey = $"birthday_24h_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_user_{instructor.Id}";
        var adminKey = $"birthday_24h_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_admin";

        var instructorItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == instructorKey);
        var adminItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == adminKey);

        Assert.Equal(ActionItemType.Birthday, instructorItem.Type);
        Assert.Equal(instructor.Id, instructorItem.AssignedToUserId);

        Assert.Equal(ActionItemType.Birthday, adminItem.Type);
        Assert.Equal(AppRoles.Admin, adminItem.AssignedToRole);
    }

    // ── No matching birthday → no items ──────────────────────────────────────

    [Fact]
    public async Task RunBirthdayCheck_no_matching_birthday_creates_no_items()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var runner = sp.GetRequiredService<IDailyJobRunner>();

        var today = new DateOnly(2025, 8, 1);
        var birthday = new DateOnly(1998, 12, 25); // does NOT match today or tomorrow

        var client = await SeedClientNoEnrollmentAsync(db, birthday);

        var countBefore = await db.ActionItems.CountAsync(a => a.RelatedEntityId == client.Id);
        await runner.RunBirthdayCheckAsync(today);
        var countAfter = await db.ActionItems.CountAsync(a => a.RelatedEntityId == client.Id);

        Assert.Equal(countBefore, countAfter);
    }

    // ── No active enrollment → admin-only ticket ─────────────────────────────

    [Fact]
    public async Task RunBirthdayCheck_no_active_enrollment_creates_admin_only_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var runner = sp.GetRequiredService<IDailyJobRunner>();

        var today = new DateOnly(2025, 9, 5);
        var birthday = new DateOnly(1998, 9, 5); // day-of

        // Client with no enrollment at all.
        var client = await SeedClientNoEnrollmentAsync(db, birthday);

        await runner.RunBirthdayCheckAsync(today);

        var birthdayOccurrence = today;
        var adminKey = $"birthday_dayof_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_admin";
        var adminItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == adminKey);

        Assert.Equal(AppRoles.Admin, adminItem.AssignedToRole);
        Assert.Null(adminItem.AssignedToUserId);

        // No instructor items.
        var instructorCount = await db.ActionItems
            .CountAsync(a => a.DeduplicationKey != null
                          && a.DeduplicationKey.StartsWith($"birthday_dayof_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_user_"));
        Assert.Equal(0, instructorCount);
    }

    // ── Idempotent: second call creates no duplicates ─────────────────────────

    [Fact]
    public async Task RunBirthdayCheck_idempotent_second_call_no_duplicates()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var runner = sp.GetRequiredService<IDailyJobRunner>();

        var today = new DateOnly(2025, 10, 10);
        var birthday = new DateOnly(1998, 10, 10); // day-of

        var (_, client, instructor) = await SeedClientWithEnrollmentAsync(db, sp, birthday);

        await runner.RunBirthdayCheckAsync(today);
        await runner.RunBirthdayCheckAsync(today);

        var birthdayOccurrence = today;
        var instructorKey = $"birthday_dayof_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_user_{instructor.Id}";
        var adminKey = $"birthday_dayof_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_admin";

        Assert.Equal(1, await db.ActionItems.CountAsync(a => a.DeduplicationKey == instructorKey));
        Assert.Equal(1, await db.ActionItems.CountAsync(a => a.DeduplicationKey == adminKey));
    }

    // ── Inactive client is skipped ────────────────────────────────────────────

    [Fact]
    public async Task RunBirthdayCheck_inactive_client_is_skipped()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var runner = sp.GetRequiredService<IDailyJobRunner>();

        var today = new DateOnly(2025, 11, 11);
        var birthday = new DateOnly(1998, 11, 11); // matches today

        var client = await SeedClientNoEnrollmentAsync(db, birthday, isActive: false);

        var countBefore = await db.ActionItems.CountAsync(a => a.RelatedEntityId == client.Id);
        await runner.RunBirthdayCheckAsync(today);
        var countAfter = await db.ActionItems.CountAsync(a => a.RelatedEntityId == client.Id);

        Assert.Equal(countBefore, countAfter);
    }

    // ── Dropped enrollment → admin-only ticket (instructor excluded) ──────────

    [Fact]
    public async Task RunBirthdayCheck_dropped_enrollment_results_in_admin_only_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var runner = sp.GetRequiredService<IDailyJobRunner>();

        var today = new DateOnly(2025, 12, 1);
        var birthday = new DateOnly(1998, 12, 1); // day-of

        // Seed with Dropped enrollment (template is NOT created for Dropped).
        var (_, client, _) = await SeedClientWithEnrollmentAsync(db, sp, birthday, enrollmentStatus: EnrollmentStatus.Dropped);

        await runner.RunBirthdayCheckAsync(today);

        var birthdayOccurrence = today;
        var adminKey = $"birthday_dayof_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_admin";
        var adminItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == adminKey);

        Assert.Equal(AppRoles.Admin, adminItem.AssignedToRole);

        // No instructor item.
        var instructorCount = await db.ActionItems
            .CountAsync(a => a.DeduplicationKey != null
                          && a.DeduplicationKey.StartsWith($"birthday_dayof_{client.Id}_{birthdayOccurrence:yyyy-MM-dd}_user_"));
        Assert.Equal(0, instructorCount);
    }

    // ── RunShiftGenerationAsync delegates to IShiftInstanceGenerator ──────────

    [Fact]
    public async Task RunShiftGeneration_generates_instances_for_active_template()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var runner = sp.GetRequiredService<IDailyJobRunner>();

        // Build an AY that covers real-today and beyond (generator reads clock internally).
        var realToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));

        var school = await EnsureSchoolAsync(db);
        var year = new AcademicYear
        {
            Label = $"שג-{Guid.NewGuid():N}"[..10],
            StartDate = realToday,
            EndDate = realToday.AddDays(30),
            IsCurrent = false // independent AY for generator test — doesn't need to be current
        };
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Class
        {
            Name = $"כ-{Guid.NewGuid():N}",
            School = school,
            AcademicYear = year,
            Status = EntityStatus.Active
        };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var instructor = await SeedInstructorAsync(sp);

        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DefaultInstructorId = instructor.Id,
            DayOfWeek = realToday.DayOfWeek, // matches today → at least 1 instance
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        await runner.RunShiftGenerationAsync(realToday);

        var instances = await db.ShiftInstances
            .Where(i => i.TemplateId == template.Id)
            .ToListAsync();

        Assert.NotEmpty(instances);
        Assert.All(instances, i => Assert.Equal(template.DayOfWeek, i.Date.DayOfWeek));
    }
}
