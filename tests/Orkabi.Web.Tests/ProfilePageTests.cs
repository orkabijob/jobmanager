using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

// F10 (non-email half) — /Account/Profile: edit display name + change password.
public class ProfilePageTests : IClassFixture<SqliteFixture>
{
    private const string Url = "/Account/Profile";
    private readonly SqliteFixture _sqlite;
    public ProfilePageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client, string email)>
        SignedInAsync(SqliteFixture sqlite, string suffix, string password = "Passw0rd!", string? fullName = null)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        var email = $"profile.{suffix}@test.test";
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            if (await um.FindByEmailAsync(email) is null)
            {
                var u = new AppUser { UserName = email, Email = email, FullName = fullName };
                await um.CreateAsync(u, password);
                await um.AddToRoleAsync(u, AppRoles.Admin);
            }
        }
        var client = await TestLogin.SignInAsync(factory, email, password);
        return (factory, client, email);
    }

    private static async Task<string> TokenAsync(HttpClient client)
        => AntiForgery.Extract(await (await client.GetAsync(Url)).Content.ReadAsStringAsync());

    [Fact]
    public async Task Anonymous_redirected_to_login()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Roleless_user_can_open_profile()
    {
        // A user with no role assigned still owns their profile (page is [Authorize] with no role).
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var email = $"profile.norole.{Guid.NewGuid():N}@test.test";
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = new AppUser { UserName = email, Email = email };
            await um.CreateAsync(u, "Passw0rd!");   // no AddToRoleAsync
        }
        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    [Fact]
    public async Task Authenticated_user_can_open_profile()
    {
        var (factory, client, email) = await SignedInAsync(_sqlite, "open");
        var resp = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains(email, body);
        factory.Dispose();
    }

    [Fact]
    public async Task User_can_update_full_name()
    {
        var (factory, client, email) = await SignedInAsync(_sqlite, "name");
        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "רון ישראלי",
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync($"{Url}?handler=SaveProfile", form);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        using var s = factory.Services.CreateScope();
        var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await um.FindByEmailAsync(email);
        Assert.Equal("רון ישראלי", user!.FullName);
        factory.Dispose();
    }

    [Fact]
    public async Task User_can_change_password_then_sign_in_with_new()
    {
        var (factory, client, email) = await SignedInAsync(_sqlite, "pwd");
        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Password.Current"] = "Passw0rd!",
            ["Password.New"] = "NewPassw1!",
            ["Password.Confirm"] = "NewPassw1!",
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync($"{Url}?handler=ChangePassword", form);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        // Proof: a fresh sign-in with the NEW password succeeds (SignInAsync throws otherwise).
        var fresh = await TestLogin.SignInAsync(factory, email, "NewPassw1!");
        Assert.NotNull(fresh);
        factory.Dispose();
    }

    [Fact]
    public async Task Change_password_with_wrong_current_fails_and_keeps_old()
    {
        var (factory, client, email) = await SignedInAsync(_sqlite, "wrongpwd");
        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Password.Current"] = "WRONG-current",
            ["Password.New"] = "NewPassw1!",
            ["Password.Confirm"] = "NewPassw1!",
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync($"{Url}?handler=ChangePassword", form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);   // re-render with error, not redirect

        // Old password still works.
        var fresh = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        Assert.NotNull(fresh);
        factory.Dispose();
    }

    [Fact]
    public async Task Change_password_mismatch_fails()
    {
        var (factory, client, email) = await SignedInAsync(_sqlite, "mismatch");
        var token = await TokenAsync(client);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Password.Current"] = "Passw0rd!",
            ["Password.New"] = "NewPassw1!",
            ["Password.Confirm"] = "DIFFERENT1!",
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync($"{Url}?handler=ChangePassword", form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var fresh = await TestLogin.SignInAsync(factory, email, "Passw0rd!");  // unchanged
        Assert.NotNull(fresh);
        factory.Dispose();
    }

    [Fact]
    public async Task Dashboard_links_to_profile()
    {
        var (factory, client, _) = await SignedInAsync(_sqlite, "dashlink");
        var body = await (await client.GetAsync("/Dashboard/Admin")).Content.ReadAsStringAsync();
        Assert.Contains("/Account/Profile", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Registration_captures_full_name()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"reg.fullname.{Guid.NewGuid():N}@test.test";

        var getResp = await client.GetAsync("/Account/Register");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "דנה כהן",
            ["Email"] = email,
            ["Password"] = "Passw0rd!",
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync("/Account/Register", form);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);   // → /Account/Login

        using var s = factory.Services.CreateScope();
        var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await um.FindByEmailAsync(email);
        Assert.Equal("דנה כהן", user!.FullName);
    }
}
