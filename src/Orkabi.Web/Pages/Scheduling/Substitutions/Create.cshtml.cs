using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.Scheduling.Substitutions;

// B2 — the instructor's "request a substitute" surface. The Admin approval queue lives at
// /Scheduling/Substitutions (Index, Admin-only); this page is where an instructor creates a
// pending request for one of their own future shifts, and cancels their own pending requests.
[Authorize(Roles = AppRoles.InstructorOrAdmin)]
public class CreateModel : PageModel
{
    private readonly SchedulingService _scheduling;
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _users;

    public CreateModel(SchedulingService scheduling, AppDbContext db, UserManager<AppUser> users)
    {
        _scheduling = scheduling;
        _db = db;
        _users = users;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<ShiftInstance> MyFutureShifts { get; private set; } = new();
    public List<AppUser> OtherInstructors { get; private set; } = new();
    public List<SubstitutionRequest> MyPendingRequests { get; private set; } = new();
    public HashSet<int> PendingShiftIds { get; private set; } = new();

    public class InputModel
    {
        [Range(1, int.MaxValue, ErrorMessage = "יש לבחור משמרת")]
        public int ShiftInstanceId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "יש לבחור מחליף/ה")]
        public int SubstituteInstructorId { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = CurrentUserId();
        await LoadAsync();

        if (!ModelState.IsValid)
            return Page();

        // Re-query, never trust the posted id: the shift must be one of MY future scheduled shifts.
        // (RequestSubstitutionAsync is a thin create with no authorization — the policy lives here.)
        var shift = MyFutureShifts.FirstOrDefault(i => i.Id == Input.ShiftInstanceId);
        if (shift is null)
        {
            ModelState.AddModelError("Input.ShiftInstanceId", "ניתן לבקש החלפה רק עבור משמרת עתידית שלך");
            return Page();
        }

        if (Input.SubstituteInstructorId == userId)
        {
            ModelState.AddModelError("Input.SubstituteInstructorId", "לא ניתן לבחור את עצמך כמחליף/ה");
            return Page();
        }

        if (OtherInstructors.All(u => u.Id != Input.SubstituteInstructorId))
        {
            ModelState.AddModelError("Input.SubstituteInstructorId", "יש לבחור מדריך/ה תקין/ה");
            return Page();
        }

        // One pending request per shift — guard a duplicate (the service has no dedup).
        var alreadyPending = await _db.SubstitutionRequests.AnyAsync(r =>
            r.ShiftInstanceId == Input.ShiftInstanceId && r.Status == SubstitutionStatus.Pending);
        if (alreadyPending)
        {
            ModelState.AddModelError("Input.ShiftInstanceId", "כבר קיימת בקשת החלפה ממתינה למשמרת זו");
            return Page();
        }

        await _scheduling.RequestSubstitutionAsync(Input.ShiftInstanceId, userId, Input.SubstituteInstructorId);
        TempData["SuccessMessage"] = "בקשת ההחלפה נשלחה לאישור";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var userId = CurrentUserId();
        try
        {
            // The service guards ownership + Pending; a mismatch throws, which we surface gently.
            await _scheduling.CancelSubstitutionAsync(id, userId);
            TempData["SuccessMessage"] = "בקשת ההחלפה בוטלה";
        }
        catch (InvalidOperationException)
        {
            TempData["ErrorMessage"] = "לא ניתן לבטל בקשה זו";
        }
        return RedirectToPage();
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task LoadAsync()
    {
        var userId = CurrentUserId();
        var todayIsrael = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));

        MyFutureShifts = await _db.ShiftInstances
            .Where(i => i.ActualInstructorId == userId
                     && i.Date > todayIsrael
                     && i.Status == ShiftInstanceStatus.Scheduled)
            .Include(i => i.Template).ThenInclude(t => t.Class)
            .OrderBy(i => i.Date)
            .ToListAsync();

        MyPendingRequests = await _db.SubstitutionRequests
            .Where(r => r.RequestingInstructorId == userId && r.Status == SubstitutionStatus.Pending)
            .Include(r => r.SubstituteInstructor)
            .Include(r => r.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .OrderBy(r => r.ShiftInstance.Date)
            .ToListAsync();

        PendingShiftIds = MyPendingRequests.Select(r => r.ShiftInstanceId).ToHashSet();

        // "Instructor" is just an AppUser in the role — offer everyone but me as a possible sub.
        var instructors = await _users.GetUsersInRoleAsync(AppRoles.Instructor);
        OtherInstructors = instructors
            .Where(u => u.Id != userId)
            .OrderBy(u => u.FullName ?? u.Email)
            .ToList();
    }
}
