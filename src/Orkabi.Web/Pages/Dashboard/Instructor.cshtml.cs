using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Dashboard;

[Authorize(Roles = AppRoles.Instructor)]
public class InstructorModel : PageModel
{
    public void OnGet() { }
}
