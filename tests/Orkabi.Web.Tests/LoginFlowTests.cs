using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class LoginFlowTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;

    public LoginFlowTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Valid_login_sets_auth_cookie()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = new AppUser { UserName = "a@b.test", Email = "a@b.test" };
            await um.CreateAsync(u, "Passw0rd!");
            await um.AddToRoleAsync(u, AppRoles.Admin);
        }

        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Fetch anti-forgery token from the login GET, then POST
        var getResp = await client.GetAsync("/Account/Login");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "a@b.test",
            ["Password"] = "Passw0rd!",
            ["__RequestVerificationToken"] = token
        });
        var postResp = await client.PostAsync("/Account/Login", form);

        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains(postResp.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("orkabi.auth"));
    }
}
