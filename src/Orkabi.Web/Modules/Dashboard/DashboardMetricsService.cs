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

    public async Task<CsMetrics> GetCsMetricsAsync()
    {
        // Israel time for month boundary
        var nowUtc = DateTime.UtcNow;
        var nowIsrael = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, IsraelClock.IsraelTz);
        var firstOfMonthIsrael = new DateTime(nowIsrael.Year, nowIsrael.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var firstOfMonthUtc = TimeZoneInfo.ConvertTimeToUtc(firstOfMonthIsrael, IsraelClock.IsraelTz);

        var csTicketsTask = _actionItemService.ListOpenForRoleAsync(AppRoles.CustomerService);
        var activeClientsTask = _db.Clients.CountAsync(c => c.IsActive);
        var newClientsTask = _db.Clients.CountAsync(c => c.IsActive && c.CreatedAt >= firstOfMonthUtc);
        var tryoutsThisMonthTask = _db.Enrollments.CountAsync(e =>
            e.Status == EnrollmentStatus.Tryout && e.EnrolledAt >= firstOfMonthUtc);

        await Task.WhenAll(csTicketsTask, activeClientsTask, newClientsTask, tryoutsThisMonthTask);

        return new CsMetrics
        {
            CsTickets = await csTicketsTask,
            ActiveClientsCount = await activeClientsTask,
            NewClientsThisMonth = await newClientsTask,
            TryoutsThisMonth = await tryoutsThisMonthTask,
        };
    }

    public async Task<LogisticsMetrics> GetLogisticsMetricsAsync()
    {
        var logisticsTicketsTask = _actionItemService.ListOpenForRoleAsync(AppRoles.Logistics);
        var ordersByStatusTask = _db.LogisticsOrders
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        var pendingOrdersTask = _db.LogisticsOrders.CountAsync(o => o.Status == LogisticsOrderStatus.Pending);

        await Task.WhenAll(logisticsTicketsTask, ordersByStatusTask, pendingOrdersTask);

        var byStatus = (await ordersByStatusTask).ToDictionary(x => x.Status, x => x.Count);

        return new LogisticsMetrics
        {
            LogisticsTickets = await logisticsTicketsTask,
            PendingOrders = await pendingOrdersTask,
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
