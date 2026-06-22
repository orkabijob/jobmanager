using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class AuditInterceptorTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public AuditInterceptorTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Adding_entity_stamps_created_and_updated_timestamps()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // unique name keeps this row distinct (harmless; file-per-fixture also isolates)
        var probe = new Probe { Name = $"audit-{Guid.NewGuid():N}" };
        db.Probes.Add(probe);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, probe.CreatedAt);
        Assert.Equal(probe.CreatedAt, probe.UpdatedAt);
    }
}
