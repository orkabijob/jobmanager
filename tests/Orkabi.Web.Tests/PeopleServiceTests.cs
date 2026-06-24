using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class PeopleServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public PeopleServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(School school, AcademicYear year)> SeedSchoolAndYear(AppDbContext db, bool isCurrent = false)
    {
        var school = new School { Name = $"בית ספר {Guid.NewGuid():N}", City = "תל אביב" };
        var year = new AcademicYear
        {
            Label = $"תשפ\"-{Guid.NewGuid().ToString("N")[..4]}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
            IsCurrent = isCurrent
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();
        return (school, year);
    }

    private static async Task<Class> SeedClass(AppDbContext db, School school, AcademicYear year, EntityStatus status = EntityStatus.Active)
    {
        var cls = new Class
        {
            Name = $"כיתה-{Guid.NewGuid():N}",
            School = school,
            AcademicYear = year,
            Status = status
        };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        return cls;
    }

    private static async Task<Client> SeedClient(AppDbContext db, bool isActive = true)
    {
        var client = new Client { Name = $"לקוח-{Guid.NewGuid():N}", IsActive = isActive };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    // ─── AcademicYearService ───────────────────────────────────────────────

    [Fact]
    public async Task SetCurrentAsync_makes_target_the_only_IsCurrent()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<AcademicYearService>();

        var year1 = new AcademicYear { Label = "תשפ\"ה", StartDate = new DateOnly(2024, 9, 1), EndDate = new DateOnly(2025, 6, 30), IsCurrent = true };
        var year2 = new AcademicYear { Label = "תשפ\"ו", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), IsCurrent = false };
        db.AcademicYears.AddRange(year1, year2);
        await db.SaveChangesAsync();

        await svc.SetCurrentAsync(year2.Id);

        // Use a fresh scope to avoid EF change-tracker cache returning stale values.
        using var readScope = factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var years = await readDb.AcademicYears.ToListAsync();
        Assert.False(years.First(y => y.Id == year1.Id).IsCurrent);
        Assert.True(years.First(y => y.Id == year2.Id).IsCurrent);
        Assert.Equal(1, years.Count(y => y.IsCurrent));
    }

    // ─── ClassService ──────────────────────────────────────────────────────

    [Fact]
    public async Task ClassService_ListAsync_default_excludes_archived()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClassService>();

        var (school, year) = await SeedSchoolAndYear(db);
        var active = await SeedClass(db, school, year, EntityStatus.Active);
        var archived = await SeedClass(db, school, year, EntityStatus.Archived);

        var results = await svc.ListAsync(school.Id, year.Id, null);

        Assert.Contains(results, c => c.Id == active.Id);
        Assert.DoesNotContain(results, c => c.Id == archived.Id);
    }

    [Fact]
    public async Task ClassService_ListAsync_Archived_status_returns_archived_classes()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClassService>();

        var (school, year) = await SeedSchoolAndYear(db);
        var archived = await SeedClass(db, school, year, EntityStatus.Archived);

        var results = await svc.ListAsync(school.Id, year.Id, EntityStatus.Archived);

        Assert.Contains(results, c => c.Id == archived.Id);
    }

    // ─── ClientService ─────────────────────────────────────────────────────

    [Fact]
    public async Task ClientService_ListAsync_activeOnly_true_excludes_inactive()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClientService>();

        var active = await SeedClient(db, isActive: true);
        var inactive = await SeedClient(db, isActive: false);

        var results = await svc.ListAsync(null, activeOnly: true);

        Assert.Contains(results, c => c.Id == active.Id);
        Assert.DoesNotContain(results, c => c.Id == inactive.Id);
    }

    [Fact]
    public async Task ClientService_ListAsync_activeOnly_false_includes_inactive()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClientService>();

        var inactive = await SeedClient(db, isActive: false);

        var results = await svc.ListAsync(null, activeOnly: false);

        Assert.Contains(results, c => c.Id == inactive.Id);
    }

    // ─── EnrollmentService ─────────────────────────────────────────────────

    [Fact]
    public async Task EnrollAsync_duplicate_throws_InvalidOperationException_with_Hebrew_message()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<EnrollmentService>();

        var (school, year) = await SeedSchoolAndYear(db);
        var cls = await SeedClass(db, school, year);
        var client = await SeedClient(db);

        await svc.EnrollAsync(cls.Id, client.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EnrollAsync(cls.Id, client.Id));
        Assert.Equal("התלמיד כבר רשום לכיתה זו", ex.Message);
    }

    [Fact]
    public async Task ToggleAsync_tryout_flips_IsTryout_and_Status()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<EnrollmentService>();

        var (school, year) = await SeedSchoolAndYear(db);
        var cls = await SeedClass(db, school, year);
        var client = await SeedClient(db);

        var enrollment = await svc.EnrollAsync(cls.Id, client.Id);
        Assert.Equal(EnrollmentStatus.Active, enrollment.Status);
        Assert.False(enrollment.IsTryout);

        // Toggle to Tryout
        await svc.ToggleAsync(enrollment.Id, "tryout");
        var updated = await db.Enrollments.FindAsync(enrollment.Id);
        Assert.Equal(EnrollmentStatus.Tryout, updated!.Status);
        Assert.True(updated.IsTryout);

        // Toggle back to Active
        await svc.ToggleAsync(enrollment.Id, "tryout");
        await db.Entry(updated).ReloadAsync();
        Assert.Equal(EnrollmentStatus.Active, updated.Status);
        Assert.False(updated.IsTryout);
    }

    [Fact]
    public async Task ListAvailableForClassAsync_excludes_enrolled_and_inactive_clients()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<EnrollmentService>();

        var (school, year) = await SeedSchoolAndYear(db);
        var cls = await SeedClass(db, school, year);

        var available = await SeedClient(db, isActive: true);
        var enrolled = await SeedClient(db, isActive: true);
        var inactive = await SeedClient(db, isActive: false);

        await svc.EnrollAsync(cls.Id, enrolled.Id);

        var results = await svc.ListAvailableForClassAsync(cls.Id, null);

        Assert.Contains(results, c => c.Id == available.Id);
        Assert.DoesNotContain(results, c => c.Id == enrolled.Id);
        Assert.DoesNotContain(results, c => c.Id == inactive.Id);
    }
}
