using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class ClientsPageTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ClientsPageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateCsClientAsync(
        SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"cs.clients{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.CustomerService);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"cs.clients{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    [Fact]
    public async Task Clients_index_active_filter_hides_inactive_but_all_filter_shows_them()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_active_filter");

        // Arrange: seed one active client and one inactive client
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Clients.Add(new Client
            {
                Name = "תלמיד פעיל לבדיקה",
                IsActive = true
            });
            db.Clients.Add(new Client
            {
                Name = "תלמיד לא פעיל לבדיקה",
                IsActive = false
            });
            await db.SaveChangesAsync();
        }

        // Act 1: default index (activeOnly=true, the "פעילים" filter)
        var activeResp = await client.GetAsync("/People/Clients");
        Assert.Equal(HttpStatusCode.OK, activeResp.StatusCode);
        var activeRaw = await activeResp.Content.ReadAsStringAsync();
        // Razor HTML-encodes Hebrew; decode before asserting
        var activeBody = System.Net.WebUtility.HtmlDecode(activeRaw);

        Assert.Contains("תלמיד פעיל לבדיקה", activeBody);
        Assert.DoesNotContain("תלמיד לא פעיל לבדיקה", activeBody);

        // Act 2: activeOnly=false (the "כולם" filter) — both clients must appear
        var allResp = await client.GetAsync("/People/Clients?activeOnly=false");
        Assert.Equal(HttpStatusCode.OK, allResp.StatusCode);
        var allRaw = await allResp.Content.ReadAsStringAsync();
        var allBody = System.Net.WebUtility.HtmlDecode(allRaw);

        Assert.Contains("תלמיד פעיל לבדיקה", allBody);
        Assert.Contains("תלמיד לא פעיל לבדיקה", allBody);

        factory.Dispose();
    }
}
