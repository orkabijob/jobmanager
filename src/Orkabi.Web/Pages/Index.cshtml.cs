using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages;

[Authorize]
public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        if (User.IsInRole(AppRoles.Admin)) return RedirectToPage("/Dashboard/Admin");
        if (User.IsInRole(AppRoles.CustomerService)) return RedirectToPage("/Dashboard/Cs");
        if (User.IsInRole(AppRoles.Logistics)) return RedirectToPage("/Dashboard/Logistics");
        if (User.IsInRole(AppRoles.Instructor)) return RedirectToPage("/Dashboard/Instructor");
        return RedirectToPage("/Account/AccessDenied");
    }
}
