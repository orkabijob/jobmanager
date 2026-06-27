using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// UserAdminService — the admin-facing user/role management the app was missing entirely
/// (roles were only ever assignable via the env-var seed). TDD: written before implementation.
/// </summary>
public class UserAdminServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public UserAdminServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static OrkabiAppFactory Factory(SqliteFixture sqlite) =>
        new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();

    private static async Task<AppUser> SeedUserAsync(IServiceProvider sp, string email, string? role)
    {
        var um = sp.GetRequiredService<UserManager<AppUser>>();
        var u = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        await um.CreateAsync(u, "Passw0rd!");
        if (role is not null) await um.AddToRoleAsync(u, role);
        return u;
    }

    // The last-admin guard depends on the GLOBAL set of admins. The class-shared SqliteFixture
    // accumulates admins from sibling tests, so these guard tests run against a throwaway DB of
    // their own (no stray admins) to keep the assertion deterministic.
    private static async Task WithIsolatedDbAsync(Func<IServiceProvider, Task> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"orkabi_useradmin_{Guid.NewGuid():N}.db");
        var cs = $"Data Source={path};Foreign Keys=True;Default Timeout=30;Pooling=False";
        var factory = new OrkabiAppFactory { ConnectionString = cs }.Prepared();
        try
        {
            using var scope = factory.Services.CreateScope();
            await body(scope.ServiceProvider);
        }
        finally
        {
            factory.Dispose();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task CreateAsync_creates_user_with_the_chosen_role()
    {
        using var factory = Factory(_sqlite);
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var result = await svc.CreateAsync("newbie.create@test.test", "חבר צוות", "Passw0rd!", AppRoles.Logistics);

        Assert.True(result.Succeeded);
        var u = await um.FindByEmailAsync("newbie.create@test.test");
        Assert.NotNull(u);
        Assert.True(await um.IsInRoleAsync(u!, AppRoles.Logistics));
        Assert.Equal("חבר צוות", u!.FullName);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_role()
    {
        using var factory = Factory(_sqlite);
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var result = await svc.CreateAsync("bad.role@test.test", null, "Passw0rd!", "Wizard");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SetRolesAsync_replaces_the_users_roles()
    {
        using var factory = Factory(_sqlite);
        using var scope = factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var svc = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var u = await SeedUserAsync(scope.ServiceProvider, "setroles@test.test", AppRoles.Instructor);

        var result = await svc.SetRolesAsync(u.Id, new[] { AppRoles.CustomerService });

        Assert.True(result.Succeeded);
        Assert.False(await um.IsInRoleAsync(u, AppRoles.Instructor));
        Assert.True(await um.IsInRoleAsync(u, AppRoles.CustomerService));
    }

    [Fact]
    public Task SetRolesAsync_blocks_removing_the_admin_role_from_the_last_admin() =>
        WithIsolatedDbAsync(async sp =>
        {
            var um = sp.GetRequiredService<UserManager<AppUser>>();
            var svc = sp.GetRequiredService<UserAdminService>();
            var onlyAdmin = await SeedUserAsync(sp, "only.admin@test.test", AppRoles.Admin);

            var result = await svc.SetRolesAsync(onlyAdmin.Id, new[] { AppRoles.CustomerService });

            Assert.False(result.Succeeded);                                   // refused
            Assert.True(await um.IsInRoleAsync(onlyAdmin, AppRoles.Admin));    // still admin
        });

    [Fact]
    public async Task SetEnabledAsync_disables_a_user_so_they_show_as_disabled()
    {
        using var factory = Factory(_sqlite);
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        // keep a separate admin so the target is not the last admin
        await SeedUserAsync(scope.ServiceProvider, "keep.admin1@test.test", AppRoles.Admin);
        var target = await SeedUserAsync(scope.ServiceProvider, "todisable@test.test", AppRoles.Instructor);

        var result = await svc.SetEnabledAsync(target.Id, enabled: false);

        Assert.True(result.Succeeded);
        var row = await svc.GetAsync(target.Id);
        Assert.NotNull(row);
        Assert.True(row!.IsDisabled);
    }

    [Fact]
    public Task SetEnabledAsync_blocks_disabling_the_last_admin() =>
        WithIsolatedDbAsync(async sp =>
        {
            var svc = sp.GetRequiredService<UserAdminService>();
            var onlyAdmin = await SeedUserAsync(sp, "last.admin@test.test", AppRoles.Admin);

            var result = await svc.SetEnabledAsync(onlyAdmin.Id, enabled: false);

            Assert.False(result.Succeeded);
            var row = await svc.GetAsync(onlyAdmin.Id);
            Assert.False(row!.IsDisabled);   // still enabled
        });

    [Fact]
    public async Task ResetPasswordAsync_sets_a_new_working_password()
    {
        using var factory = Factory(_sqlite);
        using var scope = factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var svc = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var u = await SeedUserAsync(scope.ServiceProvider, "resetpw@test.test", AppRoles.Instructor);

        var result = await svc.ResetPasswordAsync(u.Id, "NewPassw0rd!");

        Assert.True(result.Succeeded);
        var reloaded = await um.FindByIdAsync(u.Id.ToString());
        Assert.True(await um.CheckPasswordAsync(reloaded!, "NewPassw0rd!"));
        Assert.False(await um.CheckPasswordAsync(reloaded!, "Passw0rd!"));
    }

    [Fact]
    public async Task ListAsync_returns_users_with_their_roles()
    {
        using var factory = Factory(_sqlite);
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        await SeedUserAsync(scope.ServiceProvider, "list.cs@test.test", AppRoles.CustomerService);

        var rows = await svc.ListAsync();

        var row = Assert.Single(rows, r => r.Email == "list.cs@test.test");
        Assert.Contains(AppRoles.CustomerService, row.Roles);
    }
}
