using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class AdminSeedTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;

    public AdminSeedTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task SeedAdminAsync_creates_admin_user_and_adds_to_admin_role()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }
            .WithConfig("SEED_ADMIN_EMAIL", "romi@orkabi.test")
            .WithConfig("SEED_ADMIN_PASSWORD", "Passw0rd!")
            .Prepared();

        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var cfg = sp.GetRequiredService<IConfiguration>();

        await DataSeeder.SeedAdminAsync(sp, cfg);

        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var user = await userMgr.FindByEmailAsync("romi@orkabi.test");

        Assert.NotNull(user);
        Assert.True(await userMgr.IsInRoleAsync(user, AppRoles.Admin));
    }

    [Fact]
    public async Task SeedAdminAsync_is_idempotent_and_does_not_create_duplicate()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }
            .WithConfig("SEED_ADMIN_EMAIL", "romi2@orkabi.test")
            .WithConfig("SEED_ADMIN_PASSWORD", "Passw0rd!")
            .Prepared();

        // Call twice
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var cfg = sp.GetRequiredService<IConfiguration>();
            await DataSeeder.SeedAdminAsync(sp, cfg);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var cfg = sp.GetRequiredService<IConfiguration>();
            await DataSeeder.SeedAdminAsync(sp, cfg);
        }

        using var verifyScope = factory.Services.CreateScope();
        var userMgr = verifyScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var users = userMgr.Users.Where(u => u.Email == "romi2@orkabi.test").ToList();
        Assert.Single(users);
    }

    [Fact]
    public async Task SeedAdminAsync_skips_when_email_is_missing()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }
            .WithConfig("SEED_ADMIN_EMAIL", "")
            .WithConfig("SEED_ADMIN_PASSWORD", "Passw0rd!")
            .Prepared();

        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var cfg = sp.GetRequiredService<IConfiguration>();

        // Should return without throwing or creating any user
        await DataSeeder.SeedAdminAsync(sp, cfg);

        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var user = await userMgr.FindByEmailAsync("");
        Assert.Null(user);
    }
}
