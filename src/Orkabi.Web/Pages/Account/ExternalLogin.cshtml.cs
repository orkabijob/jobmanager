using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Account;

public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly UserManager<AppUser> _users;

    public ExternalLoginModel(SignInManager<AppUser> s, UserManager<AppUser> u)
    {
        _signIn = s;
        _users = u;
    }

    public IActionResult OnGet(string provider)
    {
        var props = _signIn.ConfigureExternalAuthenticationProperties(
            provider, "/Account/ExternalLogin?handler=Callback");
        return Challenge(props, provider);
    }

    public async Task<IActionResult> OnGetCallbackAsync()
    {
        var info = await _signIn.GetExternalLoginInfoAsync();
        if (info is null) return RedirectToPage("/Account/Login");

        var signin = await _signIn.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: true);
        if (signin.Succeeded) return LocalRedirect("/");

        var email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email)) return RedirectToPage("/Account/Login");

        var user = await _users.FindByEmailAsync(email)
                   ?? new AppUser { UserName = email, Email = email };
        if (user.Id == 0)
        {
            var createResult = await _users.CreateAsync(user);
            if (!createResult.Succeeded) return RedirectToPage("/Account/Login");
        }
        var addLoginResult = await _users.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded) return RedirectToPage("/Account/Login");
        await _signIn.SignInAsync(user, isPersistent: true);
        // NOTE: a freshly-provisioned Google user has NO role yet → the Index router (Task 9)
        // sends them to AccessDenied ("ממתין לשיוך תפקיד" / awaiting role assignment) until an
        // Admin assigns one. This is intended for an internal tool — document so QA doesn't file it as a bug.
        return LocalRedirect("/");
    }
}
