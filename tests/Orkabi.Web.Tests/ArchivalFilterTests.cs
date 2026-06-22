using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class ArchivalFilterTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ArchivalFilterTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Archived_rows_are_hidden_by_default_and_visible_with_IgnoreQueryFilters()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tag = $"arch-{Guid.NewGuid():N}";   // tag-scoped relative counts (kept; file-per-fixture also isolates)
        db.Probes.Add(new Probe { Name = tag, Status = EntityStatus.Active });
        db.Probes.Add(new Probe { Name = tag, Status = EntityStatus.Archived });
        await db.SaveChangesAsync();

        // relative counts scoped to this test's tag — never table totals
        Assert.Equal(1, await db.Probes.Where(p => p.Name == tag).CountAsync());                      // filtered
        Assert.Equal(2, await db.Probes.IgnoreQueryFilters().Where(p => p.Name == tag).CountAsync()); // escape hatch
    }
}
