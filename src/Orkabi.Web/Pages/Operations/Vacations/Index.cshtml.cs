using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Operations;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.Operations.Vacations;

[Authorize(Roles = AppRoles.InstructorOrAdmin)]
public class IndexModel : PageModel
{
    private readonly OperationsService _ops;
    private readonly AppDbContext _db;

    public IndexModel(OperationsService ops, AppDbContext db)
    {
        _ops = ops;
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<VacationRequest> MyVacations { get; private set; } = new();
    public List<VacationRequest> PendingVacations { get; private set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "יש לבחור תאריכים")]
        public DateOnly StartDate { get; set; }

        [Required(ErrorMessage = "יש לבחור תאריכים")]
        public DateOnly EndDate { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
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

        if (Input.EndDate < Input.StartDate)
        {
            ModelState.AddModelError("Input.EndDate", "תאריך הסיום חייב להיות אחרי תאריך ההתחלה");
            return Page();
        }

        var todayIsrael = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        if (Input.StartDate < todayIsrael)
        {
            ModelState.AddModelError("Input.StartDate", "תאריך ההתחלה חייב להיות היום או בעתיד");
            return Page();
        }

        await _ops.RequestVacationAsync(userId, Input.StartDate, Input.EndDate, Input.Reason);
        TempData["SuccessMessage"] = "בקשת החופשה נשלחה לאישור";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        if (!User.IsInRole(AppRoles.Admin)) return Forbid();
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _ops.ApproveVacationAsync(id, userId);
        var vac = await LoadVacationRecordAsync(id);
        if (vac is null) return NotFound();
        return Partial("_VacationRow", vac);
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        if (!User.IsInRole(AppRoles.Admin)) return Forbid();
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _ops.DenyVacationAsync(id, userId, null);
        var vac = await LoadVacationRecordAsync(id);
        if (vac is null) return NotFound();
        return Partial("_VacationRow", vac);
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        // Instructor cancels their OWN pending request — the service guards ownership + Pending.
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            await _ops.CancelVacationAsync(id, userId);
            TempData["SuccessMessage"] = "בקשת החופשה בוטלה";
        }
        catch (InvalidOperationException)
        {
            TempData["SuccessMessage"] = "לא ניתן לבטל בקשה זו";
        }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = User.IsInRole(AppRoles.Admin);

        if (isAdmin)
            PendingVacations = await _ops.ListPendingVacationsAsync();
        else
            MyVacations = await _ops.ListVacationsByInstructorAsync(userId);
    }

    private Task<VacationRequest?> LoadVacationRecordAsync(int id) =>
        _db.VacationRequests
            .Include(v => v.Instructor)
            .Include(v => v.ApprovedByUser)
            .FirstOrDefaultAsync(v => v.Id == id);
}
