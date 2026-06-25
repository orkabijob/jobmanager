using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Dashboard;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Dashboard;

[Authorize(Roles = AppRoles.CustomerService)]
public class CsModel : PageModel
{
    private readonly DashboardMetricsService _metrics;
    private readonly UserManager<AppUser> _users;

    public CsModel(DashboardMetricsService metrics, UserManager<AppUser> users)
    {
        _metrics = metrics;
        _users = users;
    }

    public string Greeting { get; private set; } = "";
    public IReadOnlyList<ActionItem> CsTickets { get; private set; } = [];
    public int ActiveClients { get; private set; }
    public int NewClientsThisMonth { get; private set; }
    public int TryoutsThisMonth { get; private set; }

    public async Task OnGetAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var me = await _users.FindByIdAsync(userId.ToString());
        Greeting = !string.IsNullOrWhiteSpace(me?.FullName) ? me!.FullName! : "שירות לקוחות";

        var m = await _metrics.GetCsMetricsAsync();
        CsTickets = m.CsTickets;
        ActiveClients = m.ActiveClientsCount;
        NewClientsThisMonth = m.NewClientsThisMonth;
        TryoutsThisMonth = m.TryoutsThisMonth;
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
