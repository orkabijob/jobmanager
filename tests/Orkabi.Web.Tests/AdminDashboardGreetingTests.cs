using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// TDD tests for Slice-5 Task 4: Admin dashboard dynamic greeting.
/// Written before implementation (RED first).
/// </summary>
public class AdminDashboardGreetingTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public AdminDashboardGreetingTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── test 14: Admin greeting shows FullName ────────────────────────────────

    [Fact]
    public async Task Admin_greeting_shows_FullName_in_body()
    {
        const string fullName = "יוסי מנהל";
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();

        string email = $"admin.greet.{Guid.NewGuid():N}@test.test";
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = new AppUser { UserName = email, Email = email, FullName = fullName };
            await um.CreateAsync(u, "Passw0rd!");
            await um.AddToRoleAsync(u, AppRoles.Admin);
        }

        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        var resp = await client.GetAsync("/Dashboard/Admin");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains($"שלום, {fullName}", body);
    }
}
