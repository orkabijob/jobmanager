using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Dashboard;

[Authorize(Roles = AppRoles.CustomerService)]
public class CsModel : PageModel
{
    public void OnGet() { }
}
