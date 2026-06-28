using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Dashboard;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Dashboard;

[Authorize(Roles = AppRoles.LogisticsOrAdmin)]
public class LogisticsModel : PageModel
{
    private readonly DashboardMetricsService _metrics;
    private readonly UserManager<AppUser> _users;

    public LogisticsModel(DashboardMetricsService metrics, UserManager<AppUser> users)
    {
        _metrics = metrics;
        _users = users;
    }

    public string Greeting { get; private set; } = "";
    public IReadOnlyList<ActionItem> LogisticsTickets { get; private set; } = [];
    public int PendingOrders { get; private set; }
    public int PackedOrders { get; private set; }
    public int DisputedOrders { get; private set; }

    public async Task OnGetAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var me = await _users.FindByIdAsync(userId.ToString());
        Greeting = !string.IsNullOrWhiteSpace(me?.FullName) ? me!.FullName! : "לוגיסטיקה";

        var m = await _metrics.GetLogisticsMetricsAsync();
        LogisticsTickets = m.LogisticsTickets;
        PendingOrders = m.PendingOrders;
        PackedOrders = m.PackedOrders;
        DisputedOrders = m.DisputedOrders;
    }

    // ── Type → Hebrew / CSS helpers (same enum as AdminModel) ─────────────────

    public static string TypeToHebrew(ActionItemType t) => t switch
    {
        ActionItemType.Gap => "חריגת קצב",
        ActionItemType.Absence => "היעדרות",
        ActionItemType.Dispute => "מחלוקת",
        ActionItemType.Task => "משימה",
        ActionItemType.Birthday => "יום הולדת",
        ActionItemType.TryoutFollowup => "מעקב ניסיון",
        _ => t.ToString()
    };

    public static string TypeToCssModifier(ActionItemType t) => t switch
    {
        ActionItemType.Gap => "action-type--gap",
        ActionItemType.Absence => "action-type--absence",
        ActionItemType.Dispute => "action-type--dispute",
        ActionItemType.Task => "action-type",
        ActionItemType.Birthday => "action-type--birthday",
        ActionItemType.TryoutFollowup => "action-type--tryout",
        _ => "action-type"
    };
}
