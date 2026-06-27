using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Account;

// F10 (non-email half): any logged-in user edits their display name + changes their password.
[Authorize]
public class ProfileModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;

    public ProfileModel(UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        _users = users;
        _signIn = signIn;
    }

    [BindProperty] public string? FullName { get; set; }
    [BindProperty] public PasswordInput Password { get; set; } = new();

    public string Email { get; private set; } = "";
    public string Greeting => string.IsNullOrWhiteSpace(FullName)
        ? (Email.Contains('@') ? Email[..Email.IndexOf('@')] : Email)
        : FullName!;

    // No [Required] annotations — both forms share this PageModel, so annotation validation on the
    // password fields would also fire when the "save profile" form posts (empty password fields).
    public class PasswordInput
    {
        public string Current { get; set; } = "";
        public string New { get; set; } = "";
        public string Confirm { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Forbid();
        FullName = user.FullName;
        Email = user.Email ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostSaveProfileAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Forbid();

        user.FullName = string.IsNullOrWhiteSpace(FullName) ? null : FullName.Trim();
        await _users.UpdateAsync(user);
        TempData["SuccessMessage"] = "הפרופיל עודכן";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Forbid();
        Email = user.Email ?? "";
        FullName = user.FullName;

        if (string.IsNullOrEmpty(Password.Current) || string.IsNullOrEmpty(Password.New))
        {
            ModelState.AddModelError("Password.New", "יש למלא סיסמה נוכחית וחדשה");
            return Page();
        }
        if (Password.New.Length < 8)
        {
            ModelState.AddModelError("Password.New", "הסיסמה חייבת לכלול לפחות 8 תווים");
            return Page();
        }
        if (Password.New != Password.Confirm)
        {
            ModelState.AddModelError("Password.Confirm", "הסיסמאות אינן תואמות");
            return Page();
        }

        var result = await _users.ChangePasswordAsync(user, Password.Current, Password.New);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError("Password.Current", e.Description);
            return Page();
        }

        await _signIn.RefreshSignInAsync(user);   // re-issue the cookie with the new security stamp
        TempData["SuccessMessage"] = "הסיסמה עודכנה";
        return RedirectToPage();
    }

    private async Task<AppUser?> CurrentUserAsync()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is null ? null : await _users.FindByIdAsync(id);
    }
}
