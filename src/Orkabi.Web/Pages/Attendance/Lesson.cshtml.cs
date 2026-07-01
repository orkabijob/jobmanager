using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;

namespace Orkabi.Web.Pages.Attendance;

// R4 — per-student attendance for a single lesson (drill-down from /Attendance/History).
[Authorize(Roles = AppRoles.CsOrAdmin)]
public class LessonModel : PageModel
{
    private readonly SchedulingService _scheduling;
    public LessonModel(SchedulingService scheduling) => _scheduling = scheduling;

    public LessonHistoryRow Header { get; private set; } = null!;
    public List<LessonAttendanceRow> Students { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int lessonLogId)
    {
        var header = await _scheduling.GetLessonHistoryRowAsync(lessonLogId);
        if (header is null) return NotFound();
        Header = header;
        Students = await _scheduling.ListAttendanceForLessonAsync(lessonLogId);
        return Page();
    }
}
