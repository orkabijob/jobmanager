using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.Scheduling;

// F17 — the instructor's read-only "my schedule" (next 7 or 30 days). The dashboard shows today only.
[Authorize(Roles = AppRoles.InstructorOrAdmin)]
public class MyScheduleModel : PageModel
{
    private readonly SchedulingService _scheduling;
    public MyScheduleModel(SchedulingService scheduling) => _scheduling = scheduling;

    [BindProperty(SupportsGet = true)]
    public int Days { get; set; } = 30;

    public List<ShiftInstance> Shifts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Days = Days == 7 ? 7 : 30;   // only 7 or 30
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        Shifts = await _scheduling.ListUpcomingForInstructorAsync(userId, today, today.AddDays(Days));
    }
}
