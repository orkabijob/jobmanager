using Microsoft.AspNetCore.Identity;
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
}
