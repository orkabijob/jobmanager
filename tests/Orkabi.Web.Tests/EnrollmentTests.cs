using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class EnrollmentTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public EnrollmentTests(SqliteFixture sqlite) => _sqlite = sqlite;

    /// <summary>
    /// Arranges a School + AcademicYear + Class + Client and returns (Client, Class).
    /// Mirrors the pattern in ArchivalFilterTests for School+Year+Class.
    /// </summary>
    private static async Task<(Client client, Class cls)> SeedClientAndClass(AppDbContext db)
    {
        var school = new School { Name = "בית ספר בדיקה", City = "תל אביב" };
        var year = new AcademicYear
        {
            Label = "תשפ\"ו",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
            IsCurrent = false   // IsCurrent partial-unique index only allows one true — avoid conflicts
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
        var client = new Client { Name = "ישראל ישראלי" };
        db.Classes.Add(cls);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        return (client, cls);
    }

    [Fact]
    public async Task Duplicate_active_enrollment_for_same_client_and_class_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (client, cls) = await SeedClientAndClass(db);

        db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = cls.Id, EnrolledAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = cls.Id, EnrolledAt = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Re_enrollment_allowed_after_drop()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (client, cls) = await SeedClientAndClass(db);

        // First enrollment → Dropped
        db.Enrollments.Add(new Enrollment
        {
            ClientId = client.Id,
            ClassId = cls.Id,
            Status = EnrollmentStatus.Dropped,
            EnrolledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Second enrollment → Active for same (client, class) — should succeed because Dropped is excluded from the partial index
        db.Enrollments.Add(new Enrollment
        {
            ClientId = client.Id,
            ClassId = cls.Id,
            Status = EnrollmentStatus.Active,
            EnrolledAt = DateTime.UtcNow
        });

        // Must NOT throw — re-enroll after drop is allowed
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Enrollments.IgnoreQueryFilters().CountAsync(e => e.ClientId == client.Id && e.ClassId == cls.Id));
    }

    [Fact]
    public async Task Client_can_hold_multiple_active_enrollments_in_different_classes()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (client, cls1) = await SeedClientAndClass(db);

        // Second class (same school+year reuse is fine — name must be unique per school+year+name, use fresh name)
        var school = await db.Schools.FirstAsync();
        var year = await db.AcademicYears.FirstAsync();
        var cls2 = new Class
        {
            Name = $"כיתה-{Guid.NewGuid():N}",
            School = school,
            AcademicYear = year,
            Status = EntityStatus.Active
        };
        db.Classes.Add(cls2);
        await db.SaveChangesAsync();

        db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = cls1.Id, EnrolledAt = DateTime.UtcNow });
        db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = cls2.Id, EnrolledAt = DateTime.UtcNow });

        // Both Active enrollments for different classes — must succeed
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Enrollments.CountAsync(e => e.ClientId == client.Id));
    }
}
