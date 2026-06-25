using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Operations.ActionItems;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ActionItemService _svc;

    public IndexModel(ActionItemService svc) => _svc = svc;

    public List<ActionItem> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var (userId, role) = GetUserContext();
        Items = role == AppRoles.Admin
            ? await _svc.ListAllOpenAsync()
            : await _svc.ListOpenForUserAndRoleAsync(userId, role);
    }

    /// <summary>
    /// Polling fragment: returns the _ActionItemList partial with the current open queue.
    /// Called by hx-get="?handler=List" every 25s.
    /// </summary>
    public async Task<IActionResult> OnGetListAsync()
    {
        var (userId, role) = GetUserContext();
        Items = role == AppRoles.Admin
            ? await _svc.ListAllOpenAsync()
            : await _svc.ListOpenForUserAndRoleAsync(userId, role);
        return Partial("_ActionItemList", Items);
    }

    /// <summary>
    /// Resolve handler — authz in handler (Slice-3/4 pattern):
    /// role-assigned: require IsInRole(item.AssignedToRole) OR Admin;
    /// user-assigned: require userId == item.AssignedToUserId OR Admin.
    /// Returns empty content (card vanishes from open-filter view) on success.
    /// </summary>
    public async Task<IActionResult> OnPostResolveAsync(int id)
    {
        var (userId, role) = GetUserContext();
        var isAdmin = role == AppRoles.Admin;

        var item = await _svc.FindOpenAsync(id);
        if (item is null)
            return Content(""); // already resolved or non-existent — idempotent

        // Authorization gate
        bool allowed;
        if (item.AssignedToRole is not null)
            allowed = isAdmin || User.IsInRole(item.AssignedToRole);
        else if (item.AssignedToUserId.HasValue)
            allowed = isAdmin || userId == item.AssignedToUserId.Value;
        else
            allowed = isAdmin; // fallback: unassigned → Admin only

        if (!allowed)
            return Forbid();

        await _svc.ResolveActionItemAsync(id, userId);

        // Save-success toast: HTMX reads this header and dispatches a `showToast` event.
        Response.Headers["HX-Trigger"] = "{\"showToast\":{\"msg\":\"הפריט סומן כטופל\"}}";

        // Return the resolved-state fragment (card replaced with resolved view)
        return Partial("_ResolvedCard", item);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private (int userId, string role) GetUserContext()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
        int.TryParse(userIdStr, out var userId);

        var role = AppRoles.All.FirstOrDefault(r => User.IsInRole(r)) ?? AppRoles.Instructor;
        return (userId, role);
    }
}
