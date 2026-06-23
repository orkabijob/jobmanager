using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class AuthTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public AuthTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Four_roles_are_seeded_and_user_can_be_created_with_a_role()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();   // builds schema AND seeds the 4 roles
        using var scope = factory.Services.CreateScope();
        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        foreach (var r in AppRoles.All)
            Assert.True(await roleMgr.RoleExistsAsync(r));

        var user = new AppUser { UserName = "romi@orkabi.test", Email = "romi@orkabi.test" };
        var created = await userMgr.CreateAsync(user, "Passw0rd!");
        Assert.True(created.Succeeded);
        Assert.True((await userMgr.AddToRoleAsync(user, AppRoles.Admin)).Succeeded);
        Assert.True(await userMgr.IsInRoleAsync(user, AppRoles.Admin));
    }
}
