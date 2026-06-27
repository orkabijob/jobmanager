using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class AcademicYearServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public AcademicYearServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static string Label() => $"y-{Guid.NewGuid():N}"[..18];

    [Fact]
    public async Task CreateAsync_adds_a_non_current_year()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<AcademicYearService>();

        var label = Label();
        var created = await svc.CreateAsync(label, new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30));

        Assert.True(created.Id > 0);
        Assert.False(created.IsCurrent);   // creating a year never makes it current

        db.ChangeTracker.Clear();
        var reloaded = await db.AcademicYears.FindAsync(created.Id);
        Assert.Equal(label, reloaded!.Label);
        Assert.Equal(new DateOnly(2026, 9, 1), reloaded.StartDate);
        Assert.Equal(new DateOnly(2027, 6, 30), reloaded.EndDate);
        Assert.False(reloaded.IsCurrent);
    }

    [Fact]
    public async Task SetCurrentAsync_is_exclusive_across_transitions()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<AcademicYearService>();

        // Insert both as non-current (avoids any pre-existing-current partial-index clash on the shared db).
        var y1 = new AcademicYear { Label = Label(), StartDate = new DateOnly(2024, 9, 1), EndDate = new DateOnly(2025, 6, 30), IsCurrent = false };
        var y2 = new AcademicYear { Label = Label(), StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), IsCurrent = false };
        db.AcademicYears.AddRange(y1, y2);
        await db.SaveChangesAsync();

        await svc.SetCurrentAsync(y1.Id);
        db.ChangeTracker.Clear();
        Assert.True((await db.AcademicYears.FindAsync(y1.Id))!.IsCurrent);

        await svc.SetCurrentAsync(y2.Id);
        db.ChangeTracker.Clear();
        var all = await db.AcademicYears.ToListAsync();
        Assert.Single(all.Where(y => y.IsCurrent));                          // exactly one current, table-wide
        Assert.True((await db.AcademicYears.FindAsync(y2.Id))!.IsCurrent);
        Assert.False((await db.AcademicYears.FindAsync(y1.Id))!.IsCurrent);  // previous current was cleared
    }

    [Fact]
    public async Task SetCurrentAsync_unknown_id_throws()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AcademicYearService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SetCurrentAsync(999999));
    }
}
