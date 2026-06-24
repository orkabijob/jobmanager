using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class LogisticsServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public LogisticsServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<AppUser> SeedInstructorAsync(IServiceProvider sp)
    {
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var email = $"instructor-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, "Test@1234!");
        if (!result.Succeeded)
            throw new InvalidOperationException("Seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private static async Task<(School school, AcademicYear year, Class cls)> SeedSchoolYearClassAsync(AppDbContext db)
    {
        var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "תל אביב" };
        var year = new AcademicYear
        {
            Label = $"תשפ-{Guid.NewGuid():N}"[..10],
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
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        return (school, year, cls);
    }

    private static async Task<Model> SeedModelAsync(AppDbContext db)
    {
        var model = new Model
        {
            Name = $"מודל-{Guid.NewGuid():N}",
            ExpectedLessonsToComplete = 10
        };
        db.Models.Add(model);
        await db.SaveChangesAsync();
        return model;
    }

    private static async Task<Syllabus> SeedSyllabusWithModelAsync(AppDbContext db, int modelId)
    {
        var syllabus = new Syllabus
        {
            Name = $"סילבוס-{Guid.NewGuid():N}",
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30),
            Status = EntityStatus.Active
        };
        db.Syllabi.Add(syllabus);
        await db.SaveChangesAsync();

        db.SyllabusModels.Add(new SyllabusModel { SyllabusId = syllabus.Id, ModelId = modelId, OrderIndex = 1 });
        await db.SaveChangesAsync();

        return syllabus;
    }

    private static async Task<LessonLog> SeedLessonLogAsync(AppDbContext db, int classId, int modelId, int instructorId)
    {
        var template = new ShiftTemplate
        {
            ClassId = classId,
            DefaultInstructorId = instructorId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            AcademicYearId = (await db.AcademicYears.FirstAsync()).Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var instance = new ShiftInstance
        {
            TemplateId = template.Id,
            Date = today,
            ActualInstructorId = instructorId,
            Status = ShiftInstanceStatus.Scheduled
        };
        db.ShiftInstances.Add(instance);
        await db.SaveChangesAsync();

        var model = await db.Models.FindAsync(modelId)!;
        var log = new LessonLog
        {
            ShiftInstanceId = instance.Id,
            ModelId = modelId,
            Status = LessonLogStatus.InProgress,
            ExpectedLessonsSnapshot = model!.ExpectedLessonsToComplete
        };
        db.LessonLogs.Add(log);
        await db.SaveChangesAsync();

        return log;
    }

    // ── SeedOrdersForClassAsync ───────────────────────────────────────────────

    [Fact]
    public async Task SeedOrders_creates_one_pending_order_for_class_model_with_lesson_log()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var syllabus = await SeedSyllabusWithModelAsync(db, model.Id);
        cls.SyllabusId = syllabus.Id;
        await db.SaveChangesAsync();

        var instructor = await SeedInstructorAsync(sp);
        await SeedLessonLogAsync(db, cls.Id, model.Id, instructor.Id);

        var created = await svc.SeedOrdersForClassAsync(cls.Id);

        Assert.Single(created);
        Assert.Equal(LogisticsOrderStatus.Pending, created[0].Status);
        Assert.Equal(cls.Id, created[0].ClassId);
        Assert.Equal(model.Id, created[0].ModelId);
        Assert.Equal(1, created[0].Quantity);
    }

    [Fact]
    public async Task SeedOrders_second_call_creates_no_duplicate()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var syllabus = await SeedSyllabusWithModelAsync(db, model.Id);
        cls.SyllabusId = syllabus.Id;
        await db.SaveChangesAsync();

        var instructor = await SeedInstructorAsync(sp);
        await SeedLessonLogAsync(db, cls.Id, model.Id, instructor.Id);

        await svc.SeedOrdersForClassAsync(cls.Id);
        var second = await svc.SeedOrdersForClassAsync(cls.Id);

        Assert.Empty(second);
        var total = await db.LogisticsOrders.CountAsync(o => o.ClassId == cls.Id && o.ModelId == model.Id);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task SeedOrders_model_without_lesson_log_is_not_seeded()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var syllabus = await SeedSyllabusWithModelAsync(db, model.Id);
        cls.SyllabusId = syllabus.Id;
        await db.SaveChangesAsync();

        // No LessonLog seeded for this class+model.

        var created = await svc.SeedOrdersForClassAsync(cls.Id);

        Assert.Empty(created);
    }

    [Fact]
    public async Task SeedOrders_class_without_syllabus_returns_empty()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        // SyllabusId is null by default.

        var created = await svc.SeedOrdersForClassAsync(cls.Id);

        Assert.Empty(created);
    }

    [Fact]
    public async Task SeedOrders_seeds_even_when_shift_template_archived()
    {
        // Arrange: build the full chain (class → syllabus → model → ShiftTemplate →
        // ShiftInstance → LessonLog), then ARCHIVE the ShiftTemplate.
        // Without IgnoreQueryFilters on the LessonLog check, the archived template
        // causes the LessonLog to be silently excluded and zero orders are created.
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var syllabus = await SeedSyllabusWithModelAsync(db, model.Id);
        cls.SyllabusId = syllabus.Id;
        await db.SaveChangesAsync();

        var instructor = await SeedInstructorAsync(sp);
        // SeedLessonLogAsync creates an Active ShiftTemplate internally.
        await SeedLessonLogAsync(db, cls.Id, model.Id, instructor.Id);

        // Archive the ShiftTemplate so the global query filter (Status == Active) would
        // hide it — and, by navigational inference, hide the LessonLog too.
        var template = await db.ShiftTemplates.IgnoreQueryFilters()
            .FirstAsync(t => t.ClassId == cls.Id);
        template.Status = EntityStatus.Archived;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Act
        var created = await svc.SeedOrdersForClassAsync(cls.Id);

        // Assert: exactly one Pending order for (cls, model) — proving the archived
        // template no longer hides the lesson log.
        Assert.Single(created);
        Assert.Equal(LogisticsOrderStatus.Pending, created[0].Status);
        Assert.Equal(cls.Id, created[0].ClassId);
        Assert.Equal(model.Id, created[0].ModelId);
    }

    // ── MarkPackedAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task MarkPacked_transitions_Pending_to_Packed()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 1, Status = LogisticsOrderStatus.Pending };
        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();

        await svc.MarkPackedAsync(order.Id, logisticsUserId: 0);

        db.ChangeTracker.Clear();
        var loaded = await db.LogisticsOrders.FindAsync(order.Id);
        Assert.Equal(LogisticsOrderStatus.Packed, loaded!.Status);
    }

    [Fact]
    public async Task MarkPacked_on_non_Pending_throws_InvalidOperationException()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 1, Status = LogisticsOrderStatus.Accepted };
        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.MarkPackedAsync(order.Id, logisticsUserId: 0));
    }

    // ── MarkAcceptedAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task MarkAccepted_transitions_Packed_to_Accepted_and_sets_DeliveredAt()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 1, Status = LogisticsOrderStatus.Packed };
        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();

        var before = DateTime.UtcNow;
        await svc.MarkAcceptedAsync(order.Id, instructorUserId: 0);

        db.ChangeTracker.Clear();
        var loaded = await db.LogisticsOrders.FindAsync(order.Id);
        Assert.Equal(LogisticsOrderStatus.Accepted, loaded!.Status);
        Assert.NotNull(loaded.DeliveredAt);
        Assert.True(loaded.DeliveredAt >= before);
    }

    [Fact]
    public async Task MarkAccepted_on_non_Packed_throws_InvalidOperationException()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 1, Status = LogisticsOrderStatus.Pending };
        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.MarkAcceptedAsync(order.Id, instructorUserId: 0));
    }

    // ── MarkDisputedAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task MarkDisputed_transitions_Packed_to_Disputed_sets_notes_and_creates_action_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 1, Status = LogisticsOrderStatus.Packed };
        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();

        await svc.MarkDisputedAsync(order.Id, instructorUserId: 0, disputeNotes: "חסר ציוד");

        db.ChangeTracker.Clear();
        var loaded = await db.LogisticsOrders.FindAsync(order.Id);
        Assert.Equal(LogisticsOrderStatus.Disputed, loaded!.Status);
        Assert.Equal("חסר ציוד", loaded.DisputeNotes);

        var dedupKey = $"dispute_{order.Id}";
        var actionItem = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey);
        Assert.NotNull(actionItem);
        Assert.Equal(ActionItemStatus.Open, actionItem.Status);
        Assert.Equal(ActionItemType.Dispute, actionItem.Type);
        Assert.Equal(AppRoles.Admin, actionItem.AssignedToRole);
    }

    [Fact]
    public async Task MarkDisputed_on_non_Packed_throws_InvalidOperationException()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 1, Status = LogisticsOrderStatus.Pending };
        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.MarkDisputedAsync(order.Id, instructorUserId: 0, disputeNotes: "test"));
    }

    // ── ListOrdersAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListOrders_filters_by_status()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model1 = await SeedModelAsync(db);
        var model2 = await SeedModelAsync(db);

        db.LogisticsOrders.AddRange(
            new LogisticsOrder { ClassId = cls.Id, ModelId = model1.Id, Quantity = 1, Status = LogisticsOrderStatus.Pending },
            new LogisticsOrder { ClassId = cls.Id, ModelId = model2.Id, Quantity = 1, Status = LogisticsOrderStatus.Packed }
        );
        await db.SaveChangesAsync();

        var pendingOrders = await svc.ListOrdersAsync(LogisticsOrderStatus.Pending, null);

        Assert.All(pendingOrders.Where(o => o.ClassId == cls.Id), o => Assert.Equal(LogisticsOrderStatus.Pending, o.Status));
    }

    [Fact]
    public async Task ListOrders_filters_by_classId()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls1) = await SeedSchoolYearClassAsync(db);
        var (_, _, cls2) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);

        db.LogisticsOrders.AddRange(
            new LogisticsOrder { ClassId = cls1.Id, ModelId = model.Id, Quantity = 1, Status = LogisticsOrderStatus.Pending },
            new LogisticsOrder { ClassId = cls2.Id, ModelId = model.Id, Quantity = 1, Status = LogisticsOrderStatus.Pending }
        );
        await db.SaveChangesAsync();

        var cls1Orders = await svc.ListOrdersAsync(null, cls1.Id);

        Assert.All(cls1Orders, o => Assert.Equal(cls1.Id, o.ClassId));
        Assert.DoesNotContain(cls1Orders, o => o.ClassId == cls2.Id);
    }

    [Fact]
    public async Task ListOrders_includes_Class_and_Model_navigation_properties()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<SupplyPacingService>();

        var (_, _, cls) = await SeedSchoolYearClassAsync(db);
        var model = await SeedModelAsync(db);
        db.LogisticsOrders.Add(new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 1 });
        await db.SaveChangesAsync();

        var orders = await svc.ListOrdersAsync(null, cls.Id);

        Assert.NotEmpty(orders);
        Assert.All(orders, o =>
        {
            Assert.NotNull(o.Class);
            Assert.NotNull(o.Model);
        });
    }
}
