using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;
using Orkabi.Web.Modules.Operations;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Modules.Dashboard;

/// <summary>
/// Cross-cutting read service for the Admin bento dashboard.
/// Uses AppDbContext directly (read-only aggregate queries; no N+1).
/// Classes DbSet already has a global query filter (Status == Active), so
/// CountAsync() on _db.Classes gives active-only without IgnoreQueryFilters.
/// </summary>
public class DashboardMetricsService
{
    private readonly AppDbContext _db;
    private readonly ActionItemService _actionItemService;

    public DashboardMetricsService(AppDbContext db, ActionItemService actionItemService)
    {
        _db = db;
        _actionItemService = actionItemService;
    }

    public async Task<AdminMetrics> GetAdminMetricsAsync()
    {
        // Israel today and first-of-month boundaries
        var nowUtc = DateTime.UtcNow;
        var nowIsrael = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, IsraelClock.IsraelTz);
        var todayIsrael = DateOnly.FromDateTime(nowIsrael);

        // First of month in Israel time → convert to UTC for CreatedAt comparison
        var firstOfMonthIsrael = new DateTime(nowIsrael.Year, nowIsrael.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var firstOfMonthUtc = TimeZoneInfo.ConvertTimeToUtc(firstOfMonthIsrael, IsraelClock.IsraelTz);

        // Sequential awaits — a single AppDbContext does NOT support concurrent operations.
        // EF Core throws InvalidOperationException ("a second operation was started on this
        // context...") when ops overlap; Task.WhenAll over one context is a race that SQLite
        // (in-process, fast) tolerates in tests but Npgsql/Neon (network latency) does not.
        // These are cheap COUNT/aggregate reads — sequential is correct and adequate.
        var activeClients = await _db.Clients.CountAsync(c => c.IsActive);
        var newClients = await _db.Clients.CountAsync(c => c.IsActive && c.CreatedAt >= firstOfMonthUtc);
        var sessionsToday = await _db.ShiftInstances.CountAsync(i => i.Date == todayIsrael);
        var pendingVacations = await _db.VacationRequests.CountAsync(v => v.Status == VacationStatus.Pending);
        var pendingExtraHours = await _db.ExtraHours.CountAsync(e => e.Status == ExtraHoursStatus.Pending);
        var disputedOrders = await _db.LogisticsOrders.CountAsync(o => o.Status == LogisticsOrderStatus.Disputed);
        var openOrders = await _db.LogisticsOrders.CountAsync(o =>
            o.Status == LogisticsOrderStatus.Pending || o.Status == LogisticsOrderStatus.Disputed);
        // Class global query filter = Active only → direct CountAsync = active classes
        var activeClasses = await _db.Classes.CountAsync();

        // GroupBy open action items by type
        var byTypeList = await _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open)
            .GroupBy(a => a.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();
        var byTypeDict = byTypeList.ToDictionary(x => x.Type, x => x.Count);

        // Top-5 Open items for Admin role (focal tile = the Admin's OWN assigned queue).
        // Intentionally role-scoped: e.g. dispute tickets are Logistics-assigned (F4), so they are
        // owned/resolved by Logistics and do NOT belong in the Admin's personal focal queue. The Admin
        // still sees them via OpenActionItemsByType (role-agnostic count), the RecentOpenItems alerts
        // feed below (all roles), the OpenDisputedOrders metric, and the full /Operations/ActionItems hub.
        var hubPreview = await _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open && a.AssignedToRole == AppRoles.Admin)
            .OrderBy(a => a.CreatedAt)
            .Take(5)
            .ToListAsync();

        // Total open Admin-role items count (for "עוד N משימות")
        var openCount = await _db.ActionItems.CountAsync(a =>
            a.Status == ActionItemStatus.Open && a.AssignedToRole == AppRoles.Admin);

        // Top-5 recent open items across ALL roles/users (alerts feed tile)
        var recentOpen = await _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open)
            .OrderByDescending(a => a.CreatedAt)
            .Take(5)
            .ToListAsync();

        return new AdminMetrics
        {
            ActiveClientsCount = activeClients,
            NewClientsThisMonth = newClients,
            SessionsToday = sessionsToday,
            PendingVacations = pendingVacations,
            PendingExtraHours = pendingExtraHours,
            OpenDisputedOrders = disputedOrders,
            OpenLogisticsOrders = openOrders,
            ActiveClassesCount = activeClasses,
            OpenActionItemsByType = byTypeDict,
            HubPreview = hubPreview,
            OpenCount = openCount,
            RecentOpenItems = recentOpen,
        };
    }

    public async Task<CsMetrics> GetCsMetricsAsync()
    {
        // Israel time for month boundary
        var nowUtc = DateTime.UtcNow;
        var nowIsrael = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, IsraelClock.IsraelTz);
        var firstOfMonthIsrael = new DateTime(nowIsrael.Year, nowIsrael.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var firstOfMonthUtc = TimeZoneInfo.ConvertTimeToUtc(firstOfMonthIsrael, IsraelClock.IsraelTz);

        // Sequential awaits — single AppDbContext; ListOpenForRoleAsync shares it (no concurrent ops).
        var csTickets = await _actionItemService.ListOpenForRoleAsync(AppRoles.CustomerService);
        var activeClients = await _db.Clients.CountAsync(c => c.IsActive);
        var newClients = await _db.Clients.CountAsync(c => c.IsActive && c.CreatedAt >= firstOfMonthUtc);
        var tryoutsThisMonth = await _db.Enrollments.CountAsync(e =>
            e.Status == EnrollmentStatus.Tryout && e.EnrolledAt >= firstOfMonthUtc);

        return new CsMetrics
        {
            CsTickets = csTickets,
            ActiveClientsCount = activeClients,
            NewClientsThisMonth = newClients,
            TryoutsThisMonth = tryoutsThisMonth,
        };
    }

    public async Task<LogisticsMetrics> GetLogisticsMetricsAsync()
    {
        // Sequential awaits — single AppDbContext; ListOpenForRoleAsync shares it (no concurrent ops).
        var logisticsTickets = await _actionItemService.ListOpenForRoleAsync(AppRoles.Logistics);
        var ordersByStatusList = await _db.LogisticsOrders
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        var pendingOrders = await _db.LogisticsOrders.CountAsync(o => o.Status == LogisticsOrderStatus.Pending);

        var byStatus = ordersByStatusList.ToDictionary(x => x.Status, x => x.Count);

        return new LogisticsMetrics
        {
            LogisticsTickets = logisticsTickets,
            PendingOrders = pendingOrders,
            PackedOrders = byStatus.TryGetValue(LogisticsOrderStatus.Packed, out var p) ? p : 0,
            DisputedOrders = byStatus.TryGetValue(LogisticsOrderStatus.Disputed, out var d) ? d : 0,
            OrdersByStatus = byStatus,
        };
    }
}

/// <summary>
/// DTO returned by GetAdminMetricsAsync. All fields are real data — no hardcoded values.
/// </summary>
public sealed record AdminMetrics
{
    /// <summary>Clients with IsActive == true.</summary>
    public int ActiveClientsCount { get; init; }

    /// <summary>Active clients created since the first of the current month (Israel time → UTC boundary).</summary>
    public int NewClientsThisMonth { get; init; }

    /// <summary>ShiftInstances whose Date == today in Israel timezone.</summary>
    public int SessionsToday { get; init; }

    /// <summary>VacationRequests with Status == Pending.</summary>
    public int PendingVacations { get; init; }

    /// <summary>ExtraHours records with Status == Pending.</summary>
    public int PendingExtraHours { get; init; }

    /// <summary>LogisticsOrders with Status == Disputed.</summary>
    public int OpenDisputedOrders { get; init; }

    /// <summary>LogisticsOrders with Status == Pending or Disputed (not yet packed/closed).</summary>
    public int OpenLogisticsOrders { get; init; }

    /// <summary>Classes with Status == Active (via global query filter).</summary>
    public int ActiveClassesCount { get; init; }

    /// <summary>Open ActionItems grouped by Type → count.</summary>
    public Dictionary<ActionItemType, int> OpenActionItemsByType { get; init; } = new();

    /// <summary>Top 5 Open Admin-role items (oldest first) — for the focal tile hub preview.</summary>
    public List<ActionItem> HubPreview { get; init; } = new();

    /// <summary>Total Open Admin-role item count — drives "עוד N משימות" overflow link.</summary>
    public int OpenCount { get; init; }

    /// <summary>Top 5 Open items across all roles/users, newest first — for the alerts feed tile.</summary>
    public List<ActionItem> RecentOpenItems { get; init; } = new();
}

/// <summary>
/// DTO returned by GetCsMetricsAsync. Real data from ActionItems + Clients + Enrollments.
/// </summary>
public sealed record CsMetrics
{
    /// <summary>All Open ActionItems assigned to CustomerService role.</summary>
    public List<ActionItem> CsTickets { get; init; } = new();

    /// <summary>Clients with IsActive == true.</summary>
    public int ActiveClientsCount { get; init; }

    /// <summary>Active clients created since first of the current month (Israel time → UTC boundary).</summary>
    public int NewClientsThisMonth { get; init; }

    /// <summary>Enrollments with Status == Tryout whose EnrolledAt is >= first of the current month.</summary>
    public int TryoutsThisMonth { get; init; }
}

/// <summary>
/// DTO returned by GetLogisticsMetricsAsync. Real data from ActionItems + LogisticsOrders.
/// </summary>
public sealed record LogisticsMetrics
{
    /// <summary>All Open ActionItems assigned to Logistics role.</summary>
    public List<ActionItem> LogisticsTickets { get; init; } = new();

    /// <summary>LogisticsOrders with Status == Pending.</summary>
    public int PendingOrders { get; init; }

    /// <summary>LogisticsOrders with Status == Packed.</summary>
    public int PackedOrders { get; init; }

    /// <summary>LogisticsOrders with Status == Disputed.</summary>
    public int DisputedOrders { get; init; }

    /// <summary>All orders grouped by status → count (for future extensibility).</summary>
    public Dictionary<LogisticsOrderStatus, int> OrdersByStatus { get; init; } = new();
}
