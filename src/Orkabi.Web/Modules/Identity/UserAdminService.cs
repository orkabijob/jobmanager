using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Orkabi.Web.Modules.Identity;

/// <summary>A user as shown on the admin Users screen: identity + roles + enabled/disabled.</summary>
public sealed record UserRow(int Id, string Email, string? FullName, IReadOnlyList<string> Roles, bool IsDisabled);

/// <summary>
/// Admin-facing user &amp; role management — the piece the app was missing (roles were only ever
/// assignable via the env-var seed). Wraps UserManager. "Disable" = an Identity lockout with a
/// far-future end date; the login path (SignInManager.PasswordSignInAsync) honours it.
///
/// SAFETY INVARIANT: at least one ENABLED user must keep the Admin role at all times — the service
/// refuses any operation (de-admin or disable) that would leave zero enabled admins, so the app can
/// never be locked out of its own administration.
/// </summary>
public class UserAdminService
{
    private readonly UserManager<AppUser> _users;

    public UserAdminService(UserManager<AppUser> users) => _users = users;

    private static bool IsDisabled(AppUser u) =>
        u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow;

    public async Task<List<UserRow>> ListAsync(string? q = null)
    {
        var query = _users.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.Email!.Contains(q) || (u.FullName != null && u.FullName.Contains(q)));

        var users = await query.OrderBy(u => u.Email).ToListAsync();

        var rows = new List<UserRow>(users.Count);
        foreach (var u in users)
        {
            var roles = await _users.GetRolesAsync(u);
            rows.Add(new UserRow(u.Id, u.Email ?? "", u.FullName, roles.OrderBy(r => r).ToList(), IsDisabled(u)));
        }
        return rows;
    }

    public async Task<UserRow?> GetAsync(int id)
    {
        var u = await _users.FindByIdAsync(id.ToString());
        if (u is null) return null;
        var roles = await _users.GetRolesAsync(u);
        return new UserRow(u.Id, u.Email ?? "", u.FullName, roles.OrderBy(r => r).ToList(), IsDisabled(u));
    }

    public async Task<IdentityResult> CreateAsync(string email, string? fullName, string password, string role)
    {
        if (!AppRoles.All.Contains(role))
            return Fail($"תפקיד לא חוקי: {role}");

        // Internal staff tool: created accounts are email-confirmed up front (mirrors the seed admin).
        var user = new AppUser { UserName = email, Email = email, FullName = fullName, EmailConfirmed = true };
        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded) return created;

        return await _users.AddToRoleAsync(user, role);
    }

    public async Task<IdentityResult> SetRolesAsync(int userId, IReadOnlyCollection<string> roles)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return Fail("המשתמש לא נמצא");

        foreach (var r in roles)
            if (!AppRoles.All.Contains(r))
                return Fail($"תפקיד לא חוקי: {r}");

        var current = await _users.GetRolesAsync(user);
        var removingAdmin = current.Contains(AppRoles.Admin) && !roles.Contains(AppRoles.Admin);
        if (removingAdmin && !await AnotherEnabledAdminExistsAsync(user.Id))
            return Fail("חייב להישאר לפחות מנהל אחד פעיל במערכת");

        var toRemove = current.Except(roles).ToArray();
        var toAdd = roles.Except(current).ToArray();

        if (toRemove.Length > 0)
        {
            var removed = await _users.RemoveFromRolesAsync(user, toRemove);
            if (!removed.Succeeded) return removed;
        }
        if (toAdd.Length > 0)
        {
            var added = await _users.AddToRolesAsync(user, toAdd);
            if (!added.Succeeded) return added;
        }
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> SetEnabledAsync(int userId, bool enabled)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return Fail("המשתמש לא נמצא");

        if (!enabled)
        {
            // Refuse to disable the last enabled admin.
            var isEnabledAdmin = !IsDisabled(user) && await _users.IsInRoleAsync(user, AppRoles.Admin);
            if (isEnabledAdmin && !await AnotherEnabledAdminExistsAsync(user.Id))
                return Fail("חייב להישאר לפחות מנהל אחד פעיל במערכת");

            await _users.SetLockoutEnabledAsync(user, true);
            return await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }

        return await _users.SetLockoutEndDateAsync(user, null);
    }

    public async Task<IdentityResult> ResetPasswordAsync(int userId, string newPassword)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return Fail("המשתמש לא נמצא");

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        return await _users.ResetPasswordAsync(user, token, newPassword);
    }

    /// <summary>True if some OTHER user (≠ excludeUserId) is an enabled Admin.</summary>
    private async Task<bool> AnotherEnabledAdminExistsAsync(int excludeUserId)
    {
        var admins = await _users.GetUsersInRoleAsync(AppRoles.Admin);
        return admins.Any(a => a.Id != excludeUserId && !IsDisabled(a));
    }

    private static IdentityResult Fail(string description) =>
        IdentityResult.Failed(new IdentityError { Code = "OrkabiUserAdmin", Description = description });
}
