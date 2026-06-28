using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Operations;

namespace Orkabi.Web.Pages.Operations;

[Authorize(Roles = AppRoles.CsOrInstructorOrAdmin)]
public class IndexModel : PageModel
{
    private readonly OperationsService _ops;
    private readonly AppDbContext _db;

    public IndexModel(OperationsService ops, AppDbContext db)
    {
        _ops = ops;
        _db = db;
    }

    public int PendingExtraHoursCount { get; private set; }
    public int IncidentsThisMonthCount { get; private set; }
    public int PendingVacationsCount { get; private set; }
    public int OpenActionItemsCount { get; private set; }

    public async Task OnGetAsync()
    {
        var now = DateTime.UtcNow;
        var firstOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        PendingExtraHoursCount = await _db.ExtraHours.CountAsync(e => e.Status == ExtraHoursStatus.Pending);
        IncidentsThisMonthCount = await _db.IncidentReports.CountAsync(r => r.CreatedAt >= firstOfMonth);
        PendingVacationsCount = await _db.VacationRequests.CountAsync(v => v.Status == VacationStatus.Pending);
        OpenActionItemsCount = await _db.ActionItems.CountAsync(a => a.Status == Orkabi.Web.Modules.ActionHub.ActionItemStatus.Open);
    }
}
