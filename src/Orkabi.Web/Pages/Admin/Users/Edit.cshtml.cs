using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Admin.Users;

[Authorize(Roles = AppRoles.Admin)]
public class EditModel : PageModel
{
    private readonly UserAdminService _users;

    public EditModel(UserAdminService users) => _users = users;

    public UserRow? TargetUser { get; private set; }

    [BindProperty] public string[] SelectedRoles { get; set; } = Array.Empty<string>();
    [BindProperty] public string? NewPassword { get; set; }

    public string? Error { get; private set; }
    public string Greeting => User.Identity?.Name?.Split('@')[0] ?? "";

    /// <summary>True when the row being edited is the signed-in admin (UI hides self-foot-guns).</summary>
    public bool IsSelf => TargetUser is not null
        && string.Equals(TargetUser.Email, User.Identity?.Name, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> LoadAsync(int id)
    {
        TargetUser = await _users.GetAsync(id);
        return TargetUser is not null;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        SelectedRoles = TargetUser!.Roles.ToArray();
        return Page();
    }

    public async Task<IActionResult> OnPostRolesAsync(int id)
    {
        var result = await _users.SetRolesAsync(id, SelectedRoles ?? Array.Empty<string>());
        if (result.Succeeded) return RedirectToPage("Index");
        return await FailAsync(id, result.Errors.Select(e => e.Description));
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var current = await _users.GetAsync(id);
        if (current is null) return NotFound();

        // Flip: a disabled user is re-enabled; an enabled user is disabled.
        var result = await _users.SetEnabledAsync(id, enabled: current.IsDisabled);
        if (result.Succeeded) return RedirectToPage("Index");
        return await FailAsync(id, result.Errors.Select(e => e.Description));
    }

    public async Task<IActionResult> OnPostPasswordAsync(int id)
    {
        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 8)
            return await FailAsync(id, new[] { "הסיסמה חייבת לכלול לפחות 8 תווים" });

        var result = await _users.ResetPasswordAsync(id, NewPassword);
        if (result.Succeeded) return RedirectToPage("Index");
        return await FailAsync(id, result.Errors.Select(e => e.Description));
    }

    private async Task<IActionResult> FailAsync(int id, IEnumerable<string> errors)
    {
        if (!await LoadAsync(id)) return NotFound();
        SelectedRoles = TargetUser!.Roles.ToArray();
        Error = string.Join(" ", errors);
        return Page();
    }
}
