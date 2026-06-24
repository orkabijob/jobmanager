using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;

namespace Orkabi.Web.Pages.Scheduling.Substitutions;

[Authorize(Roles = AppRoles.Admin)]
public class IndexModel : PageModel
{
    private readonly SchedulingService _scheduling;
    private readonly AppDbContext _db;

    public IndexModel(SchedulingService scheduling, AppDbContext db)
    {
        _scheduling = scheduling;
        _db = db;
    }

    public List<SubstitutionRequest> Substitutions { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Substitutions = await _scheduling.ListPendingSubstitutionsAsync();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _scheduling.ApproveSubstitutionAsync(id, userId);
        var request = await LoadRequestAsync(id);
        if (request is null) return NotFound();
        return Partial("_SubRow", request);
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _scheduling.DenySubstitutionAsync(id, userId);
        var request = await LoadRequestAsync(id);
        if (request is null) return NotFound();
        return Partial("_SubRow", request);
    }

    private Task<SubstitutionRequest?> LoadRequestAsync(int id) =>
        _db.SubstitutionRequests
            .IgnoreQueryFilters()
            .Include(r => r.RequestingInstructor)
            .Include(r => r.SubstituteInstructor)
            .Include(r => r.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .Include(r => r.ApprovedByUser)
            .FirstOrDefaultAsync(r => r.Id == id);
}
