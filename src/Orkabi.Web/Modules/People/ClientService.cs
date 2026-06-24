using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;

namespace Orkabi.Web.Modules.People;

public class ClientService
{
    private readonly AppDbContext _db;
    private readonly ActionItemService _actionHub;

    public ClientService(AppDbContext db, ActionItemService actionHub)
    {
        _db = db;
        _actionHub = actionHub;
    }

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

    /// <summary>
    /// Deactivates a client (sets IsActive = false) and, for each class the client is
    /// non-Drop enrolled in, checks whether ≥1 OTHER client in that class also went
    /// inactive within the last 7 days. If so, the current deactivation makes ≥2 inactive
    /// within 7 days — the mass-dropout threshold — and triggers an Admin ActionItem.
    ///
    /// THRESHOLD INTERPRETATION:
    ///   "≥2 clients within 7 days" means: after saving this deactivation, there must be
    ///   at least 1 OTHER inactive client (besides the one just deactivated) in the class
    ///   whose UpdatedAt >= UtcNow−7d. The count of 1 other + the current one = 2 total.
    ///
    /// TIMESTAMP APPROXIMATION:
    ///   We use Client.UpdatedAt as the deactivation timestamp. The audit interceptor
    ///   stamps UpdatedAt = DateTime.UtcNow on every SaveChangesAsync, so calling
    ///   DeactivateAsync produces an accurate timestamp for the client being deactivated.
    ///   For OTHER inactive clients, their UpdatedAt reflects the last SaveChanges on them,
    ///   which could be an unrelated edit (e.g. address update) that happens to post-date
    ///   the actual deactivation. This is a tolerable false-positive within a 7-day window:
    ///   the IsActive == false filter already gates to actually-inactive clients, and a
    ///   7-day window is forgiving enough that marginal edge cases don't cause harm.
    ///
    /// CONCERN (not fixed in this task):
    ///   Callers that set IsActive = false directly via UpdateAsync (e.g. a future
    ///   edit/archive path) will NOT go through this hook. If such paths exist or are
    ///   added, they should call DeactivateAsync instead. No such callers exist as of Slice 4.
    ///
    /// IDEMPOTENT: calling with an already-inactive client is a no-op.
    /// </summary>
    public async Task DeactivateAsync(int clientId, CancellationToken ct = default)
    {
        var client = await _db.Clients.FindAsync(new object[] { clientId }, ct);
        if (client is null || !client.IsActive)
            return;  // idempotent: already inactive or not found → no-op

        client.IsActive = false;
        // SaveChangesAsync triggers the audit interceptor which stamps UpdatedAt = UtcNow.
        await _db.SaveChangesAsync(ct);

        // For each class this client is non-Dropped enrolled in, check the 7-day window.
        var cutoff = DateTime.UtcNow.AddDays(-7);

        var classIds = await _db.Enrollments
            .Where(e => e.ClientId == clientId && e.Status != EnrollmentStatus.Dropped)
            .Select(e => e.ClassId)
            .ToListAsync(ct);

        foreach (var classId in classIds)
        {
            // Count OTHER clients in this class who are inactive AND whose UpdatedAt is
            // within the last 7 days (i.e. went inactive recently). The current client
            // (clientId) was just deactivated above and is excluded from this count.
            // count >= 1 means current + at least 1 other = ≥2 total within 7 days.
            var otherRecentInactiveCount = await _db.Enrollments
                .Where(e => e.ClassId == classId
                         && e.ClientId != clientId
                         && e.Status != EnrollmentStatus.Dropped
                         && !e.Client.IsActive
                         && e.Client.UpdatedAt >= cutoff)
                .CountAsync(ct);

            if (otherRecentInactiveCount >= 1)
                await _actionHub.EnsureMassDropoutActionItemAsync(classId);
        }
    }
}
