using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;
using Orkabi.Web.Modules.Operations;
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

        // Run all COUNT queries concurrently (independent reads)
        var activeClientsTask = _db.Clients.CountAsync(c => c.IsActive);
        var newClientsTask = _db.Clients.CountAsync(c => c.IsActive && c.CreatedAt >= firstOfMonthUtc);
        var sessionsTodayTask = _db.ShiftInstances.CountAsync(i => i.Date == todayIsrael);
        var pendingVacationsTask = _db.VacationRequests.CountAsync(v => v.Status == VacationStatus.Pending);
        var pendingExtraHoursTask = _db.ExtraHours.CountAsync(e => e.Status == ExtraHoursStatus.Pending);
        var disputedOrdersTask = _db.LogisticsOrders.CountAsync(o => o.Status == LogisticsOrderStatus.Disputed);
        var openOrdersTask = _db.LogisticsOrders.CountAsync(o =>
            o.Status == LogisticsOrderStatus.Pending || o.Status == LogisticsOrderStatus.Disputed);
        // Class global query filter = Active only → direct CountAsync = active classes
        var activeClassesTask = _db.Classes.CountAsync();

        // GroupBy open action items by type
        var byTypeTask = _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open)
            .GroupBy(a => a.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        // Top-5 Open items for Admin role (focal tile)
        var hubPreviewTask = _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open && a.AssignedToRole == AppRoles.Admin)
            .OrderBy(a => a.CreatedAt)
            .Take(5)
            .ToListAsync();

        // Total open Admin-role items count (for "עוד N משימות")
        var openCountTask = _db.ActionItems.CountAsync(a =>
            a.Status == ActionItemStatus.Open && a.AssignedToRole == AppRoles.Admin);

        // Top-5 recent open items across ALL roles/users (alerts feed tile)
        var recentOpenTask = _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open)
            .OrderByDescending(a => a.CreatedAt)
            .Take(5)
            .ToListAsync();

        await Task.WhenAll(
            activeClientsTask, newClientsTask, sessionsTodayTask,
            pendingVacationsTask, pendingExtraHoursTask, disputedOrdersTask,
            openOrdersTask, activeClassesTask, byTypeTask, hubPreviewTask, openCountTask, recentOpenTask);

        var byTypeDict = (await byTypeTask).ToDictionary(x => x.Type, x => x.Count);

        return new AdminMetrics
        {
            ActiveClientsCount = await activeClientsTask,
            NewClientsThisMonth = await newClientsTask,
            SessionsToday = await sessionsTodayTask,
            PendingVacations = await pendingVacationsTask,
            PendingExtraHours = await pendingExtraHoursTask,
            OpenDisputedOrders = await disputedOrdersTask,
            OpenLogisticsOrders = await openOrdersTask,
            ActiveClassesCount = await activeClassesTask,
            OpenActionItemsByType = byTypeDict,
            HubPreview = await hubPreviewTask,
            OpenCount = await openCountTask,
            RecentOpenItems = await recentOpenTask,
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
