using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Orkabi.Web.Pages;

// [Authorize] only (no role): any signed-in user can read Help — including a freshly-registered
// user with no role yet, so the "ask an admin to assign you a role" guidance is reachable from
// the very state (AccessDenied) where they need it. Content is static; role-gating of the
// area cards happens in the view via User.IsInRole.
[Authorize]
public class HelpModel : PageModel
{
    /// <summary>First name / local-part of the email, for the topbar greeting (mirrors other pages).</summary>
    public string Greeting => User.Identity?.Name?.Split('@')[0] ?? "";

    public void OnGet() { }
}
