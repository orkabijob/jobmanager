using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;

namespace Orkabi.Web.Modules.People;

public class ClientService
{
    private readonly AppDbContext _db;
    public ClientService(AppDbContext db) => _db = db;

    // activeOnly:false intentionally includes IsActive=false clients (dropped-out students).
    // IsActive is orthogonal to archival — inactive clients are NOT hidden, just filterable.
    public Task<List<Client>> ListAsync(string? q, bool activeOnly)
    {
        var query = _db.Clients.AsQueryable();
        if (activeOnly)
            query = query.Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(c => c.Name.Contains(q) || (c.ParentPhone != null && c.ParentPhone.Contains(q)));
        return query.OrderBy(c => c.Name).ToListAsync();
    }

    public Task<Client?> GetAsync(int id) =>
        _db.Clients.FindAsync(id).AsTask();

    public async Task<Client> CreateAsync(Client c)
    {
        _db.Clients.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    public async Task UpdateAsync(Client c)
    {
        _db.Clients.Update(c);
        await _db.SaveChangesAsync();
    }
}
