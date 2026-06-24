using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class ArchivalFilterTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ArchivalFilterTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Archived_classes_are_hidden_by_default_and_visible_with_IgnoreQueryFilters()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var school = new School { Name = "בית ספר בדיקה", City = "חיפה" };
        var year = new AcademicYear { Label = "תשפ\"ו", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), IsCurrent = true };
        db.Schools.Add(school); db.AcademicYears.Add(year); await db.SaveChangesAsync();

        var tag = $"cls-{Guid.NewGuid():N}";
        db.Classes.Add(new Class { Name = tag + "-a", School = school, AcademicYear = year, Status = EntityStatus.Active });
        db.Classes.Add(new Class { Name = tag + "-b", School = school, AcademicYear = year, Status = EntityStatus.Archived });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Classes.Where(c => c.Name.StartsWith(tag)).CountAsync());
        Assert.Equal(2, await db.Classes.IgnoreQueryFilters().Where(c => c.Name.StartsWith(tag)).CountAsync());
    }
}
