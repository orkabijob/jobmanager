using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class SyllabusClassLinkTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public SyllabusClassLinkTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Class_links_and_unlinks_syllabus()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();

        int schoolId, yearId, syllabusId, classId;

        // ── Scope 1: seed prerequisites + link ──────────────────────────────
        using (var scope1 = factory.Services.CreateScope())
        {
            var db = scope1.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "תל אביב" };
            var year = new AcademicYear
            {
                Label = Guid.NewGuid().ToString("N")[..18],
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                IsCurrent = false
            };
            var syllabus = new Syllabus
            {
                Name = $"סילבוס-{Guid.NewGuid():N}",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                Status = EntityStatus.Active
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            db.Syllabi.Add(syllabus);
            await db.SaveChangesAsync();

            schoolId = school.Id;
            yearId = year.Id;
            syllabusId = syllabus.Id;

            var cls = new Class
            {
                Name = $"כיתה-{Guid.NewGuid():N}",
                SchoolId = schoolId,
                AcademicYearId = yearId,
                SyllabusId = syllabusId,
                Status = EntityStatus.Active
            };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            classId = cls.Id;
        }

        // ── Scope 2: reload + assert linked ─────────────────────────────────
        using (var scope2 = factory.Services.CreateScope())
        {
            var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.Classes.IgnoreQueryFilters().SingleAsync(c => c.Id == classId);
            Assert.Equal(syllabusId, cls.SyllabusId);
        }

        // ── Scope 3: unlink (set null) + save ───────────────────────────────
        using (var scope3 = factory.Services.CreateScope())
        {
            var db = scope3.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.Classes.IgnoreQueryFilters().SingleAsync(c => c.Id == classId);
            cls.SyllabusId = null;
            await db.SaveChangesAsync();
        }

        // ── Scope 4: reload + assert unlinked ───────────────────────────────
        using (var scope4 = factory.Services.CreateScope())
        {
            var db = scope4.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.Classes.IgnoreQueryFilters().SingleAsync(c => c.Id == classId);
            Assert.Null(cls.SyllabusId);
        }
    }
}
