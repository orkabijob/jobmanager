using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class RoleRoutingTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public RoleRoutingTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Instructor_is_routed_to_their_dashboard_and_denied_admin()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = new AppUser { UserName = "t@b.test", Email = "t@b.test" };
            await um.CreateAsync(u, "Passw0rd!");
            await um.AddToRoleAsync(u, AppRoles.Instructor);
        }
        var client = await TestLogin.SignInAsync(factory, "t@b.test", "Passw0rd!");

        var root = await client.GetAsync("/");
        Assert.Equal("/Dashboard/Instructor", root.Headers.Location?.ToString());

        var admin = await client.GetAsync("/Dashboard/Admin");
        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode); // → AccessDenied
        Assert.Contains("AccessDenied", admin.Headers.Location?.ToString());
    }
}
