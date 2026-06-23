using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Data;

public static class DataSeeder
{
    public static async Task SeedRolesAsync(IServiceProvider sp)
    {
        var roleMgr = sp.GetRequiredService<RoleManager<AppRole>>();
        foreach (var role in AppRoles.All)
            if (!await roleMgr.RoleExistsAsync(role))
                await roleMgr.CreateAsync(new AppRole(role));
    }

    public static async Task SeedAdminAsync(IServiceProvider sp, IConfiguration cfg)
    {
        var email = cfg["SEED_ADMIN_EMAIL"];
        var pwd = cfg["SEED_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pwd)) return;

        var users = sp.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;

        var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        if ((await users.CreateAsync(admin, pwd)).Succeeded)
            await users.AddToRoleAsync(admin, AppRoles.Admin);
    }
}
