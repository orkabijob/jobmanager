using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;

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
    /// Returns all Open action items assigned to the given role, ordered by CreatedAt ascending.
    /// </summary>
    public Task<List<ActionItem>> ListOpenForRoleAsync(string role) =>
        _db.ActionItems
            .Where(a => a.Status == ActionItemStatus.Open && a.AssignedToRole == role)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
}
