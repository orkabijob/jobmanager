using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class DbConnectivityTests : IClassFixture<Orkabi.Web.Tests.Infrastructure.SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public DbConnectivityTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task App_builds_schema_and_connects_on_sqlite()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Database.CanConnectAsync());
    }
}
