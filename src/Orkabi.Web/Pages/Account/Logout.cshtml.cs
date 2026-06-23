using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;

    public LogoutModel(SignInManager<AppUser> signIn) => _signIn = signIn;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
