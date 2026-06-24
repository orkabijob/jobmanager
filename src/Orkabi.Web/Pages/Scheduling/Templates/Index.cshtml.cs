using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;

namespace Orkabi.Web.Pages.Scheduling.Templates;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class IndexModel : PageModel
{
    private readonly SchedulingService _scheduling;
    public IndexModel(SchedulingService scheduling) => _scheduling = scheduling;

    public List<ShiftTemplate> Templates { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Templates = await _scheduling.ListTemplatesAsync(null, null);
    }

    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday    => "ראשון",
        DayOfWeek.Monday    => "שני",
        DayOfWeek.Tuesday   => "שלישי",
        DayOfWeek.Wednesday => "רביעי",
        DayOfWeek.Thursday  => "חמישי",
        DayOfWeek.Friday    => "שישי",
        DayOfWeek.Saturday  => "שבת",
        _                   => day.ToString()
    };
}
