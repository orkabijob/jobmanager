using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;

namespace Orkabi.Web.Pages.Logistics.MyOrders;

[Authorize(Roles = AppRoles.InstructorOrAdmin)]
public class IndexModel : PageModel
{
    private readonly SupplyPacingService _supply;
    private readonly AppDbContext _db;

    public IndexModel(SupplyPacingService supply, AppDbContext db)
    {
        _supply = supply;
        _db = db;
    }

    public List<LogisticsOrder> Orders { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnGetCardAsync(int id)
    {
        var order = await LoadOrderAsync(id);
        if (order is null) return NotFound();
        return Partial("_OrderCard", order);
    }

    public async Task<IActionResult> OnGetDisputeFormAsync(int id)
    {
        var order = await LoadOrderAsync(id);
        if (order is null) return NotFound();
        if (order.Status != LogisticsOrderStatus.Packed) return BadRequest();
        return Partial("_OrderDisputeForm", order);
    }

    public async Task<IActionResult> OnPostAcceptAsync(int id)
    {
        if (!User.IsInRole(AppRoles.Instructor) && !User.IsInRole(AppRoles.Admin))
            return Forbid();

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (!User.IsInRole(AppRoles.Admin))
        {
            var classIds = await GetInstructorClassIdsAsync(userId);
            var order = await LoadOrderAsync(id);
            if (order is null || !classIds.Contains(order.ClassId)) return Forbid();
        }

        await _supply.MarkAcceptedAsync(id, userId);

        var updatedOrder = await LoadOrderAsync(id);
        if (updatedOrder is null) return NotFound();
        return Partial("_OrderCard", updatedOrder);
    }

    public async Task<IActionResult> OnPostDisputeAsync(int id, string? disputeNotes)
    {
        if (!User.IsInRole(AppRoles.Instructor) && !User.IsInRole(AppRoles.Admin))
            return Forbid();

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (!User.IsInRole(AppRoles.Admin))
        {
            var classIds = await GetInstructorClassIdsAsync(userId);
            var order = await LoadOrderAsync(id);
            if (order is null || !classIds.Contains(order.ClassId)) return Forbid();
        }

        if (string.IsNullOrWhiteSpace(disputeNotes))
        {
            ModelState.AddModelError("DisputeNotes", "יש לפרט את הבעיה");
            var orderForError = await LoadOrderAsync(id);
            if (orderForError is null) return NotFound();
            return Partial("_OrderDisputeForm", orderForError);
        }

        await _supply.MarkDisputedAsync(id, userId, disputeNotes);

        var updatedOrder = await LoadOrderAsync(id);
        if (updatedOrder is null) return NotFound();
        return Partial("_OrderCard", updatedOrder);
    }

    // The classes whose kit this user may see/accept/dispute: those they teach as the default template
    // instructor, PLUS (R10) those where they are the ACTUAL instructor on a shift instance — an approved
    // substitute is set as ActualInstructorId, never the template default, so without this they'd see no
    // orders and Accept/Dispute would Forbid() for the person actually teaching.
    private async Task<List<int>> GetInstructorClassIdsAsync(int userId)
    {
        var templateClassIds = await _db.ShiftTemplates
            .IgnoreQueryFilters()
            .Where(t => t.DefaultInstructorId == userId && t.Status == Orkabi.Web.Shared.EntityStatus.Active)
            .Select(t => t.ClassId)
            .ToListAsync();

        var actualInstructorClassIds = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Where(i => i.ActualInstructorId == userId)
            .Select(i => i.Template.ClassId)
            .ToListAsync();

        return templateClassIds.Concat(actualInstructorClassIds).Distinct().ToList();
    }

    private async Task LoadAsync()
    {
        var isAdmin = User.IsInRole(AppRoles.Admin);
        if (isAdmin)
        {
            Orders = await _supply.ListOrdersAsync(null, null);
        }
        else
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            // Classes the user teaches — as template default OR actual (substitute) instructor (R10).
            var classIds = await GetInstructorClassIdsAsync(userId);

            Orders = await _db.LogisticsOrders
                .IgnoreQueryFilters()
                .Where(o => classIds.Contains(o.ClassId))
                .Include(o => o.Class)
                .Include(o => o.Model)
                .OrderBy(o => o.Status)
                .ThenBy(o => o.ClassId)
                .ToListAsync();
        }
    }

    private Task<LogisticsOrder?> LoadOrderAsync(int id) =>
        _db.LogisticsOrders
            .IgnoreQueryFilters()
            .Include(o => o.Class)
            .Include(o => o.Model)
            .FirstOrDefaultAsync(o => o.Id == id);
}
