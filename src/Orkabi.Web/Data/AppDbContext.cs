using Microsoft.EntityFrameworkCore;

namespace Orkabi.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
