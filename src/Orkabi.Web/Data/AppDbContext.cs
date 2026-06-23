using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Data;

public class AppDbContext : IdentityDbContext<AppUser, AppRole, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Throwaway test scaffold — remove before Slice 1
    public DbSet<Probe> Probes => Set<Probe>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ARCHIVAL: applied ONLY to aggregate roots that implement IArchivable.
        // Use IgnoreQueryFilters() for cross-year admin reports. Do NOT apply
        // to referenced lookup entities (would cause silent null navigations).
        b.Entity<Probe>().HasQueryFilter(p => p.Status == EntityStatus.Active);

        // Int-backed enum convention example (later entities follow this):
        b.Entity<Probe>().Property(p => p.Status).HasConversion<int>();
    }
}

// Throwaway test scaffold — remove before Slice 1
public class Probe : Orkabi.Web.Shared.BaseEntity, IArchivable
{
    public string Name { get; set; } = "";
    public EntityStatus Status { get; set; }
}
