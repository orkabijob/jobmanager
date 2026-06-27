using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Operations;
using Orkabi.Web.Modules.Scheduling;

namespace Orkabi.Web.Pages.Operations.ExtraHours;

[Authorize(Roles = AppRoles.InstructorOrAdmin)]
public class IndexModel : PageModel
{
    private readonly OperationsService _ops;
    private readonly SchedulingService _scheduling;
    private readonly AppDbContext _db;

    public IndexModel(OperationsService ops, SchedulingService scheduling, AppDbContext db)
    {
        _ops = ops;
        _scheduling = scheduling;
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<ShiftInstance> RecentShifts { get; private set; } = new();
    public List<Modules.Operations.ExtraHours> MySubmissions { get; private set; } = new();
    public List<Modules.Operations.ExtraHours> PendingExtraHours { get; private set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "בחרו משמרת")]
        [Range(1, int.MaxValue, ErrorMessage = "בחרו משמרת")]
        public int ShiftInstanceId { get; set; }

        [Required(ErrorMessage = "יש להזין מספר שעות (0.5 ומעלה)")]
        [Range(0.5, 99, ErrorMessage = "יש להזין מספר שעות (0.5 ומעלה)")]
        public decimal Hours { get; set; }

        [Required(ErrorMessage = "יש לפרט סיבה")]
        [MaxLength(500)]
        public string Reason { get; set; } = "";
    }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await LoadAsync();

        if (!ModelState.IsValid)
            return Page();

        await _ops.SubmitExtraHoursAsync(Input.ShiftInstanceId, userId, Input.Hours, Input.Reason);
        TempData["SuccessMessage"] = "דיווח השעות נשלח לאישור";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        if (!User.IsInRole(AppRoles.Admin)) return Forbid();
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _ops.ApproveExtraHoursAsync(id, userId);
        var record = await LoadExtraHoursRecordAsync(id);
        if (record is null) return NotFound();
        return Partial("_ExtraHoursRow", record);
    }

    public async Task<IActionResult> OnPostDenyAsync(int id)
    {
        if (!User.IsInRole(AppRoles.Admin)) return Forbid();
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _ops.DenyExtraHoursAsync(id, userId);
        var record = await LoadExtraHoursRecordAsync(id);
        if (record is null) return NotFound();
        return Partial("_ExtraHoursRow", record);
    }

    private async Task LoadAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = User.IsInRole(AppRoles.Admin);

        if (isAdmin)
        {
            PendingExtraHours = await _ops.ListPendingExtraHoursAsync();
        }
        else
        {
            RecentShifts = await _scheduling.ListRecentForInstructorAsync(userId);
            MySubmissions = await _ops.ListExtraHoursByInstructorAsync(userId);
        }
    }

    private Task<Modules.Operations.ExtraHours?> LoadExtraHoursRecordAsync(int id) =>
        _db.ExtraHours
            .Include(e => e.Instructor)
            .Include(e => e.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .Include(e => e.ApprovedByUser)
            .FirstOrDefaultAsync(e => e.Id == id);
}
