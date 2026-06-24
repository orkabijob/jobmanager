using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class AcademicYearSeedTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;

    public AcademicYearSeedTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Seed_creates_exactly_one_current_year_and_is_idempotent()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await DataSeeder.SeedAcademicYearAsync(sp);
        await DataSeeder.SeedAcademicYearAsync(sp);   // second call must be a no-op
        var db = sp.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AcademicYears.CountAsync());
        Assert.True((await db.AcademicYears.SingleAsync()).IsCurrent);
    }
}
