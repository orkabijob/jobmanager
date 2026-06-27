using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// /Admin/Users — the admin-only user &amp; role management surface. Covers the auth seam,
/// the page wiring (create / assign role), and the end-value: assigning a role unblocks a
/// user who was previously stuck at AccessDenied. TDD: written before implementation.
/// </summary>
public class UserAdminPageTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public UserAdminPageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static OrkabiAppFactory Factory(SqliteFixture sqlite) =>
        new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();

    private static async Task<AppUser> SeedAsync(IServiceProvider sp, string email, string? role, string pwd = "Passw0rd!")
    {
        var um = sp.GetRequiredService<UserManager<AppUser>>();
        if (await um.FindByEmailAsync(email) is { } existing) return existing;
        var u = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        await um.CreateAsync(u, pwd);
        if (role is not null) await um.AddToRoleAsync(u, role);
        return u;
    }

    private static HttpClient Anon(OrkabiAppFactory f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── 1: anonymous → login ─────────────────────────────────────────────────────
    [Fact]
    public async Task Anonymous_is_redirected_to_login()
    {
        using var factory = Factory(_sqlite);
        var resp = await Anon(factory).GetAsync("/Admin/Users");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    // ── 2: a non-admin (CS) is forbidden → AccessDenied ──────────────────────────
    [Fact]
    public async Task Non_admin_is_denied()
    {
        using var factory = Factory(_sqlite);
        using (var s = factory.Services.CreateScope())
            await SeedAsync(s.ServiceProvider, "cs.users@test.test", AppRoles.CustomerService);
        var client = await TestLogin.SignInAsync(factory, "cs.users@test.test", "Passw0rd!");

        var resp = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/AccessDenied", resp.Headers.Location?.ToString());
    }

    // ── 3: an admin sees the user list ───────────────────────────────────────────
    [Fact]
    public async Task Admin_sees_the_user_list()
    {
        using var factory = Factory(_sqlite);
        using (var s = factory.Services.CreateScope())
        {
            await SeedAsync(s.ServiceProvider, "admin.list@test.test", AppRoles.Admin);
            await SeedAsync(s.ServiceProvider, "someinstructor@test.test", AppRoles.Instructor);
        }
        var client = await TestLogin.SignInAsync(factory, "admin.list@test.test", "Passw0rd!");

        var resp = await client.GetAsync("/Admin/Users");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("someinstructor@test.test", body);
    }

    // ── 4: an admin creates a user through the Create page ───────────────────────
    [Fact]
    public async Task Admin_can_create_a_user_via_the_page()
    {
        using var factory = Factory(_sqlite);
        using (var s = factory.Services.CreateScope())
            await SeedAsync(s.ServiceProvider, "admin.create@test.test", AppRoles.Admin);
        var client = await TestLogin.SignInAsync(factory, "admin.create@test.test", "Passw0rd!");

        var getResp = await client.GetAsync("/Admin/Users/Create");
        getResp.EnsureSuccessStatusCode();
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "created.user@test.test",
            ["Input.FullName"] = "משתמש נוצר",
            ["Input.Password"] = "Passw0rd!",
            ["Input.Role"] = AppRoles.Logistics,
            ["__RequestVerificationToken"] = token
        });
        var postResp = await client.PostAsync("/Admin/Users/Create", form);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        using var verify = factory.Services.CreateScope();
        var um = verify.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var created = await um.FindByEmailAsync("created.user@test.test");
        Assert.NotNull(created);
        Assert.True(await um.IsInRoleAsync(created!, AppRoles.Logistics));
    }

    // ── 5: an admin assigns a role through the Edit page ─────────────────────────
    [Fact]
    public async Task Admin_can_assign_a_role_via_the_edit_page()
    {
        using var factory = Factory(_sqlite);
        AppUser target;
        using (var s = factory.Services.CreateScope())
        {
            await SeedAsync(s.ServiceProvider, "admin.assign@test.test", AppRoles.Admin);
            target = await SeedAsync(s.ServiceProvider, "norole.assign@test.test", role: null);
        }
        var client = await TestLogin.SignInAsync(factory, "admin.assign@test.test", "Passw0rd!");

        var getResp = await client.GetAsync($"/Admin/Users/Edit/{target.Id}");
        getResp.EnsureSuccessStatusCode();
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("SelectedRoles", AppRoles.Instructor),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });
        var postResp = await client.PostAsync($"/Admin/Users/Edit/{target.Id}?handler=Roles", form);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        using var verify = factory.Services.CreateScope();
        var um = verify.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var reloaded = await um.FindByIdAsync(target.Id.ToString());
        Assert.True(await um.IsInRoleAsync(reloaded!, AppRoles.Instructor));
    }

    // ── 6: assigning a role UNBLOCKS a previously-stuck user (the whole point) ────
    [Fact]
    public async Task Assigning_a_role_unblocks_a_stuck_user()
    {
        using var factory = Factory(_sqlite);
        AppUser stuck;
        using (var s = factory.Services.CreateScope())
            stuck = await SeedAsync(s.ServiceProvider, "stuck.user@test.test", role: null);

        // Before: a no-role user lands on AccessDenied via the role router.
        var before = await TestLogin.SignInAsync(factory, "stuck.user@test.test", "Passw0rd!");
        var beforeHome = await before.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, beforeHome.StatusCode);
        Assert.Contains("/Account/AccessDenied", beforeHome.Headers.Location?.ToString());

        // Admin assigns the Instructor role.
        using (var s = factory.Services.CreateScope())
            await s.ServiceProvider.GetRequiredService<UserAdminService>()
                   .SetRolesAsync(stuck.Id, new[] { AppRoles.Instructor });

        // After: a fresh sign-in carries the new role → routes to the instructor dashboard.
        var after = await TestLogin.SignInAsync(factory, "stuck.user@test.test", "Passw0rd!");
        var afterHome = await after.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, afterHome.StatusCode);
        Assert.Contains("/Dashboard/Instructor", afterHome.Headers.Location?.ToString());
    }
}
