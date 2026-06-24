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
}
