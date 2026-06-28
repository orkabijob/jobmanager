using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.Logistics.Orders;

[Authorize(Roles = AppRoles.LogisticsOrAdmin)]
public class IndexModel : PageModel
{
    private readonly SupplyPacingService _supply;
    private readonly AppDbContext _db;

    public IndexModel(SupplyPacingService supply, AppDbContext db)
    {
        _supply = supply;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ClassIdFilter { get; set; }

    public List<LogisticsOrder> Orders { get; private set; } = new();
    public List<Class> Classes { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostPackAsync(int id)
    {
        if (!User.IsInRole(AppRoles.Logistics) && !User.IsInRole(AppRoles.Admin))
            return Forbid();

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _supply.MarkPackedAsync(id, userId);

        var order = await _db.LogisticsOrders
            .Include(o => o.Class)
            .Include(o => o.Model)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        return Partial("_OrderRow", order);
    }

    public async Task<IActionResult> OnPostRepackAsync(int id)
    {
        if (!User.IsInRole(AppRoles.Logistics) && !User.IsInRole(AppRoles.Admin))
            return Forbid();

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _supply.RepackDisputedAsync(id, userId);

        var order = await _db.LogisticsOrders
            .Include(o => o.Class)
            .Include(o => o.Model)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        return Partial("_OrderRow", order);
    }

    public async Task<IActionResult> OnPostGenerateAsync(int? classId)
    {
        if (!User.IsInRole(AppRoles.Logistics) && !User.IsInRole(AppRoles.Admin))
            return Forbid();

        if (classId.HasValue)
        {
            await _supply.SeedOrdersForClassAsync(classId.Value);
        }
        else
        {
            // Seed for all classes
            var allClasses = await _db.Classes.IgnoreQueryFilters().Select(c => c.Id).ToListAsync();
            foreach (var cid in allClasses)
                await _supply.SeedOrdersForClassAsync(cid);
        }

        LogisticsOrderStatus? status = null;
        if (!string.IsNullOrEmpty(StatusFilter) && Enum.TryParse<LogisticsOrderStatus>(StatusFilter, out var parsed))
            status = parsed;

        var orders = await _supply.ListOrdersAsync(status, ClassIdFilter);
        return Partial("_OrdersBody", orders);
    }

    private async Task LoadAsync()
    {
        LogisticsOrderStatus? status = null;
        if (!string.IsNullOrEmpty(StatusFilter) && Enum.TryParse<LogisticsOrderStatus>(StatusFilter, out var parsed))
            status = parsed;

        Orders = await _supply.ListOrdersAsync(status, ClassIdFilter);
        Classes = await _db.Classes.IgnoreQueryFilters().OrderBy(c => c.Name).ToListAsync();
    }
}
