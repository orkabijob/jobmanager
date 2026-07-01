using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;

namespace Orkabi.Web.Modules.Logistics;

public class SupplyPacingService
{
    private readonly AppDbContext _db;
    private readonly ActionItemService _actionHub;

    public SupplyPacingService(AppDbContext db, ActionItemService actionHub)
    {
        _db = db;
        _actionHub = actionHub;
    }

    /// <summary>
    /// For each model in the class's syllabus where a LessonLog exists for (classId, modelId)
    /// AND no non-Disputed LogisticsOrder exists for (classId, modelId), creates a Pending order.
    /// Idempotent: a second call creates no duplicates.
    /// </summary>
    public async Task<List<LogisticsOrder>> SeedOrdersForClassAsync(int classId, CancellationToken ct = default)
    {
        // Load the class (IgnoreQueryFilters so archived classes are still reachable).
        var cls = await _db.Classes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == classId, ct);

        if (cls?.SyllabusId is null)
            return new List<LogisticsOrder>();

        // Load the syllabus with its ordered models (IgnoreQueryFilters for archived syllabi).
        var syllabus = await _db.Syllabi
            .IgnoreQueryFilters()
            .Include(s => s.SyllabusModels.OrderBy(sm => sm.OrderIndex))
            .FirstOrDefaultAsync(s => s.Id == cls.SyllabusId, ct);

        if (syllabus is null || syllabus.SyllabusModels.Count == 0)
            return new List<LogisticsOrder>();

        var created = new List<LogisticsOrder>();

        foreach (var sm in syllabus.SyllabusModels)
        {
            // Check if a LessonLog exists for (classId, modelId).
            // LessonLog → ShiftInstance → Template → ClassId.
            // IgnoreQueryFilters: ShiftTemplate has a global filter (Status == Active);
            // without it, a LessonLog taught under a since-archived template is silently excluded.
            var hasLessonLog = await _db.LessonLogs
                .IgnoreQueryFilters()
                .AnyAsync(l => l.ModelId == sm.ModelId
                             && l.ShiftInstance.Template.ClassId == classId, ct);

            if (!hasLessonLog)
                continue;

            // R16: any existing order for (classId, modelId) blocks re-seeding — including a Disputed
            // one. A live dispute is re-queued via RepackDisputedAsync (Disputed→Pending), NOT by
            // "Generate" forking a second Pending order for the same class+model.
            var existingOrder = await _db.LogisticsOrders
                .AnyAsync(o => o.ClassId == classId && o.ModelId == sm.ModelId, ct);

            if (existingOrder)
                continue;

            var order = new LogisticsOrder
            {
                ClassId = classId,
                ModelId = sm.ModelId,
                Quantity = 1,
                Status = LogisticsOrderStatus.Pending
            };
            _db.LogisticsOrders.Add(order);
            created.Add(order);
        }

        if (created.Count > 0)
            await _db.SaveChangesAsync(ct);

        return created;
    }

    /// <summary>
    /// Marks the order as Packed. Guards that current status is Pending.
    /// </summary>
    public async Task MarkPackedAsync(int orderId, int logisticsUserId, CancellationToken ct = default)
    {
        var order = await _db.LogisticsOrders.FindAsync(new object[] { orderId }, ct)
            ?? throw new InvalidOperationException($"הזמנה {orderId} לא נמצאה");

        if (order.Status != LogisticsOrderStatus.Pending)
            throw new InvalidOperationException($"הזמנה {orderId} אינה במצב ממתין — לא ניתן לסמן כארוזה");

        order.Status = LogisticsOrderStatus.Packed;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Marks the order as Accepted and sets DeliveredAt. Guards that current status is Packed.
    /// </summary>
    public async Task MarkAcceptedAsync(int orderId, int instructorUserId, CancellationToken ct = default)
    {
        var order = await _db.LogisticsOrders.FindAsync(new object[] { orderId }, ct)
            ?? throw new InvalidOperationException($"הזמנה {orderId} לא נמצאה");

        if (order.Status != LogisticsOrderStatus.Packed)
            throw new InvalidOperationException($"הזמנה {orderId} אינה במצב ארוז — לא ניתן לאשר");

        order.Status = LogisticsOrderStatus.Accepted;
        order.DeliveredAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Marks the order as Disputed with notes. Guards that current status is Packed.
    /// In a transaction: updates status, saves, then ensures a dispute ActionItem, then commits.
    /// </summary>
    public async Task MarkDisputedAsync(int orderId, int instructorUserId, string disputeNotes, CancellationToken ct = default)
    {
        var order = await _db.LogisticsOrders.FindAsync(new object[] { orderId }, ct)
            ?? throw new InvalidOperationException($"הזמנה {orderId} לא נמצאה");

        if (order.Status != LogisticsOrderStatus.Packed)
            throw new InvalidOperationException($"הזמנה {orderId} אינה במצב ארוז — לא ניתן לסמן כמחלוקת");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        order.Status = LogisticsOrderStatus.Disputed;
        order.DisputeNotes = disputeNotes;
        await _db.SaveChangesAsync(ct);

        await _actionHub.EnsureDisputeActionItemAsync(orderId, order.ClassId);

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Re-pack a disputed order: transitions Disputed → Pending (back into the packing queue) and
    /// resolves the open dispute ActionItem, closing the loop. Guards that current status is Disputed.
    /// In a transaction: clears the dispute, saves, resolves the ticket (frees its dedup slot), commits.
    /// </summary>
    public async Task RepackDisputedAsync(int orderId, int logisticsUserId, CancellationToken ct = default)
    {
        var order = await _db.LogisticsOrders.FindAsync(new object[] { orderId }, ct)
            ?? throw new InvalidOperationException($"הזמנה {orderId} לא נמצאה");

        if (order.Status != LogisticsOrderStatus.Disputed)
            throw new InvalidOperationException($"הזמנה {orderId} אינה במחלוקת — לא ניתן להחזיר לאריזה");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        order.Status = LogisticsOrderStatus.Pending;
        order.DisputeNotes = null;
        await _db.SaveChangesAsync(ct);

        // Close the dispute ticket so the Logistics hub queue clears; nulling its dedup key lets a
        // future dispute on the same order re-create a fresh ticket.
        var ticket = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == $"dispute_{orderId}"
                                   && a.Status == ActionItemStatus.Open, ct);
        if (ticket is not null)
            await _actionHub.ResolveActionItemAsync(ticket.Id, logisticsUserId);

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Returns all Pending and Packed orders for the master packing list, grouped by
    /// School → Class → Status. Includes Class → School and Model navigations.
    /// IgnoreQueryFilters: Class has a global filter (Status == Active); archived classes
    /// must still be reachable so historic Pending/Packed orders are not silently dropped.
    /// </summary>
    public Task<List<LogisticsOrder>> GetPackingListAsync(CancellationToken ct = default) =>
        _db.LogisticsOrders
            .IgnoreQueryFilters()
            .Where(o => o.Status == LogisticsOrderStatus.Pending || o.Status == LogisticsOrderStatus.Packed)
            .Include(o => o.Class).ThenInclude(c => c.School)
            .Include(o => o.Model)
            .OrderBy(o => o.Class.School.Name)
            .ThenBy(o => o.Class.Name)
            .ThenBy(o => o.Status)
            .ToListAsync(ct);

    /// <summary>
    /// Lists orders, optionally filtered by status and/or classId, with Class and Model included.
    /// IgnoreQueryFilters: Class has a global filter (Status == Active); without it, the Class
    /// navigation property is silently nulled for orders whose class has been archived.
    /// </summary>
    public Task<List<LogisticsOrder>> ListOrdersAsync(LogisticsOrderStatus? status, int? classId, CancellationToken ct = default) =>
        _db.LogisticsOrders
            .IgnoreQueryFilters()
            .Where(o => (status == null || o.Status == status)
                     && (classId == null || o.ClassId == classId))
            .Include(o => o.Class)
            .Include(o => o.Model)
            .OrderBy(o => o.Status)
            .ThenBy(o => o.ClassId)
            .ToListAsync(ct);
}
