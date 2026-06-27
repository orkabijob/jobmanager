using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Modules.Curriculum;

public class CurriculumService
{
    private readonly AppDbContext _db;
    public CurriculumService(AppDbContext db) => _db = db;

    // ── Model CRUD ───────────────────────────────────────────────────────────

    public Task<List<Model>> ListModelsAsync() =>
        _db.Models.OrderBy(m => m.Name).ToListAsync();

    public Task<Model?> GetModelAsync(int id) =>
        _db.Models.FindAsync(id).AsTask();

    public async Task<Model> CreateModelAsync(Model m)
    {
        _db.Models.Add(m);
        await _db.SaveChangesAsync();
        return m;
    }

    public async Task UpdateModelAsync(Model m)
    {
        _db.Models.Update(m);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes a model, but only if nothing references it (FK-guarded): not in any syllabus, not in
    /// any lesson log, not in any logistics order. Throws a friendly Hebrew error if it's in use.
    /// </summary>
    public async Task DeleteModelAsync(int id)
    {
        var model = await _db.Models.FindAsync(id)
            ?? throw new InvalidOperationException($"מודל {id} לא נמצא");

        var inUse = await _db.SyllabusModels.AnyAsync(sm => sm.ModelId == id)
                 || await _db.LessonLogs.IgnoreQueryFilters().AnyAsync(l => l.ModelId == id)
                 || await _db.LogisticsOrders.AnyAsync(o => o.ModelId == id);
        if (inUse)
            throw new InvalidOperationException("המודל בשימוש (בסילבוס, ביומן שיעור או בהזמנה) ולא ניתן למחיקה.");

        _db.Models.Remove(model);
        await _db.SaveChangesAsync();
    }

    // ── Syllabus CRUD ────────────────────────────────────────────────────────

    /// <summary>
    /// null = Active (relies on the global HasQueryFilter on Syllabus).
    /// Archived = bypass global filter and return only Archived rows.
    /// </summary>
    public Task<List<Syllabus>> ListSyllabiAsync(EntityStatus? status = null)
    {
        if (status == EntityStatus.Archived)
        {
            return _db.Syllabi
                .IgnoreQueryFilters()
                .Where(s => s.Status == EntityStatus.Archived)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        // null or Active — global filter already restricts to Active rows.
        return _db.Syllabi
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Returns the Syllabus with its SyllabusModels (ordered by OrderIndex) and their Models loaded.
    /// </summary>
    public Task<Syllabus?> GetSyllabusAsync(int id) =>
        _db.Syllabi
            .IgnoreQueryFilters()   // caller may request archived syllabus by id
            .Include(s => s.SyllabusModels.OrderBy(sm => sm.OrderIndex))
                .ThenInclude(sm => sm.Model)
            .FirstOrDefaultAsync(s => s.Id == id);

    public async Task<Syllabus> CreateSyllabusAsync(
        Syllabus s,
        IEnumerable<(int modelId, int orderIndex)> models)
    {
        _db.Syllabi.Add(s);
        await _db.SaveChangesAsync();

        foreach (var (modelId, orderIndex) in models)
        {
            _db.SyllabusModels.Add(new SyllabusModel
            {
                SyllabusId = s.Id,
                ModelId = modelId,
                OrderIndex = orderIndex
            });
        }
        await _db.SaveChangesAsync();
        return s;
    }

    /// <summary>
    /// Transaction: delete all existing junction rows for this syllabus, then insert the new set.
    /// </summary>
    public async Task UpdateSyllabusAsync(
        Syllabus s,
        IEnumerable<(int modelId, int orderIndex)> models)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        _db.Syllabi.Update(s);

        // Remove all existing junction rows for this syllabus.
        var existing = await _db.SyllabusModels
            .Where(sm => sm.SyllabusId == s.Id)
            .ToListAsync();
        _db.SyllabusModels.RemoveRange(existing);
        await _db.SaveChangesAsync();

        // Insert the new set.
        foreach (var (modelId, orderIndex) in models)
        {
            _db.SyllabusModels.Add(new SyllabusModel
            {
                SyllabusId = s.Id,
                ModelId = modelId,
                OrderIndex = orderIndex
            });
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task ArchiveSyllabusAsync(int id)
    {
        var syllabus = await _db.Syllabi.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException($"סילבוס {id} לא נמצא");
        syllabus.Status = EntityStatus.Archived;
        await _db.SaveChangesAsync();
    }

    // ── Ordered model operations ──────────────────────────────────────────────

    /// <summary>
    /// Appends modelId at OrderIndex = max(existing OrderIndex) + 1.
    /// </summary>
    public async Task AddModelToSyllabusAsync(int syllabusId, int modelId)
    {
        var maxOrder = await _db.SyllabusModels
            .Where(sm => sm.SyllabusId == syllabusId)
            .MaxAsync(sm => (int?)sm.OrderIndex) ?? 0;

        _db.SyllabusModels.Add(new SyllabusModel
        {
            SyllabusId = syllabusId,
            ModelId = modelId,
            OrderIndex = maxOrder + 1
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Swaps the OrderIndex of modelId with the adjacent row in the given direction
    /// (direction -1 = move up, +1 = move down).
    ///
    /// Unique-index collision avoidance: EF Core cannot swap two rows' OrderIndex
    /// values in a single SaveChanges — it detects a circular dependency through
    /// the unique (SyllabusId, OrderIndex) index and throws. The solution is a
    /// 3-SaveChanges sentinel approach within a transaction:
    ///   1. Park target at sentinel 0 (outside valid 1..n range); SaveChanges — frees target's slot.
    ///   2. Move neighbor to target's original index; SaveChanges — no collision (sentinel still holds target's slot).
    ///   3. Move target to neighbor's original index; SaveChanges — sentinel gone, final values in place.
    /// All 3 SaveChanges are inside one transaction so atomicity is maintained.
    /// </summary>
    public async Task ReorderAsync(int syllabusId, int modelId, int direction)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        var rows = await _db.SyllabusModels
            .Where(sm => sm.SyllabusId == syllabusId)
            .OrderBy(sm => sm.OrderIndex)
            .ToListAsync();

        var target = rows.FirstOrDefault(sm => sm.ModelId == modelId)
            ?? throw new InvalidOperationException($"מודל {modelId} לא נמצא בסילבוס {syllabusId}");

        SyllabusModel? neighbor = direction < 0
            ? rows.LastOrDefault(sm => sm.OrderIndex < target.OrderIndex)
            : rows.FirstOrDefault(sm => sm.OrderIndex > target.OrderIndex);

        if (neighbor is null)
        {
            // Already at the boundary — no-op.
            await tx.CommitAsync();
            return;
        }

        int targetFinalIndex = neighbor.OrderIndex;
        int neighborFinalIndex = target.OrderIndex;

        // Step 1: park target at sentinel 0 (outside valid 1..n range) to free its slot.
        target.OrderIndex = 0;
        await _db.SaveChangesAsync();

        // Step 2: move neighbor to target's original index.
        neighbor.OrderIndex = neighborFinalIndex;
        await _db.SaveChangesAsync();

        // Step 3: move target to neighbor's original index.
        target.OrderIndex = targetFinalIndex;
        await _db.SaveChangesAsync();

        await tx.CommitAsync();
    }

    /// <summary>
    /// Removes the junction row for (syllabusId, modelId) and compacts remaining
    /// OrderIndex values to be contiguous 1..n.
    ///
    /// Unique-index collision avoidance: mirrors the sentinel approach used by ReorderAsync.
    /// Compaction in a single SaveChanges relies on EF's undocumented ascending UPDATE order,
    /// which is not guaranteed on Postgres with a non-deferrable unique index. Instead:
    ///   1. Delete target row; SaveChanges — removes one slot, remaining rows are gap-y but valid.
    ///   2. Offset ALL remaining rows by +1000; SaveChanges — moves entire block into an empty
    ///      high range (valid OrderIndex values are small, so no collision is possible).
    ///   3. Assign final 1..n from the offset block; SaveChanges — target range 1..n is fully
    ///      vacated by step 2, so no transient collision regardless of UPDATE order.
    /// All 3 SaveChanges are inside one transaction so atomicity is maintained.
    /// </summary>
    public async Task RemoveModelFromSyllabusAsync(int syllabusId, int modelId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        var row = await _db.SyllabusModels
            .FirstOrDefaultAsync(sm => sm.SyllabusId == syllabusId && sm.ModelId == modelId)
            ?? throw new InvalidOperationException($"מודל {modelId} לא נמצא בסילבוס {syllabusId}");

        // Step 1: delete the target row.
        _db.SyllabusModels.Remove(row);
        await _db.SaveChangesAsync();

        var remaining = await _db.SyllabusModels
            .Where(sm => sm.SyllabusId == syllabusId)
            .OrderBy(sm => sm.OrderIndex)
            .ToListAsync();

        if (remaining.Count == 0)
        {
            await tx.CommitAsync();
            return;
        }

        // Step 2: offset all remaining rows into a high sentinel range (+1000) so that
        // the final target range 1..n is entirely vacated before we write to it.
        foreach (var sm in remaining)
            sm.OrderIndex += 1000;
        await _db.SaveChangesAsync();

        // Step 3: assign final contiguous 1..n — no collision possible since 1..n is empty.
        for (int i = 0; i < remaining.Count; i++)
            remaining[i].OrderIndex = i + 1;
        await _db.SaveChangesAsync();

        await tx.CommitAsync();
    }
}
