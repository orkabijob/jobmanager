using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Logistics;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class LogisticsEntityTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public LogisticsEntityTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Shared arrange helpers ───────────────────────────────────────────────

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

    private static async Task<Model> SeedModelAsync(AppDbContext db)
    {
        var model = new Model
        {
            Name = $"מודל-{Guid.NewGuid():N}",
            ExpectedLessonsToComplete = 10
        };
        db.Models.Add(model);
        await db.SaveChangesAsync();
        return model;
    }

    // ── LogisticsOrder tests ─────────────────────────────────────────────────

    [Fact]
    public async Task LogisticsOrder_round_trips_on_sqlite()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);

        var order = new LogisticsOrder
        {
            ClassId = cls.Id,
            ModelId = model.Id,
            Quantity = 3,
            Status = LogisticsOrderStatus.Packed,
            DisputeNotes = null,
            DeliveredAt = null
        };
        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.LogisticsOrders.SingleAsync(o => o.Id == order.Id);

        Assert.Equal(cls.Id, loaded.ClassId);
        Assert.Equal(model.Id, loaded.ModelId);
        Assert.Equal(3, loaded.Quantity);
        Assert.Equal(LogisticsOrderStatus.Packed, loaded.Status);
        Assert.Null(loaded.DisputeNotes);
        Assert.Null(loaded.DeliveredAt);
    }

    [Fact]
    public async Task LogisticsOrder_default_status_is_Pending()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);

        var order = new LogisticsOrder
        {
            ClassId = cls.Id,
            ModelId = model.Id,
            Quantity = 1
        };

        // Check default BEFORE save
        Assert.Equal(LogisticsOrderStatus.Pending, order.Status);

        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.LogisticsOrders.SingleAsync(o => o.Id == order.Id);

        // Check default AFTER round-trip
        Assert.Equal(LogisticsOrderStatus.Pending, loaded.Status);
    }

    [Fact]
    public async Task LogisticsOrder_invalid_classId_fk_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var model = await SeedModelAsync(db);

        db.LogisticsOrders.Add(new LogisticsOrder
        {
            ClassId = 999999, // non-existent class
            ModelId = model.Id,
            Quantity = 1
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LogisticsOrder_invalid_modelId_fk_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);

        db.LogisticsOrders.Add(new LogisticsOrder
        {
            ClassId = cls.Id,
            ModelId = 999999, // non-existent model
            Quantity = 1
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
