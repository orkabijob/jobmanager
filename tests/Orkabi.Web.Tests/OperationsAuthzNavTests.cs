using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

// F5–F9 — role-correct authorization + navigation for the Operations area, the Scheduling
// "החלפות" link, and the Logistics dashboard.
public class OperationsAuthzNavTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public OperationsAuthzNavTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)>
        ClientAsync(SqliteFixture sqlite, string role, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        var email = $"navz.{role}.{suffix}@test.test";
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            if (await um.FindByEmailAsync(email) is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, role);
            }
        }
        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        return (factory, client);
    }

    private static async Task<string> BodyAsync(HttpClient client, string url)
        => WebUtility.HtmlDecode(await (await client.GetAsync(url)).Content.ReadAsStringAsync());

    // ── F9: Admin can open the Logistics dashboard ──────────────────────────────

    [Fact]
    public async Task Admin_can_open_logistics_dashboard()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Admin, "f9a");
        var resp = await client.GetAsync("/Dashboard/Logistics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    [Fact]
    public async Task Logistics_can_still_open_logistics_dashboard()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Logistics, "f9l");
        var resp = await client.GetAsync("/Dashboard/Logistics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    // ── F5: Logistics excluded from Operations hub + Incidents; CS/Instructor kept ──

    [Fact]
    public async Task Logistics_forbidden_from_operations_hub()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Logistics, "f5h");
        var resp = await client.GetAsync("/Operations");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Logistics_forbidden_from_incidents()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Logistics, "f5i");
        var resp = await client.GetAsync("/Operations/Incidents");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_can_view_operations_hub()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "f5csh");
        var resp = await client.GetAsync("/Operations");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_can_view_incidents()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "f5csi");
        var resp = await client.GetAsync("/Operations/Incidents");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_can_view_operations_hub()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Instructor, "f5inh");
        var resp = await client.GetAsync("/Operations");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    // ── F6: CS sees no dead-end links (Operations ExtraHours/Vacations, Scheduling Substitutions) ──

    [Fact]
    public async Task Cs_operations_hub_hides_extrahours_and_vacations()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "f6cs");
        var body = await BodyAsync(client, "/Operations");
        Assert.DoesNotContain("/Operations/ExtraHours", body);
        Assert.DoesNotContain("/Operations/Vacations", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_operations_hub_shows_extrahours_and_vacations()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Instructor, "f6in");
        var body = await BodyAsync(client, "/Operations");
        Assert.Contains("/Operations/ExtraHours", body);
        Assert.Contains("/Operations/Vacations", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_scheduling_hub_hides_substitutions()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "f6cssch");
        var body = await BodyAsync(client, "/Scheduling");
        Assert.DoesNotContain("/Scheduling/Substitutions", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_scheduling_subpage_hides_substitutions()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "f6cstpl");
        var body = await BodyAsync(client, "/Scheduling/Templates");
        Assert.DoesNotContain("/Scheduling/Substitutions", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Admin_scheduling_hub_shows_substitutions()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Admin, "f6adsch");
        var body = await BodyAsync(client, "/Scheduling");
        Assert.Contains("/Scheduling/Substitutions", body);
        factory.Dispose();
    }

    // ── F8: ActionItems link present for non-admins (instructor + CS) ────────────

    [Fact]
    public async Task Instructor_operations_hub_shows_action_items_link()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Instructor, "f8in");
        var body = await BodyAsync(client, "/Operations");
        Assert.Contains("/Operations/ActionItems", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_operations_hub_shows_action_items_link()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "f8cs");
        var body = await BodyAsync(client, "/Operations");
        Assert.Contains("/Operations/ActionItems", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_scheduling_template_create_hides_substitutions()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "f6tplcreate");
        var body = await BodyAsync(client, "/Scheduling/Templates/Create");
        Assert.DoesNotContain("/Scheduling/Substitutions", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Logistics_action_items_subnav_has_no_deadends()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Logistics, "aihub");
        var resp = await client.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains("/Operations/ActionItems", body);     // its own link is fine
        Assert.DoesNotContain("/Operations/Incidents", body); // would 403 for Logistics
        Assert.DoesNotContain("/Operations/ExtraHours", body);
        factory.Dispose();
    }

    // ── F7: incident submit form is instructor-only ─────────────────────────────

    [Fact]
    public async Task Cs_does_not_see_incident_submit_form()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "f7cs");
        var body = await BodyAsync(client, "/Operations/Incidents");
        Assert.DoesNotContain("Input.ShiftInstanceId", body);  // the form's shift <select> name
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_sees_incident_submit_form()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Instructor, "f7in");
        var body = await BodyAsync(client, "/Operations/Incidents");
        Assert.Contains("Input.ShiftInstanceId", body);
        factory.Dispose();
    }

    // F7 defense-in-depth: the form is hidden for CS, and the POST handler must also refuse CS
    // (view/handler symmetry — a crafted POST must not create an incident attributed to a non-instructor).
    [Fact]
    public async Task Cs_cannot_submit_incident_via_handler()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "f7post");
        // The incident form is hidden for CS, so grab a valid antiforgery token from a CS-accessible form page.
        var tokenResp = await client.GetAsync("/People/Schools/Create");
        var token = AntiForgery.Extract(await tokenResp.Content.ReadAsStringAsync());

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.ShiftInstanceId"] = "1",
            ["Input.Severity"] = "Low",
            ["Input.Description"] = "cs-must-not-create",
            ["__RequestVerificationToken"] = token
        });
        var resp = await client.PostAsync("/Operations/Incidents", form);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());  // Forbid() fired before any write
        factory.Dispose();
    }
}
