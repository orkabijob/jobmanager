using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.Attendance;

// F13 + R4 — read-only attendance history for CS/Admin, filterable by class, with a per-lesson
// drill-down (so CS can answer "was my child present?").
[Authorize(Roles = AppRoles.CsOrAdmin)]
public class HistoryModel : PageModel
{
    private readonly SchedulingService _scheduling;
    private readonly ClassService _classes;

    public HistoryModel(SchedulingService scheduling, ClassService classes)
    {
        _scheduling = scheduling;
        _classes = classes;
    }

    [BindProperty(SupportsGet = true)]
    public int? ClassId { get; set; }

    public List<LessonHistoryRow> Lessons { get; private set; } = new();
    public List<Class> Classes { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Classes = await _classes.ListAsync(null, null, EntityStatus.Active);
        Lessons = await _scheduling.ListLessonHistoryAsync(ClassId, take: 100);
    }
}
