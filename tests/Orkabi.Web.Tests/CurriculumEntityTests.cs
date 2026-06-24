using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class CurriculumEntityTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public CurriculumEntityTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(Syllabus active, Syllabus archived)> SeedSyllabi(AppDbContext db)
    {
        var tag = $"syl-{Guid.NewGuid():N}";
        var active = new Syllabus
        {
            Name = tag + "-active",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
            Status = EntityStatus.Active
        };
        var archived = new Syllabus
        {
            Name = tag + "-archived",
            StartDate = new DateOnly(2024, 9, 1),
            EndDate = new DateOnly(2025, 6, 30),
            Status = EntityStatus.Archived
        };
        db.Syllabi.Add(active);
        db.Syllabi.Add(archived);
        await db.SaveChangesAsync();
        return (active, archived);
    }

    [Fact]
    public async Task Archived_syllabi_hidden_by_filter_and_visible_with_IgnoreQueryFilters()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (active, _) = await SeedSyllabi(db);
        var prefix = active.Name[..active.Name.LastIndexOf('-')];

        // Default query — archived row must be hidden
        var filtered = await db.Syllabi.Where(s => s.Name.StartsWith(prefix)).CountAsync();
        // IgnoreQueryFilters — both rows visible
        var unfiltered = await db.Syllabi.IgnoreQueryFilters().Where(s => s.Name.StartsWith(prefix)).CountAsync();

        Assert.Equal(1, filtered);
        Assert.Equal(2, unfiltered);
    }

    [Fact]
    public async Task SyllabusModel_duplicate_model_in_same_syllabus_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int syllabusId, modelId;

        // Scope 1: seed syllabus + model + first junction row
        using (var scope1 = factory.Services.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            var syllabus = new Syllabus
            {
                Name = $"סילבוס-{Guid.NewGuid():N}",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                Status = EntityStatus.Active
            };
            var model = new Model { Name = $"מודל-{Guid.NewGuid():N}", ExpectedLessonsToComplete = 10 };
            db1.Syllabi.Add(syllabus);
            db1.Models.Add(model);
            await db1.SaveChangesAsync();
            syllabusId = syllabus.Id;
            modelId = model.Id;

            db1.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabusId, ModelId = modelId, OrderIndex = 1 });
            await db1.SaveChangesAsync();
        }

        // Scope 2: attempt duplicate (SyllabusId, ModelId) — composite PK violation
        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabusId, ModelId = modelId, OrderIndex = 2 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task SyllabusModel_duplicate_order_index_in_same_syllabus_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        int syllabusId, model1Id, model2Id;

        // Scope 1: seed syllabus + two models + first junction row at OrderIndex = 1
        using (var scope1 = factory.Services.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            var syllabus = new Syllabus
            {
                Name = $"סילבוס-{Guid.NewGuid():N}",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                Status = EntityStatus.Active
            };
            var model1 = new Model { Name = $"מודל-א-{Guid.NewGuid():N}", ExpectedLessonsToComplete = 10 };
            var model2 = new Model { Name = $"מודל-ב-{Guid.NewGuid():N}", ExpectedLessonsToComplete = 8 };
            db1.Syllabi.Add(syllabus);
            db1.Models.Add(model1);
            db1.Models.Add(model2);
            await db1.SaveChangesAsync();
            syllabusId = syllabus.Id;
            model1Id = model1.Id;
            model2Id = model2.Id;

            db1.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabusId, ModelId = model1Id, OrderIndex = 1 });
            await db1.SaveChangesAsync();
        }

        // Scope 2: attempt duplicate (SyllabusId, OrderIndex) — unique index violation
        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabusId, ModelId = model2Id, OrderIndex = 1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }
}
