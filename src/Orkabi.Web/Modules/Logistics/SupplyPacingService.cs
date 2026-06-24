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
            var hasLessonLog = await _db.LessonLogs
                .AnyAsync(l => l.ModelId == sm.ModelId
                             && l.ShiftInstance.Template.ClassId == classId, ct);

            if (!hasLessonLog)
                continue;

            // Check if a non-Disputed order already exists for (classId, modelId).
            var existingOrder = await _db.LogisticsOrders
                .AnyAsync(o => o.ClassId == classId
                             && o.ModelId == sm.ModelId
                             && o.Status != LogisticsOrderStatus.Disputed, ct);

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
    /// Lists orders, optionally filtered by status and/or classId, with Class and Model included.
    /// </summary>
    public Task<List<LogisticsOrder>> ListOrdersAsync(LogisticsOrderStatus? status, int? classId, CancellationToken ct = default) =>
        _db.LogisticsOrders
            .Where(o => (status == null || o.Status == status)
                     && (classId == null || o.ClassId == classId))
            .Include(o => o.Class)
            .Include(o => o.Model)
            .OrderBy(o => o.Status)
            .ThenBy(o => o.ClassId)
            .ToListAsync(ct);
}
