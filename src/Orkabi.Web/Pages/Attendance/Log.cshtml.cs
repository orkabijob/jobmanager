using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;
using CurriculumModel = Orkabi.Web.Modules.Curriculum.Model;

namespace Orkabi.Web.Pages.Attendance;

[Authorize(Roles = AppRoles.Instructor + "," + AppRoles.Admin)]
public class LogModel : PageModel
{
    private readonly SchedulingService _scheduling;
    private readonly CurriculumService _curriculum;

    public LogModel(SchedulingService scheduling, CurriculumService curriculum)
    {
        _scheduling = scheduling;
        _curriculum = curriculum;
    }

    /// <summary>VM for _LessonPacing — the "X מתוך N" chip + bar.</summary>
    public record PacingVm(string? ModelName, int Spent, int Expected, bool Over, bool HasModel);

    /// <summary>VM for _LessonStatus — status segment + notes + saved flag.</summary>
    public record StatusVm(LessonLogStatus Status, string? Notes, bool Saved);

    public int ShiftInstanceId { get; private set; }
    public int ClassId { get; private set; }
    public string ContextLine { get; private set; } = "";
    public List<CurriculumModel> Models { get; private set; } = new();
    public int? SelectedModelId { get; private set; }
    public PacingVm Pacing { get; private set; } = new(null, 0, 0, false, false);
    public StatusVm Status { get; private set; } = new(LessonLogStatus.InProgress, null, false);

    private async Task<IActionResult?> GuardAndLoadAsync(int shiftInstanceId)
    {
        ShiftInstanceId = shiftInstanceId;

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = User.IsInRole(AppRoles.Admin);
        if (!isAdmin && !await _scheduling.CanAccessShiftAsync(shiftInstanceId, userId))
            return Forbid();

        var instance = await _scheduling.GetShiftInstanceAsync(shiftInstanceId);
        if (instance is null) return NotFound();

        ClassId = instance.Template.ClassId;
        var cls = instance.Template.Class;
        var heCulture = new System.Globalization.CultureInfo("he-IL");
        var schoolName = cls.School?.Name;
        ContextLine = string.IsNullOrWhiteSpace(schoolName)
            ? $"{cls.Name} · {instance.Date.ToString("dddd", heCulture)} {instance.Date.ToString("d MMMM", heCulture)}"
            : $"{cls.Name} · {schoolName} · {instance.Date.ToString("dddd", heCulture)} {instance.Date.ToString("d MMMM", heCulture)}";

        // Models available to pick = the class's syllabus models (in order).
        if (cls.SyllabusId is int sid)
        {
            var syllabus = await _curriculum.GetSyllabusAsync(sid);
            Models = syllabus?.SyllabusModels.OrderBy(sm => sm.OrderIndex).Select(sm => sm.Model).ToList()
                     ?? new List<CurriculumModel>();
        }
        return null;
    }

    public async Task<IActionResult> OnGetAsync(int shiftInstanceId)
    {
        var guard = await GuardAndLoadAsync(shiftInstanceId);
        if (guard is not null) return guard;

        // Hydrate from an existing log if present; else the resolved current model.
        var (lessonLogId, modelId, modelName) = await _scheduling.ResolveLessonLogForAttendanceAsync(shiftInstanceId);
        SelectedModelId = modelId;

        if (modelId is int mid)
        {
            var (spent, expected, over) = await _scheduling.ComputePacingAsync(ClassId, mid);
            Pacing = new PacingVm(modelName, spent, expected, over, true);
        }

        // Existing status/notes (if a log exists).
        var existing = lessonLogId is null ? null : await _scheduling.GetLessonLogAsync(shiftInstanceId);
        if (existing is not null)
            Status = new StatusVm(existing.Status, existing.InstructorNotes, false);

        return Page();
    }

    public async Task<IActionResult> OnPostPaceAsync(int shiftInstanceId, int modelId)
    {
        var guard = await GuardAndLoadAsync(shiftInstanceId);
        if (guard is not null) return guard;

        SelectedModelId = modelId;
        var model = await _curriculum.GetModelAsync(modelId);
        if (model is null)
        {
            Pacing = new PacingVm(null, 0, 0, false, false);
            return Partial("_LessonPacing", Pacing);
        }

        var (spent, expected, over) = await _scheduling.ComputePacingAsync(ClassId, modelId);
        Pacing = new PacingVm(model.Name, spent, expected, over, true);
        return Partial("_LessonPacing", Pacing);
    }

    public async Task<IActionResult> OnPostSaveLogAsync(int shiftInstanceId, int modelId, string status, string? notes)
    {
        var guard = await GuardAndLoadAsync(shiftInstanceId);
        if (guard is not null) return guard;

        var logStatus = string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
            ? LessonLogStatus.Completed
            : LessonLogStatus.InProgress;

        await _scheduling.SaveLessonLogAsync(shiftInstanceId, modelId, logStatus, notes);

        Status = new StatusVm(logStatus, notes, true);
        return Partial("_LessonStatus", Status);
    }
}
