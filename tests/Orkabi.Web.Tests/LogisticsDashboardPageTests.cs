using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// TDD tests for Slice-5 Task 4: Logistics real dashboard page.
/// Written before implementation (RED first).
/// </summary>
public class LogisticsDashboardPageTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public LogisticsDashboardPageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)>
        CreateLogisticsClientAsync(SqliteFixture sqlite, string suffix, string? fullName = null)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        string email = $"log.real{suffix}@test.test";
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email, FullName = fullName };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.Logistics);
            }
        }
        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        return (factory, client);
    }

    private static async Task<(OrkabiAppFactory factory, HttpClient client)>
        CreateCsClientAsync(SqliteFixture sqlite, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        string email = $"cs.logdenied{suffix}@test.test";
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.CustomerService);
            }
        }
        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        return (factory, client);
    }

    // ── test 11: Logistics GET 200 as Logistics user ──────────────────────────

    [Fact]
    public async Task Logistics_dashboard_returns_200_for_Logistics_user_with_correct_copy()
    {
        var (factory, client) = await CreateLogisticsClientAsync(_sqlite, "_200");

        var resp = await client.GetAsync("/Dashboard/Logistics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("לוגיסטיקה", body);
        Assert.Contains("שלום,", body);
        Assert.DoesNotContain("dash-stub", body);

        factory.Dispose();
    }

    // ── test 12: Logistics dashboard denied for CS user ──────────────────────

    [Fact]
    public async Task Logistics_dashboard_denied_for_CS_user()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_logdenied");

        var resp = await client.GetAsync("/Dashboard/Logistics");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString() ?? "");

        factory.Dispose();
    }

    // ── test 13: Logistics greeting shows FullName ────────────────────────────

    [Fact]
    public async Task Logistics_greeting_shows_FullName_in_body()
    {
        const string fullName = "דני לוי";
        var (factory, client) = await CreateLogisticsClientAsync(_sqlite, "_greet", fullName);

        var resp = await client.GetAsync("/Dashboard/Logistics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains($"שלום, {fullName}", body);

        factory.Dispose();
    }
}
