using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;

namespace Orkabi.Web.Pages.Logistics.PackingList;

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
    public string GroupBy { get; set; } = "model";

    public List<PackGroup> Groups { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var orders = await _supply.GetPackingListAsync();
        Groups = BuildGroups(orders, GroupBy);
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

        // Save-success toast: HTMX reads this header and dispatches a `showToast` event.
        Response.Headers["HX-Trigger"] = "{\"showToast\":{\"msg\":\"ההזמנה סומנה כנארזה\"}}";

        return Partial("_PackRow", order);
    }

    private static List<PackGroup> BuildGroups(List<LogisticsOrder> orders, string groupBy)
    {
        if (groupBy == "class")
        {
            return orders
                .GroupBy(o => new { o.ClassId, ClassName = o.Class?.Name ?? "" })
                .OrderBy(g => g.Key.ClassName)
                .Select(g => new PackGroup
                {
                    Name = g.Key.ClassName,
                    TotalQuantity = g.Sum(o => o.Quantity),
                    Lines = g.Select(o => new PackLine
                    {
                        OrderId = o.Id,
                        Label = o.Model?.Name ?? "",
                        Quantity = o.Quantity,
                        Status = o.Status
                    }).ToList()
                })
                .ToList();
        }
        else
        {
            // group by model (default)
            return orders
                .GroupBy(o => new { o.ModelId, ModelName = o.Model?.Name ?? "" })
                .OrderBy(g => g.Key.ModelName)
                .Select(g => new PackGroup
                {
                    Name = g.Key.ModelName,
                    TotalQuantity = g.Sum(o => o.Quantity),
                    Lines = g.Select(o => new PackLine
                    {
                        OrderId = o.Id,
                        Label = o.Class?.Name ?? "",
                        Quantity = o.Quantity,
                        Status = o.Status
                    }).ToList()
                })
                .ToList();
        }
    }
}

public sealed class PackGroup
{
    public string Name { get; init; } = "";
    public int TotalQuantity { get; init; }
    public List<PackLine> Lines { get; init; } = new();
}

public sealed class PackLine
{
    public int OrderId { get; init; }
    public string Label { get; init; } = "";
    public int Quantity { get; init; }
    public LogisticsOrderStatus Status { get; init; }
}
