using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class CurriculumServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public CurriculumServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<Model> SeedModelAsync(AppDbContext db, string? name = null, int expected = 10)
    {
        var model = new Model
        {
            Name = name ?? $"מודל-{Guid.NewGuid():N}",
            ExpectedLessonsToComplete = expected
        };
        db.Models.Add(model);
        await db.SaveChangesAsync();
        return model;
    }

    private static async Task<Syllabus> SeedSyllabusAsync(AppDbContext db, EntityStatus status = EntityStatus.Active)
    {
        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
            Status = status
        };
        db.Syllabi.Add(syllabus);
        await db.SaveChangesAsync();
        return syllabus;
    }

    // ── Model CRUD ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateModelAsync_persists_and_returns_with_id()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var model = new Model { Name = "ריצה מהירה", ExpectedLessonsToComplete = 5 };
        var created = await svc.CreateModelAsync(model);

        Assert.True(created.Id > 0);
        Assert.Equal("ריצה מהירה", created.Name);
    }

    [Fact]
    public async Task ListModelsAsync_returns_all_models()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);
        var m2 = await SeedModelAsync(db);

        var list = await svc.ListModelsAsync();

        Assert.Contains(list, m => m.Id == m1.Id);
        Assert.Contains(list, m => m.Id == m2.Id);
    }

    [Fact]
    public async Task GetModelAsync_returns_null_for_missing_id()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var result = await svc.GetModelAsync(99999);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateModelAsync_persists_changes()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var model = await SeedModelAsync(db, expected: 3);
        model.ExpectedLessonsToComplete = 99;
        await svc.UpdateModelAsync(model);

        db.ChangeTracker.Clear();
        var reloaded = await db.Models.FindAsync(model.Id);
        Assert.Equal(99, reloaded!.ExpectedLessonsToComplete);
    }

    // ── Syllabus CRUD ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSyllabusAsync_creates_with_ordered_models()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);
        var m2 = await SeedModelAsync(db);

        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
        };

        var created = await svc.CreateSyllabusAsync(syllabus, new[]
        {
            (m1.Id, 1),
            (m2.Id, 2)
        });

        Assert.True(created.Id > 0);

        // GetSyllabusAsync must return ordered junction rows.
        var loaded = await svc.GetSyllabusAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.SyllabusModels.Count);
        var ordered = loaded.SyllabusModels.OrderBy(sm => sm.OrderIndex).ToList();
        Assert.Equal(1, ordered[0].OrderIndex);
        Assert.Equal(m1.Id, ordered[0].ModelId);
        Assert.Equal(2, ordered[1].OrderIndex);
        Assert.Equal(m2.Id, ordered[1].ModelId);
        // Navigation to Model should be loaded.
        Assert.NotNull(ordered[0].Model);
    }

    [Fact]
    public async Task ListSyllabiAsync_default_excludes_archived()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var active = await SeedSyllabusAsync(db, EntityStatus.Active);
        var archived = await SeedSyllabusAsync(db, EntityStatus.Archived);

        var list = await svc.ListSyllabiAsync();

        Assert.Contains(list, s => s.Id == active.Id);
        Assert.DoesNotContain(list, s => s.Id == archived.Id);
    }

    [Fact]
    public async Task ListSyllabiAsync_Archived_returns_only_archived()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var active = await SeedSyllabusAsync(db, EntityStatus.Active);
        var archived = await SeedSyllabusAsync(db, EntityStatus.Archived);

        var list = await svc.ListSyllabiAsync(EntityStatus.Archived);

        Assert.DoesNotContain(list, s => s.Id == active.Id);
        Assert.Contains(list, s => s.Id == archived.Id);
    }

    [Fact]
    public async Task ArchiveSyllabusAsync_sets_status_Archived()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var syllabus = await SeedSyllabusAsync(db, EntityStatus.Active);

        await svc.ArchiveSyllabusAsync(syllabus.Id);

        db.ChangeTracker.Clear();
        var reloaded = await db.Syllabi.IgnoreQueryFilters().FirstAsync(s => s.Id == syllabus.Id);
        Assert.Equal(EntityStatus.Archived, reloaded.Status);
    }

    [Fact]
    public async Task UpdateSyllabusAsync_replaces_junction_rows_in_transaction()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);
        var m2 = await SeedModelAsync(db);
        var m3 = await SeedModelAsync(db);

        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
        };
        await svc.CreateSyllabusAsync(syllabus, new[] { (m1.Id, 1), (m2.Id, 2) });

        // Update: replace with m2, m3 (drop m1, add m3, reindex).
        syllabus.Name = "שם-מעודכן";
        await svc.UpdateSyllabusAsync(syllabus, new[] { (m2.Id, 1), (m3.Id, 2) });

        db.ChangeTracker.Clear();
        var loaded = await svc.GetSyllabusAsync(syllabus.Id);
        Assert.NotNull(loaded);
        Assert.Equal("שם-מעודכן", loaded!.Name);
        var modelIds = loaded.SyllabusModels.OrderBy(sm => sm.OrderIndex).Select(sm => sm.ModelId).ToList();
        Assert.Equal(new[] { m2.Id, m3.Id }, modelIds);
    }

    // ── Ordered model operations ──────────────────────────────────────────────

    [Fact]
    public async Task AddModelToSyllabusAsync_appends_at_max_plus_one()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);
        var m2 = await SeedModelAsync(db);
        var m3 = await SeedModelAsync(db);

        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
        };
        await svc.CreateSyllabusAsync(syllabus, new[] { (m1.Id, 1), (m2.Id, 2) });

        // Append m3 — should get OrderIndex 3.
        await svc.AddModelToSyllabusAsync(syllabus.Id, m3.Id);

        db.ChangeTracker.Clear();
        var loaded = await svc.GetSyllabusAsync(syllabus.Id);
        var last = loaded!.SyllabusModels.OrderBy(sm => sm.OrderIndex).Last();
        Assert.Equal(m3.Id, last.ModelId);
        Assert.Equal(3, last.OrderIndex);
    }

    [Fact]
    public async Task ReorderAsync_up_swaps_orderindex_correctly()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);
        var m2 = await SeedModelAsync(db);
        var m3 = await SeedModelAsync(db);

        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
        };
        // Initial order: m1=1, m2=2, m3=3
        await svc.CreateSyllabusAsync(syllabus, new[] { (m1.Id, 1), (m2.Id, 2), (m3.Id, 3) });

        // Move m2 up (direction=-1) → m2 should become 1, m1 should become 2.
        await svc.ReorderAsync(syllabus.Id, m2.Id, -1);

        db.ChangeTracker.Clear();
        var loaded = await svc.GetSyllabusAsync(syllabus.Id);
        var ordered = loaded!.SyllabusModels.OrderBy(sm => sm.OrderIndex).ToList();
        Assert.Equal(m2.Id, ordered[0].ModelId);
        Assert.Equal(1, ordered[0].OrderIndex);
        Assert.Equal(m1.Id, ordered[1].ModelId);
        Assert.Equal(2, ordered[1].OrderIndex);
        Assert.Equal(m3.Id, ordered[2].ModelId);
        Assert.Equal(3, ordered[2].OrderIndex);
    }

    [Fact]
    public async Task ReorderAsync_down_swaps_orderindex_correctly()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);
        var m2 = await SeedModelAsync(db);
        var m3 = await SeedModelAsync(db);

        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
        };
        // Initial order: m1=1, m2=2, m3=3
        await svc.CreateSyllabusAsync(syllabus, new[] { (m1.Id, 1), (m2.Id, 2), (m3.Id, 3) });

        // Move m2 down (direction=+1) → m2 should become 3, m3 should become 2.
        await svc.ReorderAsync(syllabus.Id, m2.Id, +1);

        db.ChangeTracker.Clear();
        var loaded = await svc.GetSyllabusAsync(syllabus.Id);
        var ordered = loaded!.SyllabusModels.OrderBy(sm => sm.OrderIndex).ToList();
        Assert.Equal(m1.Id, ordered[0].ModelId);
        Assert.Equal(1, ordered[0].OrderIndex);
        Assert.Equal(m3.Id, ordered[1].ModelId);
        Assert.Equal(2, ordered[1].OrderIndex);
        Assert.Equal(m2.Id, ordered[2].ModelId);
        Assert.Equal(3, ordered[2].OrderIndex);
    }

    [Fact]
    public async Task RemoveModelFromSyllabusAsync_removes_and_compacts_orderindex()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);
        var m2 = await SeedModelAsync(db);
        var m3 = await SeedModelAsync(db);

        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
        };
        // Initial order: m1=1, m2=2, m3=3
        await svc.CreateSyllabusAsync(syllabus, new[] { (m1.Id, 1), (m2.Id, 2), (m3.Id, 3) });

        // Remove m2 (index 2). Remaining should be m1=1, m3=2 (compacted).
        await svc.RemoveModelFromSyllabusAsync(syllabus.Id, m2.Id);

        db.ChangeTracker.Clear();
        var loaded = await svc.GetSyllabusAsync(syllabus.Id);
        var ordered = loaded!.SyllabusModels.OrderBy(sm => sm.OrderIndex).ToList();
        Assert.Equal(2, ordered.Count);
        Assert.Equal(m1.Id, ordered[0].ModelId);
        Assert.Equal(1, ordered[0].OrderIndex);
        Assert.Equal(m3.Id, ordered[1].ModelId);
        Assert.Equal(2, ordered[1].OrderIndex);
    }

    [Fact]
    public async Task ReorderAsync_up_on_first_row_is_noop()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);
        var m2 = await SeedModelAsync(db);

        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
        };
        await svc.CreateSyllabusAsync(syllabus, new[] { (m1.Id, 1), (m2.Id, 2) });

        // Moving the first row up must be a no-op — no exception, order unchanged.
        await svc.ReorderAsync(syllabus.Id, m1.Id, -1);

        db.ChangeTracker.Clear();
        var loaded = await svc.GetSyllabusAsync(syllabus.Id);
        var ordered = loaded!.SyllabusModels.OrderBy(sm => sm.OrderIndex).ToList();
        Assert.Equal(m1.Id, ordered[0].ModelId);
        Assert.Equal(1, ordered[0].OrderIndex);
        Assert.Equal(m2.Id, ordered[1].ModelId);
        Assert.Equal(2, ordered[1].OrderIndex);
    }

    [Fact]
    public async Task AddModelToSyllabusAsync_to_empty_syllabus_starts_at_one()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);

        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
        };
        // Create syllabus with no models.
        await svc.CreateSyllabusAsync(syllabus, Array.Empty<(int, int)>());

        // Adding the first model to an empty syllabus must get OrderIndex 1.
        await svc.AddModelToSyllabusAsync(syllabus.Id, m1.Id);

        db.ChangeTracker.Clear();
        var loaded = await svc.GetSyllabusAsync(syllabus.Id);
        Assert.Single(loaded!.SyllabusModels);
        Assert.Equal(1, loaded.SyllabusModels.First().OrderIndex);
    }

    [Fact]
    public async Task RemoveModelFromSyllabusAsync_missing_model_id_throws()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CurriculumService>();

        var m1 = await SeedModelAsync(db);
        var m2 = await SeedModelAsync(db);

        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
        };
        await svc.CreateSyllabusAsync(syllabus, new[] { (m1.Id, 1) });

        // m2 is not in this syllabus — must throw InvalidOperationException.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RemoveModelFromSyllabusAsync(syllabus.Id, m2.Id));
    }
}
