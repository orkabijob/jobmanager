using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.Dashboard;

// Instructor is the primary audience; Admin may also view the instructor "today" home.
[Authorize(Roles = AppRoles.Instructor + "," + AppRoles.Admin)]
public class InstructorModel : PageModel
{
    private readonly SchedulingService _scheduling;
    private readonly EnrollmentService _enrollments;
    private readonly CurriculumService _curriculum;
    private readonly ClassService _classes;
    private readonly UserManager<AppUser> _users;

    public InstructorModel(
        SchedulingService scheduling,
        EnrollmentService enrollments,
        CurriculumService curriculum,
        ClassService classes,
        UserManager<AppUser> users)
    {
        _scheduling = scheduling;
        _enrollments = enrollments;
        _curriculum = curriculum;
        _classes = classes;
        _users = users;
    }

    /// <summary>One openable/locked shift row on the "today" home.</summary>
    public record ShiftCardVm(
        int ShiftInstanceId,
        string ClassLine,
        string TimeRange,
        TimeOnly StartTime,
        string? ModelName,
        int RosterCount,
        bool IsLocked);

    public string Greeting { get; private set; } = "";
    public string TodayLine { get; private set; } = "";
    public List<ShiftCardVm> Shifts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var heCulture = new System.Globalization.CultureInfo("he-IL");
        var nowIsrael = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz);
        var today = DateOnly.FromDateTime(nowIsrael);

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var me = await _users.FindByIdAsync(userId.ToString());
        Greeting = !string.IsNullOrWhiteSpace(me?.FullName)
            ? me!.FullName!
            : FirstNameFromIdentity();
        TodayLine = $"{today.ToString("dddd", heCulture)} · {today.ToString("d MMMM", heCulture)}";

        var instances = await _scheduling.ListTodayForInstructorAsync(userId);

        var nowTime = TimeOnly.FromDateTime(nowIsrael);

        foreach (var inst in instances.OrderBy(i => i.Template.StartTime))
        {
            // Load the class WITH its School (the ListTodayForInstructorAsync include stops at Class).
            var cls = await _classes.GetAsync(inst.Template.ClassId) ?? inst.Template.Class;
            var roster = await _enrollments.ListByClassAsync(cls.Id);

            string? modelName = await ResolveCurrentModelNameAsync(cls);

            // "Locked" until the shift's start time today (date-scope already guarantees today).
            var locked = nowTime < inst.Template.StartTime;

            var schoolName = cls.School?.Name;
            var classLine = string.IsNullOrWhiteSpace(schoolName) ? cls.Name : $"{cls.Name} · {schoolName}";

            Shifts.Add(new ShiftCardVm(
                ShiftInstanceId: inst.Id,
                ClassLine: classLine,
                TimeRange: $"{inst.Template.StartTime:HH\\:mm}–{inst.Template.EndTime:HH\\:mm}",
                StartTime: inst.Template.StartTime,
                ModelName: modelName,
                RosterCount: roster.Count,
                IsLocked: locked));
        }
    }

    private string FirstNameFromIdentity()
    {
        var name = User.Identity?.Name ?? "";
        var at = name.IndexOf('@');
        return at > 0 ? name[..at] : name;
    }

    // First model in the class's syllabus = the "current" model for Slice 2.
    // (A precise "first incomplete" resolution is a later refinement — flagged in the report.)
    private async Task<string?> ResolveCurrentModelNameAsync(Class cls)
    {
        if (cls.SyllabusId is not int syllabusId) return null;
        var syllabus = await _curriculum.GetSyllabusAsync(syllabusId);
        var first = syllabus?.SyllabusModels.OrderBy(sm => sm.OrderIndex).FirstOrDefault();
        return first?.Model?.Name;
    }
}
