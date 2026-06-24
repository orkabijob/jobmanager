using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;

namespace Orkabi.Web.Pages.Attendance;

[Authorize(Roles = AppRoles.Instructor + "," + AppRoles.Admin)]
public class IndexModel : PageModel
{
    private readonly SchedulingService _scheduling;
    private readonly EnrollmentService _enrollments;

    public IndexModel(SchedulingService scheduling, EnrollmentService enrollments)
    {
        _scheduling = scheduling;
        _enrollments = enrollments;
    }

    public record RosterEntry(int ClientId, string Name, string? Phone, bool IsTryout);

    public int ShiftInstanceId { get; private set; }
    public int? LessonLogId { get; private set; }
    public string? ModelName { get; private set; }
    public bool HasModel { get; private set; }
    public string ClassName { get; private set; } = "";
    public string DateLine { get; private set; } = "";
    public List<RosterEntry> Roster { get; private set; } = new();
    public List<RosterEntry> Tryouts { get; private set; } = new();
    public string IdempotencyKey { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(int shiftInstanceId)
    {
        ShiftInstanceId = shiftInstanceId;

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = User.IsInRole(AppRoles.Admin);

        // Date-scope guard. Admin bypasses the date-scope (resource-based authz §B).
        if (!isAdmin && !await _scheduling.CanAccessShiftAsync(shiftInstanceId, userId))
            return Forbid();

        var instance = await _scheduling.GetShiftInstanceAsync(shiftInstanceId);
        if (instance is null) return NotFound();

        var cls = instance.Template.Class;
        ClassName = cls.Name;

        var heCulture = new System.Globalization.CultureInfo("he-IL");
        DateLine = instance.Date.ToString("d MMMM", heCulture);

        // Resolve (and get-or-create) the LessonLog + current model for this shift.
        var (lessonLogId, _, modelName) = await _scheduling.ResolveLessonLogForAttendanceAsync(shiftInstanceId);
        LessonLogId = lessonLogId;
        ModelName = modelName;
        HasModel = lessonLogId is not null;

        // Roster = class enrollments (non-Dropped). Tryouts pinned to the tray.
        var enrollments = await _enrollments.ListByClassAsync(cls.Id);
        foreach (var e in enrollments)
        {
            var entry = new RosterEntry(e.ClientId, e.Client.Name, e.Client.ParentPhone,
                e.IsTryout || e.Status == EnrollmentStatus.Tryout);
            if (entry.IsTryout) Tryouts.Add(entry);
            else Roster.Add(entry);
        }

        // One batch idempotency key per page session (a reload mints a new one; that's intended
        // — a fresh session is a fresh submit. Within a session JS reuses this for retries).
        IdempotencyKey = $"att-{shiftInstanceId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        return Page();
    }
}
