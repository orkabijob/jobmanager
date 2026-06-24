using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Modules.People;

public class ClassService
{
    private readonly AppDbContext _db;
    public ClassService(AppDbContext db) => _db = db;

    public Task<List<Class>> ListAsync(int? schoolId, int? academicYearId, EntityStatus? status)
    {
        // When requesting Archived, bypass the global filter (Status==Active) and filter explicitly.
        // null/Active status relies on the global filter to exclude archived rows.
        IQueryable<Class> query = status == EntityStatus.Archived
            ? _db.Classes.IgnoreQueryFilters().Where(c => c.Status == EntityStatus.Archived)
            : _db.Classes; // global filter already restricts to Active

        if (schoolId.HasValue)
            query = query.Where(c => c.SchoolId == schoolId.Value);
        if (academicYearId.HasValue)
            query = query.Where(c => c.AcademicYearId == academicYearId.Value);

        return query
            .Include(c => c.School)
            .Include(c => c.AcademicYear)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public Task<Class?> GetAsync(int id) =>
        _db.Classes.Include(c => c.School).Include(c => c.AcademicYear)
            .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<Class> CreateAsync(Class c)
    {
        _db.Classes.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    public async Task UpdateAsync(Class c)
    {
        _db.Classes.Update(c);
        await _db.SaveChangesAsync();
    }

    public async Task ArchiveAsync(int id)
    {
        var cls = await _db.Classes.FindAsync(id)
            ?? throw new InvalidOperationException($"כיתה {id} לא נמצאה");
        cls.Status = EntityStatus.Archived;
        await _db.SaveChangesAsync();
    }
}
