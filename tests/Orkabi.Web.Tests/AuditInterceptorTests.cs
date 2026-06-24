using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.People;
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
        var school = new School { Name = $"audit-{Guid.NewGuid():N}", City = "תל אביב" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, school.CreatedAt);
        Assert.Equal(school.CreatedAt, school.UpdatedAt);
    }
}
