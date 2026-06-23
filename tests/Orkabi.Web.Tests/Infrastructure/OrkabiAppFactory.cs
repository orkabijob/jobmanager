using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Tests.Infrastructure;

public class OrkabiAppFactory : WebApplicationFactory<Program>
{
    public string ConnectionString { get; set; } = "";
    private readonly Dictionary<string, string?> _config = new();

    public OrkabiAppFactory WithConfig(string key, string? value)
    { _config[key] = value; return this; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Testing env => Program skips its Npgsql boot-migrate; the factory owns schema (A1 fix).
        builder.UseEnvironment("Testing");
        // Force the SQLite provider + the test DB connection string for the whole app.
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        // Apply each config entry via UseSetting so it is visible to the eager
        // builder.Configuration reads in Program.cs (e.g. Authentication:Google:ClientId).
        // This matches how Database:Provider and ConnectionStrings:Default are injected above.
        foreach (var kv in _config)
            builder.UseSetting(kv.Key, kv.Value);
        // No DbContext re-registration needed: Program's provider switch already reads
        // Database:Provider + ConnectionStrings:Default (set above), and the audit interceptor
        // (Task 4) is wired in that same single registration.
    }

    /// <summary>
    /// Build the schema for the test's SQLite DB from the EF model via EnsureCreated()
    /// (no migration files — those are Npgsql-only and run at the Neon deploy gate, Task 11).
    /// Idempotent; call once at the start of a DB test. Also seeds the 4 roles (boot-seeding
    /// is skipped under Testing, so the factory must seed them for role-dependent tests).
    /// </summary>
    public OrkabiAppFactory Prepared()
    {
        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        DataSeeder.SeedRolesAsync(sp).GetAwaiter().GetResult();
        return this;
    }
}
