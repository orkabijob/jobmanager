using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;

namespace Orkabi.Web.Modules.ActionHub;

/// <summary>
/// Slice-3 surface: gap-ticket creator and open-items read.
///
/// DEDUP KEY INVARIANT:
///   DeduplicationKey is unique across ALL rows (Open AND Resolved) via a partial DB index.
///   Therefore, when resolving a gap item (Slice 5), the resolver MUST clear
///   DeduplicationKey to null before saving — this frees the slot so a future
///   recurrence can create a new open item for the same class+model pair.
///   The Resolve operation is NOT built in Slice 3.
/// </summary>
public class ActionItemService
{
    private readonly AppDbContext _db;

    public ActionItemService(AppDbContext db) => _db = db;

    /// <summary>
    /// Ensures exactly one Open gap action item exists for the given (classId, modelId) pair.
    /// Idempotent: if an Open item with the computed DeduplicationKey already exists, returns
    /// without creating a duplicate. A concurrent insert racing to the same unique index is
    /// swallowed via DbUpdateException + ChangeTracker.Clear() — the invariant holds.
    /// </summary>
    public async Task EnsureGapActionItemAsync(int classId, int modelId, int expected, int spent)
    {
        var dedupKey = $"gap_{classId}_{modelId}";

        // Guard: an Open item with this key already exists → no-op.
        var existing = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey
                                   && a.Status == ActionItemStatus.Open);
        if (existing is not null)
            return;

        // Load names for the Hebrew description; IgnoreQueryFilters for archived classes.
        var cls = await _db.Classes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == classId);
        var model = await _db.Models.FindAsync(modelId);

        var className = cls?.Name ?? classId.ToString();
        var modelName = model?.Name ?? modelId.ToString();

        var item = new ActionItem
        {
            Type = ActionItemType.Gap,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.Admin,
            AssignedToUserId = null,
            RelatedEntityId = classId,
            DeduplicationKey = dedupKey,
            Description = $"חריגת קצב: כיתה \"{className}\" · דגם \"{modelName}\" — בוצעו {spent} שיעורים מתוך {expected} צפויים."
        };

        _db.ActionItems.Add(item);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Concurrent insert won the unique index race — the invariant already holds.
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Resolves an action item. Idempotent: no-op if item is null or already Resolved.
    /// LYNCHPIN: DeduplicationKey is nulled so the unique-index slot is freed and automation
    /// can create a fresh recurrence for the same entity after resolution.
    /// </summary>
    public async Task<ActionItem?> ResolveActionItemAsync(int actionItemId, int resolvedByUserId)
    {
        var item = await _db.ActionItems.FirstOrDefaultAsync(a => a.Id == actionItemId);
        if (item is null) return null;
        if (item.Status != ActionItemStatus.Resolved)                           // double-resolve no-op
        {
            item.Status = ActionItemStatus.Resolved;
            item.ResolvedByUserId = resolvedByUserId;
            item.ResolvedAt = DateTime.UtcNow;
            item.DeduplicationKey = null;                                        // LYNCHPIN — frees the slot
            await _db.SaveChangesAsync();
        }
        // Load the resolver for the "✓ טופל · ע״י {name}" meta (no-op if no resolver / already loaded).
        await _db.Entry(item).Reference(a => a.ResolvedByUser).LoadAsync();
        return item;
    }

    /// <summary>
    /// Returns all Open action items assigned to the given role, ordered by CreatedAt ascending.
    /// </summary>
    public Task<List<ActionItem>> ListOpenForRoleAsync(string role) =>
        _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open && a.AssignedToRole == role)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

    /// <summary>
    /// Ensures exactly one Open action item for a severe (High) incident report. Dedup key
    /// "severe_incident_{incidentReportId}" — one ticket per incident, assigned to Admin.
    /// Idempotent: existing Open item with the key → no-op; concurrent insert races are absorbed
    /// via DbUpdateException + ChangeTracker.Clear().
    /// </summary>
    public async Task EnsureSevereIncidentActionItemAsync(int incidentReportId)
    {
        var dedupKey = $"severe_incident_{incidentReportId}";

        var existing = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        if (existing is not null)
            return;

        // Load context for the Hebrew description (IgnoreQueryFilters: archived class/template chain).
        var incident = await _db.IncidentReports.IgnoreQueryFilters()
            .Include(r => r.Instructor)
            .Include(r => r.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .FirstOrDefaultAsync(r => r.Id == incidentReportId);

        var instructorName = incident?.Instructor?.FullName ?? incident?.Instructor?.Email ?? "";
        var className = incident?.ShiftInstance?.Template?.Class?.Name ?? "";

        var item = new ActionItem
        {
            Type = ActionItemType.Task,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.Admin,
            AssignedToUserId = null,
            RelatedEntityId = incidentReportId,
            DeduplicationKey = dedupKey,
            Description = $"דיווח אירוע חמור: {instructorName} · כיתה {className} — נדרש טיפול."
        };

        _db.ActionItems.Add(item);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Hub query: a user's open queue = role-assigned to their role OR user-assigned to them.
    /// </summary>
    public Task<List<ActionItem>> ListOpenForUserAndRoleAsync(int userId, string role) =>
        _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open && (a.AssignedToRole == role || a.AssignedToUserId == userId))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

    /// <summary>
    /// Admin "everything open" — returns all Open action items regardless of assignee.
    /// </summary>
    public Task<List<ActionItem>> ListAllOpenAsync() =>
        _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

    /// <summary>
    /// Finds an Open action item by id for the resolve handler authz check.
    /// Returns null if not found or already resolved.
    /// </summary>
    public Task<ActionItem?> FindOpenAsync(int id) =>
        _db.ActionItems.FirstOrDefaultAsync(a => a.Id == id && a.Status == ActionItemStatus.Open);

    /// <summary>
    /// Returns all Open action items user-assigned to the given user (dashboard badge query).
    /// </summary>
    public Task<List<ActionItem>> ListOpenForUserAsync(int userId) =>
        _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open && a.AssignedToUserId == userId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

    /// <summary>
    /// Ensures exactly one Open absence action item exists for a client who missed two consecutive
    /// lessons in the given class. Idempotent: an Open item with key "absence_double_{clientId}_{classId}"
    /// already present → no-op. Concurrent insert races are absorbed via DbUpdateException + ChangeTracker.Clear().
    /// </summary>
    public async Task EnsureDoubleAbsenceActionItemAsync(int clientId, int classId)
    {
        var dedupKey = $"absence_double_{clientId}_{classId}";

        var existing = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        if (existing is not null)
            return;

        var client = await _db.Clients.FindAsync(clientId);
        var cls = await _db.Classes.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == classId);

        var clientName = client?.Name ?? clientId.ToString();
        var className = cls?.Name ?? classId.ToString();

        var item = new ActionItem
        {
            Type = ActionItemType.Absence,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.CustomerService,
            AssignedToUserId = null,
            RelatedEntityId = clientId,
            DeduplicationKey = dedupKey,
            Description = $"היעדרות כפולה: {clientName} נעדר/ה פעמיים ברצף בכיתה {className}."
        };

        _db.ActionItems.Add(item);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Ensures exactly one Open absence action item exists for a class with an unusually high
    /// dropout rate within one week. Idempotent: an Open item with key "dropout_mass_{classId}"
    /// already present → no-op. Concurrent insert races are absorbed via DbUpdateException + ChangeTracker.Clear().
    /// </summary>
    public async Task EnsureMassDropoutActionItemAsync(int classId)
    {
        var dedupKey = $"dropout_mass_{classId}";

        var existing = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        if (existing is not null)
            return;

        var cls = await _db.Classes.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == classId);
        var className = cls?.Name ?? classId.ToString();

        var item = new ActionItem
        {
            Type = ActionItemType.Absence,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.Admin,
            AssignedToUserId = null,
            RelatedEntityId = classId,
            DeduplicationKey = dedupKey,
            Description = $"נשירה חריגה: מספר תלמידים עזבו את כיתה {className} בתוך שבוע."
        };

        _db.ActionItems.Add(item);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Ensures exactly one Open dispute action item exists for the given logistics order.
    /// Idempotent: an Open item with key "dispute_{logisticsOrderId}" already present → no-op.
    /// Concurrent insert races are absorbed via DbUpdateException + ChangeTracker.Clear().
    /// </summary>
    public async Task EnsureDisputeActionItemAsync(int logisticsOrderId, int classId)
    {
        var dedupKey = $"dispute_{logisticsOrderId}";

        var existing = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        if (existing is not null)
            return;

        var order = await _db.LogisticsOrders
            .Include(o => o.Class)
            .Include(o => o.Model)
            .FirstOrDefaultAsync(o => o.Id == logisticsOrderId);

        var cls = order?.Class ?? await _db.Classes.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == classId);
        var className = cls?.Name ?? classId.ToString();
        var modelName = order?.Model?.Name ?? "";

        var item = new ActionItem
        {
            Type = ActionItemType.Dispute,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.Logistics,   // F4: the dispute loop is Logistics' to resolve (re-pack)
            AssignedToUserId = null,
            RelatedEntityId = logisticsOrderId,
            DeduplicationKey = dedupKey,
            Description = $"מחלוקת על הזמנה לוגיסטית: כיתה {className} · דגם {modelName}."
        };

        _db.ActionItems.Add(item);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Ensures birthday day-of action items exist for the given client. Always creates an admin
    /// item (key "birthday_dayof_{clientId}_{birthday:yyyy-MM-dd}_admin"). When instructorId is
    /// provided, also creates an instructor-assigned item (key "birthday_dayof_{clientId}_{birthday:yyyy-MM-dd}_user_{instructorId}").
    /// Each item is independently idempotent. Concurrent insert races absorbed via DbUpdateException + ChangeTracker.Clear().
    /// </summary>
    public async Task EnsureBirthdayDayOfActionItemAsync(int clientId, int? instructorId, DateOnly birthday)
    {
        var client = await _db.Clients.FindAsync(clientId);
        var clientName = client?.Name ?? clientId.ToString();
        var birthdayStr = birthday.ToString("yyyy-MM-dd");
        var description = $"יום הולדת היום: {clientName}.";

        if (instructorId.HasValue)
        {
            var instructorKey = $"birthday_dayof_{clientId}_{birthdayStr}_user_{instructorId.Value}";
            var existingInstructor = await _db.ActionItems
                .FirstOrDefaultAsync(a => a.DeduplicationKey == instructorKey && a.Status == ActionItemStatus.Open);
            if (existingInstructor is null)
            {
                _db.ActionItems.Add(new ActionItem
                {
                    Type = ActionItemType.Birthday,
                    Status = ActionItemStatus.Open,
                    AssignedToRole = null,
                    AssignedToUserId = instructorId.Value,
                    RelatedEntityId = clientId,
                    DeduplicationKey = instructorKey,
                    DueDate = birthday,
                    Description = description
                });
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
            }
        }

        var adminKey = $"birthday_dayof_{clientId}_{birthdayStr}_admin";
        var existingAdmin = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == adminKey && a.Status == ActionItemStatus.Open);
        if (existingAdmin is null)
        {
            _db.ActionItems.Add(new ActionItem
            {
                Type = ActionItemType.Birthday,
                Status = ActionItemStatus.Open,
                AssignedToRole = AppRoles.Admin,
                AssignedToUserId = null,
                RelatedEntityId = clientId,
                DeduplicationKey = adminKey,
                DueDate = birthday,
                Description = description
            });
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
        }
    }

    /// <summary>
    /// Ensures birthday 24-hours-ahead action items exist for the given client. Always creates an
    /// admin item (key "birthday_24h_{clientId}_{birthday:yyyy-MM-dd}_admin"). When instructorId
    /// is provided, also creates an instructor-assigned item (key "birthday_24h_{clientId}_{birthday:yyyy-MM-dd}_user_{instructorId}").
    /// Each item is independently idempotent. Concurrent insert races absorbed via DbUpdateException + ChangeTracker.Clear().
    /// </summary>
    public async Task EnsureBirthday24hActionItemAsync(int clientId, int? instructorId, DateOnly birthday)
    {
        var client = await _db.Clients.FindAsync(clientId);
        var clientName = client?.Name ?? clientId.ToString();
        var birthdayStr = birthday.ToString("yyyy-MM-dd");
        var description = $"יום הולדת מחר: {clientName}.";

        if (instructorId.HasValue)
        {
            var instructorKey = $"birthday_24h_{clientId}_{birthdayStr}_user_{instructorId.Value}";
            var existingInstructor = await _db.ActionItems
                .FirstOrDefaultAsync(a => a.DeduplicationKey == instructorKey && a.Status == ActionItemStatus.Open);
            if (existingInstructor is null)
            {
                _db.ActionItems.Add(new ActionItem
                {
                    Type = ActionItemType.Birthday,
                    Status = ActionItemStatus.Open,
                    AssignedToRole = null,
                    AssignedToUserId = instructorId.Value,
                    RelatedEntityId = clientId,
                    DeduplicationKey = instructorKey,
                    DueDate = birthday,
                    Description = description
                });
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
            }
        }

        var adminKey = $"birthday_24h_{clientId}_{birthdayStr}_admin";
        var existingAdmin = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == adminKey && a.Status == ActionItemStatus.Open);
        if (existingAdmin is null)
        {
            _db.ActionItems.Add(new ActionItem
            {
                Type = ActionItemType.Birthday,
                Status = ActionItemStatus.Open,
                AssignedToRole = AppRoles.Admin,
                AssignedToUserId = null,
                RelatedEntityId = clientId,
                DeduplicationKey = adminKey,
                DueDate = birthday,
                Description = description
            });
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
        }
    }

    /// <summary>
    /// Ensures exactly one Open tryout-followup action item exists for the given client+class pair.
    /// Idempotent: an Open item with key "tryout_followup_{clientId}_{classId}" already present → no-op.
    /// Concurrent insert races are absorbed via DbUpdateException + ChangeTracker.Clear().
    /// </summary>
    public async Task EnsureTryoutFollowupActionItemAsync(int clientId, int classId)
    {
        var dedupKey = $"tryout_followup_{clientId}_{classId}";

        var existing = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        if (existing is not null)
            return;

        var client = await _db.Clients.FindAsync(clientId);
        var cls = await _db.Classes.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == classId);

        var clientName = client?.Name ?? clientId.ToString();
        var className = cls?.Name ?? classId.ToString();

        var item = new ActionItem
        {
            Type = ActionItemType.TryoutFollowup,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.CustomerService,
            AssignedToUserId = null,
            RelatedEntityId = clientId,
            DeduplicationKey = dedupKey,
            Description = $"מעקב ניסיון: יש ליצור קשר לגבי {clientName} (כיתה {className})."
        };

        _db.ActionItems.Add(item);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
        }
    }
}
