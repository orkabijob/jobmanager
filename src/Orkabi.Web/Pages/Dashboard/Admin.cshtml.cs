using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Dashboard;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Dashboard;

[Authorize(Roles = AppRoles.Admin)]
public class AdminModel : PageModel
{
    private readonly DashboardMetricsService _metrics;
    private readonly UserManager<AppUser> _users;

    public AdminModel(DashboardMetricsService metrics, UserManager<AppUser> users)
    {
        _metrics = metrics;
        _users = users;
    }

    // ── Metrics loaded in OnGetAsync ──────────────────────────────────────────

    /// <summary>Resolved greeting name from UserManager.</summary>
    public string Greeting { get; private set; } = "";

    /// <summary>Active clients (IsActive == true).</summary>
    public int ActiveClients { get; private set; }

    /// <summary>Active clients created since first of current month (Israel time).</summary>
    public int ClientsDeltaThisMonth { get; private set; }

    /// <summary>ShiftInstances with Date == today (Israel time).</summary>
    public int SessionsToday { get; private set; }

    /// <summary>VacationRequests with Status == Pending.</summary>
    public int PendingVacations { get; private set; }

    /// <summary>ExtraHours records with Status == Pending.</summary>
    public int PendingExtraHours { get; private set; }

    /// <summary>LogisticsOrders with Status == Pending or Disputed (not yet packed).</summary>
    public int OpenOrders { get; private set; }

    /// <summary>LogisticsOrders with Status == Disputed.</summary>
    public int DisputedOrders { get; private set; }

    /// <summary>Classes with Status == Active (global query filter).</summary>
    public int ActiveClasses { get; private set; }

    /// <summary>Open + Escalated incident reports — passive incident signal (R8).</summary>
    public int OpenIncidents { get; private set; }

    /// <summary>Top-5 Open Admin-role items (oldest first) — focal tile hub preview.</summary>
    public IReadOnlyList<ActionItem> HubPreview { get; private set; } = [];

    /// <summary>Total Open Admin-role item count — drives "עוד N משימות" overflow link.</summary>
    public int OpenAdminCount { get; private set; }

    /// <summary>Top-5 Open items across all roles/users, newest first — alerts feed tile.</summary>
    public IReadOnlyList<ActionItem> RecentOpenItems { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var me = await _users.FindByIdAsync(userId.ToString());
        Greeting = !string.IsNullOrWhiteSpace(me?.FullName) ? me!.FullName! : "מנהל";

        var m = await _metrics.GetAdminMetricsAsync();

        ActiveClients = m.ActiveClientsCount;
        ClientsDeltaThisMonth = m.NewClientsThisMonth;
        SessionsToday = m.SessionsToday;
        PendingVacations = m.PendingVacations;
        PendingExtraHours = m.PendingExtraHours;
        OpenOrders = m.OpenLogisticsOrders;
        DisputedOrders = m.OpenDisputedOrders;
        ActiveClasses = m.ActiveClassesCount;
        OpenIncidents = m.OpenIncidents;
        OpenAdminCount = m.OpenCount;
        HubPreview = m.HubPreview;
        RecentOpenItems = m.RecentOpenItems;
    }

    // ── Type → Hebrew / CSS helpers (same enum as Action Hub page) ───────────

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

    /// <summary>Feed dot CSS modifier for the alerts feed tile.</summary>
    public static string TypeToFeedDot(ActionItemType t) => t switch
    {
        ActionItemType.Dispute or ActionItemType.Absence => "feed-dot--alert",
        ActionItemType.Gap or ActionItemType.TryoutFollowup => "feed-dot--warn",
        ActionItemType.Birthday => "feed-dot--ok",
        _ => ""
    };
}
