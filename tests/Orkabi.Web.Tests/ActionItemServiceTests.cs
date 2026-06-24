using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;
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

    private static async Task<Client> SeedClientAsync(AppDbContext db)
    {
        var client = new Client { Name = $"לקוח-{Guid.NewGuid():N}" };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    private static async Task<LogisticsOrder> SeedLogisticsOrderAsync(AppDbContext db, int classId, int modelId)
    {
        var order = new LogisticsOrder { ClassId = classId, ModelId = modelId, Quantity = 1 };
        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<AppUser> SeedInstructorAsync(AppDbContext db, Microsoft.AspNetCore.Identity.UserManager<AppUser> um)
    {
        var user = new AppUser
        {
            UserName = $"instructor_{Guid.NewGuid():N}@test.com",
            Email = $"instructor_{Guid.NewGuid():N}@test.com",
            FullName = "מדריך"
        };
        await um.CreateAsync(user, "Test1234!");
        return user;
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

        // Snapshot admin count before seeding (shared fixture DB may have items from other tests).
        var countBefore = (await svc.ListOpenForRoleAsync(AppRoles.Admin)).Count;

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

        Assert.Equal(countBefore + 2, adminItems.Count);
        Assert.All(adminItems, i => Assert.Equal(AppRoles.Admin, i.AssignedToRole));
        Assert.All(adminItems, i => Assert.Equal(ActionItemStatus.Open, i.Status));
        // Ordered by CreatedAt ascending (first created = first in list).
        Assert.True(adminItems[0].CreatedAt <= adminItems[adminItems.Count - 1].CreatedAt);
    }

    // ─── EnsureDoubleAbsenceActionItemAsync ──────────────────────────────────

    [Fact]
    public async Task EnsureDoubleAbsence_creates_cs_absence_item_with_correct_key_and_description()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, _) = await SeedClassAndModelAsync(db);
        var client = await SeedClientAsync(db);
        var dedupKey = $"absence_double_{client.Id}_{cls.Id}";

        await svc.EnsureDoubleAbsenceActionItemAsync(client.Id, cls.Id);

        var item = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == dedupKey);
        Assert.Equal(ActionItemType.Absence, item.Type);
        Assert.Equal(ActionItemStatus.Open, item.Status);
        Assert.Equal(AppRoles.CustomerService, item.AssignedToRole);
        Assert.Null(item.AssignedToUserId);
        Assert.Equal(client.Id, item.RelatedEntityId);
        Assert.Contains(client.Name, item.Description);
        Assert.Contains(cls.Name, item.Description);
    }

    [Fact]
    public async Task EnsureDoubleAbsence_idempotent_second_call_no_duplicate()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, _) = await SeedClassAndModelAsync(db);
        var client = await SeedClientAsync(db);
        var dedupKey = $"absence_double_{client.Id}_{cls.Id}";

        await svc.EnsureDoubleAbsenceActionItemAsync(client.Id, cls.Id);
        await svc.EnsureDoubleAbsenceActionItemAsync(client.Id, cls.Id);

        var count = await db.ActionItems.CountAsync(a => a.DeduplicationKey == dedupKey);
        Assert.Equal(1, count);
    }

    // ─── EnsureMassDropoutActionItemAsync ────────────────────────────────────

    [Fact]
    public async Task EnsureMassDropout_creates_admin_absence_item_with_correct_key_and_description()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, _) = await SeedClassAndModelAsync(db);
        var dedupKey = $"dropout_mass_{cls.Id}";

        await svc.EnsureMassDropoutActionItemAsync(cls.Id);

        var item = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == dedupKey);
        Assert.Equal(ActionItemType.Absence, item.Type);
        Assert.Equal(ActionItemStatus.Open, item.Status);
        Assert.Equal(AppRoles.Admin, item.AssignedToRole);
        Assert.Null(item.AssignedToUserId);
        Assert.Equal(cls.Id, item.RelatedEntityId);
        Assert.Contains(cls.Name, item.Description);
    }

    [Fact]
    public async Task EnsureMassDropout_idempotent_second_call_no_duplicate()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, _) = await SeedClassAndModelAsync(db);
        var dedupKey = $"dropout_mass_{cls.Id}";

        await svc.EnsureMassDropoutActionItemAsync(cls.Id);
        await svc.EnsureMassDropoutActionItemAsync(cls.Id);

        var count = await db.ActionItems.CountAsync(a => a.DeduplicationKey == dedupKey);
        Assert.Equal(1, count);
    }

    // ─── EnsureDisputeActionItemAsync ─────────────────────────────────────────

    [Fact]
    public async Task EnsureDispute_creates_admin_dispute_item_with_correct_key_and_description()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, _) = await SeedClassAndModelAsync(db);
        var order = await SeedLogisticsOrderAsync(db, cls.Id, (await db.Models.FirstAsync()).Id);
        var dedupKey = $"dispute_{order.Id}";

        await svc.EnsureDisputeActionItemAsync(order.Id, cls.Id);

        var item = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == dedupKey);
        Assert.Equal(ActionItemType.Dispute, item.Type);
        Assert.Equal(ActionItemStatus.Open, item.Status);
        Assert.Equal(AppRoles.Admin, item.AssignedToRole);
        Assert.Null(item.AssignedToUserId);
        Assert.Equal(order.Id, item.RelatedEntityId);
        Assert.Contains(cls.Name, item.Description);
    }

    [Fact]
    public async Task EnsureDispute_idempotent_second_call_no_duplicate()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, _) = await SeedClassAndModelAsync(db);
        var order = await SeedLogisticsOrderAsync(db, cls.Id, (await db.Models.FirstAsync()).Id);
        var dedupKey = $"dispute_{order.Id}";

        await svc.EnsureDisputeActionItemAsync(order.Id, cls.Id);
        await svc.EnsureDisputeActionItemAsync(order.Id, cls.Id);

        var count = await db.ActionItems.CountAsync(a => a.DeduplicationKey == dedupKey);
        Assert.Equal(1, count);
    }

    // ─── EnsureBirthdayDayOfActionItemAsync ──────────────────────────────────

    [Fact]
    public async Task EnsureBirthdayDayOf_with_instructor_creates_both_instructor_and_admin_items()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();
        var um = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();

        var client = await SeedClientAsync(db);
        var instructor = await SeedInstructorAsync(db, um);
        var birthday = new DateOnly(1995, 3, 15);

        await svc.EnsureBirthdayDayOfActionItemAsync(client.Id, instructor.Id, birthday);

        var instructorKey = $"birthday_dayof_{client.Id}_{birthday:yyyy-MM-dd}_user_{instructor.Id}";
        var adminKey = $"birthday_dayof_{client.Id}_{birthday:yyyy-MM-dd}_admin";

        var instructorItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == instructorKey);
        var adminItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == adminKey);

        Assert.Equal(ActionItemType.Birthday, instructorItem.Type);
        Assert.Equal(instructor.Id, instructorItem.AssignedToUserId);
        Assert.Null(instructorItem.AssignedToRole);
        Assert.Equal(birthday, instructorItem.DueDate);
        Assert.Contains(client.Name, instructorItem.Description);

        Assert.Equal(ActionItemType.Birthday, adminItem.Type);
        Assert.Equal(AppRoles.Admin, adminItem.AssignedToRole);
        Assert.Null(adminItem.AssignedToUserId);
        Assert.Equal(birthday, adminItem.DueDate);
        Assert.Contains(client.Name, adminItem.Description);
    }

    [Fact]
    public async Task EnsureBirthdayDayOf_without_instructor_creates_only_admin_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var client = await SeedClientAsync(db);
        var birthday = new DateOnly(1995, 3, 16);

        await svc.EnsureBirthdayDayOfActionItemAsync(client.Id, null, birthday);

        var adminKey = $"birthday_dayof_{client.Id}_{birthday:yyyy-MM-dd}_admin";
        var adminItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == adminKey);
        Assert.Equal(ActionItemType.Birthday, adminItem.Type);
        Assert.Equal(AppRoles.Admin, adminItem.AssignedToRole);

        // No instructor item should exist.
        var instructorItemsCount = await db.ActionItems
            .CountAsync(a => a.DeduplicationKey != null && a.DeduplicationKey.StartsWith($"birthday_dayof_{client.Id}_{birthday:yyyy-MM-dd}_user_"));
        Assert.Equal(0, instructorItemsCount);
    }

    [Fact]
    public async Task EnsureBirthdayDayOf_idempotent_second_call_no_duplicates()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();
        var um = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();

        var client = await SeedClientAsync(db);
        var instructor = await SeedInstructorAsync(db, um);
        var birthday = new DateOnly(1995, 3, 17);

        await svc.EnsureBirthdayDayOfActionItemAsync(client.Id, instructor.Id, birthday);
        await svc.EnsureBirthdayDayOfActionItemAsync(client.Id, instructor.Id, birthday);

        var instructorKey = $"birthday_dayof_{client.Id}_{birthday:yyyy-MM-dd}_user_{instructor.Id}";
        var adminKey = $"birthday_dayof_{client.Id}_{birthday:yyyy-MM-dd}_admin";

        Assert.Equal(1, await db.ActionItems.CountAsync(a => a.DeduplicationKey == instructorKey));
        Assert.Equal(1, await db.ActionItems.CountAsync(a => a.DeduplicationKey == adminKey));
    }

    // ─── EnsureBirthday24hActionItemAsync ────────────────────────────────────

    [Fact]
    public async Task EnsureBirthday24h_with_instructor_creates_both_instructor_and_admin_items()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();
        var um = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();

        var client = await SeedClientAsync(db);
        var instructor = await SeedInstructorAsync(db, um);
        var birthday = new DateOnly(1995, 4, 1);

        await svc.EnsureBirthday24hActionItemAsync(client.Id, instructor.Id, birthday);

        var instructorKey = $"birthday_24h_{client.Id}_{birthday:yyyy-MM-dd}_user_{instructor.Id}";
        var adminKey = $"birthday_24h_{client.Id}_{birthday:yyyy-MM-dd}_admin";

        var instructorItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == instructorKey);
        var adminItem = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == adminKey);

        Assert.Equal(ActionItemType.Birthday, instructorItem.Type);
        Assert.Equal(instructor.Id, instructorItem.AssignedToUserId);
        Assert.Null(instructorItem.AssignedToRole);
        Assert.Contains(client.Name, instructorItem.Description);

        Assert.Equal(ActionItemType.Birthday, adminItem.Type);
        Assert.Equal(AppRoles.Admin, adminItem.AssignedToRole);
        Assert.Contains(client.Name, adminItem.Description);
    }

    [Fact]
    public async Task EnsureBirthday24h_idempotent_second_call_no_duplicates()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();
        var um = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();

        var client = await SeedClientAsync(db);
        var instructor = await SeedInstructorAsync(db, um);
        var birthday = new DateOnly(1995, 4, 2);

        await svc.EnsureBirthday24hActionItemAsync(client.Id, instructor.Id, birthday);
        await svc.EnsureBirthday24hActionItemAsync(client.Id, instructor.Id, birthday);

        var instructorKey = $"birthday_24h_{client.Id}_{birthday:yyyy-MM-dd}_user_{instructor.Id}";
        var adminKey = $"birthday_24h_{client.Id}_{birthday:yyyy-MM-dd}_admin";

        Assert.Equal(1, await db.ActionItems.CountAsync(a => a.DeduplicationKey == instructorKey));
        Assert.Equal(1, await db.ActionItems.CountAsync(a => a.DeduplicationKey == adminKey));
    }

    // ─── EnsureTryoutFollowupActionItemAsync ─────────────────────────────────

    [Fact]
    public async Task EnsureTryoutFollowup_creates_cs_tryout_item_with_correct_key_and_description()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, _) = await SeedClassAndModelAsync(db);
        var client = await SeedClientAsync(db);
        var dedupKey = $"tryout_followup_{client.Id}_{cls.Id}";

        await svc.EnsureTryoutFollowupActionItemAsync(client.Id, cls.Id);

        var item = await db.ActionItems.SingleAsync(a => a.DeduplicationKey == dedupKey);
        Assert.Equal(ActionItemType.TryoutFollowup, item.Type);
        Assert.Equal(ActionItemStatus.Open, item.Status);
        Assert.Equal(AppRoles.CustomerService, item.AssignedToRole);
        Assert.Null(item.AssignedToUserId);
        Assert.Equal(client.Id, item.RelatedEntityId);
        Assert.Contains(client.Name, item.Description);
        Assert.Contains(cls.Name, item.Description);
    }

    [Fact]
    public async Task EnsureTryoutFollowup_idempotent_second_call_no_duplicate()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ActionItemService>();

        var (cls, _) = await SeedClassAndModelAsync(db);
        var client = await SeedClientAsync(db);
        var dedupKey = $"tryout_followup_{client.Id}_{cls.Id}";

        await svc.EnsureTryoutFollowupActionItemAsync(client.Id, cls.Id);
        await svc.EnsureTryoutFollowupActionItemAsync(client.Id, cls.Id);

        var count = await db.ActionItems.CountAsync(a => a.DeduplicationKey == dedupKey);
        Assert.Equal(1, count);
    }
}
