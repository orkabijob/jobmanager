using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// /Help — a role-agnostic guide page ([Authorize], any role). Anonymous is bounced to login
/// like every other authed page; an authenticated user renders the guide; the per-area cards are
/// role-gated (mirroring each area's [Authorize]) so users don't get AccessDenied dead-ends.
/// </summary>
public class HelpPageTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public HelpPageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)>
        ClientAsync(SqliteFixture sqlite, string role, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"{role.ToLower()}.help{suffix}@test.test";
            if (await um.FindByEmailAsync(email) is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, role);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"{role.ToLower()}.help{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    // ── 1: anonymous → 302 to login (the auth seam holds for /Help) ──────────────
    [Fact]
    public async Task Anonymous_is_redirected_to_login()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/Help");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    // ── 2: an authenticated user (any role) renders the guide ────────────────────
    [Fact]
    public async Task Instructor_can_view_help_guide()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Instructor, "_ins");
        var resp = await client.GetAsync("/Help");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("עזרה ומדריך", body);     // page heading
        Assert.Contains("ניהול משתמשים", body);   // user-management section
        Assert.Contains("שאלות נפוצות", body);     // FAQ section

        factory.Dispose();
    }

    // ── 3: area cards are role-gated — instructor sees their cards, not People ────
    [Fact]
    public async Task Instructor_sees_only_their_area_cards()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Instructor, "_ins2");
        var resp = await client.GetAsync("/Help");
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("ההזמנות של הכיתה שלי", body);   // InstructorOrAdmin card visible
        Assert.DoesNotContain("בתי ספר, כיתות, לקוחות", body);   // People (CS/Admin) card hidden

        factory.Dispose();
    }

    // ── 4: CS sees the People management card (and not the instructor-only one) ───
    [Fact]
    public async Task Cs_sees_people_card_but_not_instructor_card()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "_cs");
        var resp = await client.GetAsync("/Help");
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("בתי ספר, כיתות, לקוחות", body);          // People card visible to CS
        Assert.DoesNotContain("ההזמנות של הכיתה שלי", body);      // instructor-only card hidden

        factory.Dispose();
    }
}
