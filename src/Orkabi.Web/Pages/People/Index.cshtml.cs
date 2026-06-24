using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.People;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class IndexModel : PageModel
{
    private readonly AcademicYearService _years;

    public IndexModel(AcademicYearService years) => _years = years;

    public AcademicYear? CurrentYear { get; private set; }

    public async Task OnGetAsync()
    {
        CurrentYear = await _years.GetCurrentAsync();
    }
}
