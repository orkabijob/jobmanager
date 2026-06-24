using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Operations;
using Orkabi.Web.Modules.Scheduling;

namespace Orkabi.Web.Pages.Operations.Incidents;

[Authorize]
public class IndexModel : PageModel
{
    private readonly OperationsService _ops;
    private readonly SchedulingService _scheduling;

    public IndexModel(OperationsService ops, SchedulingService scheduling)
    {
        _ops = ops;
        _scheduling = scheduling;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<ShiftInstance> RecentShifts { get; private set; } = new();
    public List<IncidentReport> MyIncidents { get; private set; } = new();
    public List<IncidentReport> AllIncidents { get; private set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "בחרו משמרת")]
        [Range(1, int.MaxValue, ErrorMessage = "בחרו משמרת")]
        public int ShiftInstanceId { get; set; }

        [Required(ErrorMessage = "בחרו את חומרת האירוע")]
        public string Severity { get; set; } = "";

        [Required(ErrorMessage = "יש לתאר את האירוע")]
        [MaxLength(2000)]
        public string Description { get; set; } = "";
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

        if (!Enum.TryParse<IncidentSeverity>(Input.Severity, out var severity))
        {
            ModelState.AddModelError("Input.Severity", "בחרו את חומרת האירוע");
            return Page();
        }

        await _ops.SubmitIncidentReportAsync(Input.ShiftInstanceId, userId, severity, Input.Description);
        TempData["SuccessMessage"] = "הדיווח נשלח";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdminOrCs = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.CustomerService);

        RecentShifts = await _scheduling.ListRecentForInstructorAsync(userId);

        if (isAdminOrCs)
            AllIncidents = await _ops.ListIncidentsAsync();
        else
            MyIncidents = (await _ops.ListIncidentsAsync()).Where(r => r.InstructorId == userId).ToList();
    }
}
