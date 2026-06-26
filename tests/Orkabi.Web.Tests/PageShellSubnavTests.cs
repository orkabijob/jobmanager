using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Pages.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// Slice-5 audit follow-up: the shared _PageShell Logistics subnav must include the
/// role-gated "ההזמנות של הכיתה שלי" (MyOrders) link from the default SubnavFor map,
/// and the shell must hide it from users who cannot reach it (Logistics-only).
/// Written BEFORE implementation (TDD: RED first).
/// </summary>
public class PageShellSubnavTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public PageShellSubnavTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)>
        ClientAsync(SqliteFixture sqlite, string role, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"{role.ToLower()}.subnav{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, role);
            }
            var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
            return (factory, client);
        }
    }

    // ── 1: the default Logistics subnav map includes MyOrders (+ Orders + PackingList) ──

    [Fact]
    public void Logistics_subnav_map_includes_my_orders_link()
    {
        var items = PageShellVm.SubnavFor(NavSection.Logistics);
        Assert.Contains(items, i => i.Href == "/Logistics/Orders");
        Assert.Contains(items, i => i.Href == "/Logistics/MyOrders");
        Assert.Contains(items, i => i.Href == "/Logistics/PackingList");
    }

    // ── 2: Admin (InstructorOrAdmin) sees the MyOrders link rendered ─────────────

    [Fact]
    public async Task Admin_sees_my_orders_link_in_logistics_subnav()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Admin, "_adm");
        var resp = await client.GetAsync("/Logistics/PackingList");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("ההזמנות של הכיתה שלי", body);   // MyOrders link present for Admin
        Assert.Contains("רשימת אריזה", body);            // own page link still present

        factory.Dispose();
    }

    // ── 3: Logistics-only user does NOT see the MyOrders link (role-gated) ────────

    [Fact]
    public async Task Logistics_user_does_not_see_my_orders_link_in_subnav()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Logistics, "_log");
        var resp = await client.GetAsync("/Logistics/PackingList");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.DoesNotContain("ההזמנות של הכיתה שלי", body);   // gated away from Logistics-only
        Assert.Contains("רשימת אריזה", body);                  // but their own links remain

        factory.Dispose();
    }
}
