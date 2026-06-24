using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        var result = await users.CreateAsync(admin, pwd);
        if (!result.Succeeded)
        {
            sp.GetService<ILoggerFactory>()?.CreateLogger("DataSeeder")
              .LogWarning("Admin seed failed for {Email}: {Errors}", email,
                          string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await users.AddToRoleAsync(admin, AppRoles.Admin);
    }

    public static async Task SeedAcademicYearAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        if (await db.AcademicYears.AnyAsync()) return;   // idempotent
        db.AcademicYears.Add(new Orkabi.Web.Modules.People.AcademicYear
        {
            Label = "תשפ\"ו",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
            IsCurrent = true
        });
        await db.SaveChangesAsync();
    }
}
