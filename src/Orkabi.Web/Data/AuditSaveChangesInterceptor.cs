using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Data;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _user;
    public AuditSaveChangesInterceptor(ICurrentUser user) => _user = user;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        var ctx = eventData.Context;
        if (ctx is not null) Stamp(ctx);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        var ctx = eventData.Context;
        if (ctx is not null) Stamp(ctx);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext ctx)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ctx.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedByUserId = _user.UserId;
            }
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedByUserId = _user.UserId;
            }
        }
    }
}
