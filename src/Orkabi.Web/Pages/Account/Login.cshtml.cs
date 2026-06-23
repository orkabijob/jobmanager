using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;

    public LoginModel(SignInManager<AppUser> signIn) => _signIn = signIn;

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await _signIn.PasswordSignInAsync(Email, Password, isPersistent: true, lockoutOnFailure: false);
        if (result.Succeeded) return LocalRedirect("/");
        Error = "אימייל או סיסמה שגויים";
        return Page();
    }
}
