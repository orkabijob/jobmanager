using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;

namespace Orkabi.Web.Pages.Attendance;

// F13 — read-only attendance history for CS/Admin (instructors take attendance; CS had no visibility).
[Authorize(Roles = AppRoles.CsOrAdmin)]
public class HistoryModel : PageModel
{
    private readonly SchedulingService _scheduling;
    public HistoryModel(SchedulingService scheduling) => _scheduling = scheduling;

    public List<LessonHistoryRow> Lessons { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Lessons = await _scheduling.ListLessonHistoryAsync(classId: null, take: 100);
    }
}
