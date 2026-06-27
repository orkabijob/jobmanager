using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

// F16 — FK-guarded deletes for Curriculum Models and Schools.
public class DeletionGuardTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public DeletionGuardTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task DeleteModel_removes_an_unused_model()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var model = new Model { Name = $"מודל-{Guid.NewGuid():N}"[..12], ExpectedLessonsToComplete = 3 };
        db.Models.Add(model);
        await db.SaveChangesAsync();

        await svc.DeleteModelAsync(model.Id);

        db.ChangeTracker.Clear();
        Assert.Null(await db.Models.FindAsync(model.Id));
    }

    [Fact]
    public async Task DeleteModel_in_use_throws_and_keeps_it()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var model = new Model { Name = $"מודל-{Guid.NewGuid():N}"[..12], ExpectedLessonsToComplete = 3 };
        var syllabus = new Syllabus { Name = $"סילבוס-{Guid.NewGuid():N}"[..12], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), Status = EntityStatus.Active };
        db.Models.Add(model);
        db.Syllabi.Add(syllabus);
        await db.SaveChangesAsync();
        db.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabus.Id, ModelId = model.Id, OrderIndex = 1 });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteModelAsync(model.Id));

        db.ChangeTracker.Clear();
        Assert.NotNull(await db.Models.FindAsync(model.Id));
    }

    [Fact]
    public async Task DeleteSchool_removes_an_unused_school()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<SchoolService>();

        var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}"[..14], City = "תל אביב" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        await svc.DeleteAsync(school.Id);

        db.ChangeTracker.Clear();
        Assert.Null(await db.Schools.FindAsync(school.Id));
    }

    [Fact]
    public async Task DeleteSchool_with_a_class_throws_and_keeps_it()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<SchoolService>();

        var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}"[..14], City = "תל אביב" };
        var year = new AcademicYear { Label = $"y{Guid.NewGuid():N}"[..8], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), IsCurrent = false };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();
        db.Classes.Add(new Class { Name = $"כיתה-{Guid.NewGuid():N}"[..12], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(school.Id));

        db.ChangeTracker.Clear();
        Assert.NotNull(await db.Schools.FindAsync(school.Id));
    }
}
