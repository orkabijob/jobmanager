using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Dashboard;

[Authorize(Roles = AppRoles.Admin)]
public class AdminModel : PageModel
{
    public void OnGet() { }
}
