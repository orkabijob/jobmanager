using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class ShiftInstanceGeneratorTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ShiftInstanceGeneratorTests(SqliteFixture sqlite) => _sqlite = sqlite;

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

    // Creates a template with the given DayOfWeek in an AY that covers today and beyond.
    private static async Task<ShiftTemplate> SeedTemplateAsync(
        AppDbContext db,
        Class cls,
        AcademicYear year,
        AppUser instructor,
        DayOfWeek dow = DayOfWeek.Monday,
        EntityStatus status = EntityStatus.Active)
    {
        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DefaultInstructorId = instructor.Id,
            DayOfWeek = dow,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            AcademicYearId = year.Id,
            Status = status
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    [Fact]
    public async Task Generator_creates_instances_for_matching_day_of_week_in_window()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var generator = sp.GetRequiredService<IShiftInstanceGenerator>();

        // Build an AY that starts today and ends in 30 days.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Shared.IsraelClock.IsraelTz));
        var school = new School { Name = $"ס-{Guid.NewGuid():N}", City = "ת\"א" };
        db.Schools.Add(school);
        var year = new AcademicYear
        {
            Label = $"חג-{Guid.NewGuid():N}"[..10],
            StartDate = today,
            EndDate = today.AddDays(30),
            IsCurrent = false
        };
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Class { Name = $"כ-{Guid.NewGuid():N}", School = school, AcademicYear = year, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var instructor = await SeedInstructorAsync(sp);
        // Use today's DayOfWeek so at least one instance is created (today itself).
        var template = await SeedTemplateAsync(db, cls, year, instructor, dow: today.DayOfWeek);

        await generator.GenerateForTemplateAsync(template.Id);

        var instances = await db.ShiftInstances
            .Where(i => i.TemplateId == template.Id)
            .ToListAsync();

        // Every instance date must match the template's DayOfWeek.
        Assert.NotEmpty(instances);
        Assert.All(instances, i => Assert.Equal(template.DayOfWeek, i.Date.DayOfWeek));
        // All must fall within [today, today+30].
        Assert.All(instances, i =>
        {
            Assert.True(i.Date >= today);
            Assert.True(i.Date <= today.AddDays(30));
        });
    }

    [Fact]
    public async Task Generator_is_idempotent_on_second_call()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var generator = sp.GetRequiredService<IShiftInstanceGenerator>();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Shared.IsraelClock.IsraelTz));
        var school = new School { Name = $"ס-{Guid.NewGuid():N}", City = "ת\"א" };
        db.Schools.Add(school);
        var year = new AcademicYear
        {
            Label = $"חג-{Guid.NewGuid():N}"[..10],
            StartDate = today,
            EndDate = today.AddDays(30),
            IsCurrent = false
        };
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Class { Name = $"כ-{Guid.NewGuid():N}", School = school, AcademicYear = year, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateAsync(db, cls, year, instructor, dow: today.DayOfWeek);

        await generator.GenerateForTemplateAsync(template.Id);
        var countFirst = await db.ShiftInstances.CountAsync(i => i.TemplateId == template.Id);

        await generator.GenerateForTemplateAsync(template.Id);
        var countSecond = await db.ShiftInstances.CountAsync(i => i.TemplateId == template.Id);

        Assert.Equal(countFirst, countSecond);
    }

    [Fact]
    public async Task Generator_skips_existing_detached_instances()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var generator = sp.GetRequiredService<IShiftInstanceGenerator>();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Shared.IsraelClock.IsraelTz));
        var school = new School { Name = $"ס-{Guid.NewGuid():N}", City = "ת\"א" };
        db.Schools.Add(school);
        var year = new AcademicYear
        {
            Label = $"חג-{Guid.NewGuid():N}"[..10],
            StartDate = today,
            EndDate = today.AddDays(30),
            IsCurrent = false
        };
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Class { Name = $"כ-{Guid.NewGuid():N}", School = school, AcademicYear = year, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var instructor = await SeedInstructorAsync(sp);
        // Use today's DayOfWeek so today is included.
        var template = await SeedTemplateAsync(db, cls, year, instructor, dow: today.DayOfWeek);

        // Pre-seed a Detached instance for today.
        db.ShiftInstances.Add(new ShiftInstance
        {
            TemplateId = template.Id,
            Date = today,
            Status = ShiftInstanceStatus.Detached,
            ActualInstructorId = instructor.Id
        });
        await db.SaveChangesAsync();

        await generator.GenerateForTemplateAsync(template.Id);

        // The Detached instance for today must still be Detached — generator must not touch it.
        var todayInstance = await db.ShiftInstances
            .SingleAsync(i => i.TemplateId == template.Id && i.Date == today);
        Assert.Equal(ShiftInstanceStatus.Detached, todayInstance.Status);

        // No duplicate for today.
        var count = await db.ShiftInstances.CountAsync(i => i.TemplateId == template.Id && i.Date == today);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Generator_clamps_to_academic_year_end()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var generator = sp.GetRequiredService<IShiftInstanceGenerator>();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Shared.IsraelClock.IsraelTz));
        // AY ends in 5 days — well within the default 30-day horizon.
        var ayEnd = today.AddDays(5);

        var school = new School { Name = $"ס-{Guid.NewGuid():N}", City = "ת\"א" };
        db.Schools.Add(school);
        var year = new AcademicYear
        {
            Label = $"חג-{Guid.NewGuid():N}"[..10],
            StartDate = today,
            EndDate = ayEnd,
            IsCurrent = false
        };
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Class { Name = $"כ-{Guid.NewGuid():N}", School = school, AcademicYear = year, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var instructor = await SeedInstructorAsync(sp);
        var template = await SeedTemplateAsync(db, cls, year, instructor, dow: today.DayOfWeek);

        await generator.GenerateForTemplateAsync(template.Id, horizonDays: 30);

        var instances = await db.ShiftInstances
            .Where(i => i.TemplateId == template.Id)
            .ToListAsync();

        // All instances must be on or before the AY end date.
        Assert.All(instances, i => Assert.True(i.Date <= ayEnd));
    }
}
