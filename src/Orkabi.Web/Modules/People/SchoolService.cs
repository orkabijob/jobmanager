using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;

namespace Orkabi.Web.Modules.People;

public class SchoolService
{
    private readonly AppDbContext _db;
    public SchoolService(AppDbContext db) => _db = db;

    public Task<List<School>> ListAsync(string? q = null)
    {
        var query = _db.Schools.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(s => s.Name.Contains(q) || s.City.Contains(q));
        return query.OrderBy(s => s.Name).ToListAsync();
    }

    public Task<School?> GetAsync(int id) =>
        _db.Schools.FindAsync(id).AsTask();

    public async Task<School> CreateAsync(School s)
    {
        _db.Schools.Add(s);
        await _db.SaveChangesAsync();
        return s;
    }

    public async Task UpdateAsync(School s)
    {
        _db.Schools.Update(s);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes a school, but only if no class references it (FK-guarded; archived classes count too).
    /// Throws a friendly Hebrew error if classes are still attached.
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        var school = await _db.Schools.FindAsync(id)
            ?? throw new InvalidOperationException($"בית ספר {id} לא נמצא");

        var inUse = await _db.Classes.IgnoreQueryFilters().AnyAsync(c => c.SchoolId == id);
        if (inUse)
            throw new InvalidOperationException("לבית הספר משויכות כיתות ולא ניתן למחיקה.");

        _db.Schools.Remove(school);
        await _db.SaveChangesAsync();
    }
}
