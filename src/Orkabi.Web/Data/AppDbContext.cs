using Microsoft.EntityFrameworkCore;

namespace Orkabi.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Throwaway test scaffold — remove before Slice 1
    public DbSet<Probe> Probes => Set<Probe>();
}

// Throwaway test scaffold — remove before Slice 1
public class Probe : Orkabi.Web.Shared.BaseEntity
{
    public string Name { get; set; } = "";
}
