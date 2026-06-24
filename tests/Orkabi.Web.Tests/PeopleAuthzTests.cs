using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class PeopleAuthzTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public PeopleAuthzTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Anonymous_is_redirected_to_login_from_people_hub()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var resp = await client.GetAsync("/People");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Instructor_is_forbidden_from_people_hub()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = new AppUser { UserName = "instructor@people.test", Email = "instructor@people.test" };
            await um.CreateAsync(u, "Passw0rd!");
            await um.AddToRoleAsync(u, AppRoles.Instructor);
        }
        var client = await TestLogin.SignInAsync(factory, "instructor@people.test", "Passw0rd!");

        var resp = await client.GetAsync("/People");

        // An authenticated but unauthorized user gets redirected to AccessDenied (which is a 302).
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Cs_user_can_open_schools_index()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = new AppUser { UserName = "cs@people.test", Email = "cs@people.test" };
            await um.CreateAsync(u, "Passw0rd!");
            await um.AddToRoleAsync(u, AppRoles.CustomerService);
        }
        var client = await TestLogin.SignInAsync(factory, "cs@people.test", "Passw0rd!");

        var resp = await client.GetAsync("/People/Schools");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
