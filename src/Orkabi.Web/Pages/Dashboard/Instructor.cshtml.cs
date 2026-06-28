using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.ActionHub;
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
    private readonly ClassService _classes;
    private readonly UserManager<AppUser> _users;
    private readonly ActionItemService _actionItems;

    public InstructorModel(
        SchedulingService scheduling,
        EnrollmentService enrollments,
        ClassService classes,
        UserManager<AppUser> users,
        ActionItemService actionItems)
    {
        _scheduling = scheduling;
        _enrollments = enrollments;
        _classes = classes;
        _users = users;
        _actionItems = actionItems;
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

    /// <summary>The instructor's open action items (user-assigned + Instructor-role) — the "my tickets" strip.</summary>
    public List<ActionItem> Tickets { get; private set; } = new();

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

        // Open tickets for this instructor (user-assigned + Instructor-role) — mirrors the hub queue.
        Tickets = await _actionItems.ListOpenForUserAndRoleAsync(userId, AppRoles.Instructor);
    }

    // ── Action-item type → Hebrew label + badge CSS (mirrors the dashboard convention) ──

    public static string TypeToHebrew(ActionItemType t) => t switch
    {
        ActionItemType.Gap            => "חריגת קצב",
        ActionItemType.Absence        => "היעדרות",
        ActionItemType.Dispute        => "מחלוקת",
        ActionItemType.Task           => "משימה",
        ActionItemType.Birthday       => "יום הולדת",
        ActionItemType.TryoutFollowup => "מעקב ניסיון",
        _                             => t.ToString()
    };

    public static string TypeToCssModifier(ActionItemType t) => t switch
    {
        ActionItemType.Gap            => "action-type--gap",
        ActionItemType.Absence        => "action-type--absence",
        ActionItemType.Dispute        => "action-type--dispute",
        ActionItemType.Birthday       => "action-type--birthday",
        ActionItemType.TryoutFollowup => "action-type--tryout",
        _                             => ""
    };

    private string FirstNameFromIdentity()
    {
        var name = User.Identity?.Name ?? "";
        var at = name.IndexOf('@');
        return at > 0 ? name[..at] : name;
    }

    // The class's current (first-incomplete) syllabus model — resolved by SchedulingService (F20).
    private async Task<string?> ResolveCurrentModelNameAsync(Class cls)
    {
        var (_, modelName) = await _scheduling.ResolveCurrentModelForClassAsync(cls.Id);
        return modelName;
    }
}
