using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Orkabi.Web.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Design-time uses the DIRECT (non-pooled) Migrations endpoint. `migrations add`
        // needs no live DB; `database update` would hit this — never point it at the pooler.
        var cs = Environment.GetEnvironmentVariable("ORKABI_MIGRATIONS_CONNSTRING")
                 ?? "Host=localhost;Database=orkabi_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs).Options;
        return new AppDbContext(options);
    }
}
