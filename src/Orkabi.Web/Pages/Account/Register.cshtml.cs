using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signIn;

    public RegisterModel(UserManager<AppUser> userManager, SignInManager<AppUser> signIn)
    {
        _userManager = userManager;
        _signIn = signIn;
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
            await _signIn.SignInAsync(user, isPersistent: false);
            return LocalRedirect("/");
        }
        Error = string.Join(" ", result.Errors.Select(e => e.Description));
        return Page();
    }
}
