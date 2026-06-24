using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.People.Schools;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class IndexModel : PageModel
{
    private readonly SchoolService _schools;

    public IndexModel(SchoolService schools) => _schools = schools;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<School> Schools { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Schools = await _schools.ListAsync(Q);
    }
}
