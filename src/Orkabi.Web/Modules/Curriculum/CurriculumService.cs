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
    /// 3-step sentinel approach within a transaction:
    ///   1. Set target.OrderIndex to a sentinel (0, which is outside the 1..n range).
    ///   2. SaveChanges — neighbor's index is unchanged, target has sentinel. No collision.
    ///   3. Set target to neighbor's old index; set neighbor to target's old index.
    ///   4. SaveChanges — sentinel is gone, final values in place. No collision.
    /// All 4 steps are inside one transaction so atomicity is maintained.
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
    /// </summary>
    public async Task RemoveModelFromSyllabusAsync(int syllabusId, int modelId)
    {
        var row = await _db.SyllabusModels
            .FirstOrDefaultAsync(sm => sm.SyllabusId == syllabusId && sm.ModelId == modelId)
            ?? throw new InvalidOperationException($"מודל {modelId} לא נמצא בסילבוס {syllabusId}");

        _db.SyllabusModels.Remove(row);
        await _db.SaveChangesAsync();

        // Compact: reload remaining rows ordered, then reassign 1..n.
        // Because the OrderIndex is unique per (SyllabusId, OrderIndex), we must
        // avoid transient collisions during reassignment. Strategy: update to a
        // temporary offset (add a large sentinel), SaveChanges, then update to final.
        // However, since we are removing one row, the remaining rows only need
        // contiguous renumbering. We can do this safely by processing them in ascending
        // order because the newly assigned index is always ≤ the current index (we
        // fill the gap of the removed row), so no intermediate collision can occur.
        var remaining = await _db.SyllabusModels
            .Where(sm => sm.SyllabusId == syllabusId)
            .OrderBy(sm => sm.OrderIndex)
            .ToListAsync();

        for (int i = 0; i < remaining.Count; i++)
        {
            remaining[i].OrderIndex = i + 1;
        }
        await _db.SaveChangesAsync();
    }
}
