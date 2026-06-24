using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class ActionItemServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ActionItemServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<(Class cls, Model model)> SeedClassAndModelAsync(AppDbContext db)
    {
        var school = new School { Name = $"בית ספר {Guid.NewGuid():N}", City = "תל אביב" };
        var year = new AcademicYear
        {
            Label = $"תשפ\"-{Guid.NewGuid().ToString("N")[..4]}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
            IsCurrent = false
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Class
        {
            Name = $"כיתה-{Guid.NewGuid():N}",
            School = school,
            AcademicYear = year,
            Status = EntityStatus.Active
        };
        var model = new Model
        {
            Name = $"מודל-{Guid.NewGuid():N}",
            ExpectedLessonsToComplete = 10
        };
        db.Classes.Add(cls);
        db.Models.Add(model);
        await db.SaveChangesAsync();
        return (cls, model);
    }

    // ─── EnsureGapActionItemAsync ─────────────────────────────────────────────

    [Fact]
    public async Task EnsureGap_creates_open_admin_gap_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, model) = await SeedClassAndModelAsync(db);

        await svc.EnsureGapActionItemAsync(cls.Id, model.Id, expected: 10, spent: 12);

        var item = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == $"gap_{cls.Id}_{model.Id}");

        Assert.Equal(ActionItemType.Gap, item.Type);
        Assert.Equal(ActionItemStatus.Open, item.Status);
        Assert.Equal(AppRoles.Admin, item.AssignedToRole);
        Assert.Null(item.AssignedToUserId);
        Assert.Equal(cls.Id, item.RelatedEntityId);
        Assert.Equal($"gap_{cls.Id}_{model.Id}", item.DeduplicationKey);
        Assert.Contains(cls.Name, item.Description);
        Assert.Contains(model.Name, item.Description);
    }

    [Fact]
    public async Task EnsureGap_idempotent_second_call_creates_no_duplicate()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, model) = await SeedClassAndModelAsync(db);
        var dedupKey = $"gap_{cls.Id}_{model.Id}";

        await svc.EnsureGapActionItemAsync(cls.Id, model.Id, expected: 10, spent: 12);
        await svc.EnsureGapActionItemAsync(cls.Id, model.Id, expected: 10, spent: 14);

        var count = await db.ActionItems.CountAsync(a => a.DeduplicationKey == dedupKey);
        Assert.Equal(1, count);
    }

    // ─── ListOpenForRoleAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ListOpenForRoleAsync_returns_admin_open_items_ordered()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls1, model1) = await SeedClassAndModelAsync(db);
        var (cls2, model2) = await SeedClassAndModelAsync(db);

        // Create two gap items for Admin and one for another role.
        await svc.EnsureGapActionItemAsync(cls1.Id, model1.Id, expected: 10, spent: 12);
        await svc.EnsureGapActionItemAsync(cls2.Id, model2.Id, expected: 10, spent: 15);

        // Add a non-Admin item directly so it doesn't interfere.
        db.ActionItems.Add(new ActionItem
        {
            Type = ActionItemType.Task,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.Instructor,
            Description = "בדיקה"
        });
        await db.SaveChangesAsync();

        var adminItems = await svc.ListOpenForRoleAsync(AppRoles.Admin);

        Assert.Equal(2, adminItems.Count);
        Assert.All(adminItems, i => Assert.Equal(AppRoles.Admin, i.AssignedToRole));
        Assert.All(adminItems, i => Assert.Equal(ActionItemStatus.Open, i.Status));
        // Ordered by CreatedAt ascending (first created = first in list).
        Assert.True(adminItems[0].CreatedAt <= adminItems[1].CreatedAt);
    }
}
