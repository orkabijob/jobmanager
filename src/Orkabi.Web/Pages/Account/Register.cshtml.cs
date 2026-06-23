using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public RegisterModel(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = new AppUser { UserName = Email, Email = Email };
        var result = await _userManager.CreateAsync(user, Password);
        if (result.Succeeded)
        {
            // Do NOT auto-sign-in: a freshly registered user has no role yet and would
            // immediately hit AccessDenied via the role router. Send them to Login; an
            // Admin assigns a role before they can reach a dashboard.
            return RedirectToPage("/Account/Login");
        }
        Error = string.Join(" ", result.Errors.Select(e => e.Description));
        return Page();
    }
}
