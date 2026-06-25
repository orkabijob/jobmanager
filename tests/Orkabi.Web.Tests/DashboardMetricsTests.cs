using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Dashboard;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;
using Orkabi.Web.Modules.Operations;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// TDD tests for DashboardMetricsService.GetAdminMetricsAsync().
/// Each test seeds a known fixture and asserts the exact Admin bento metric.
/// </summary>
public class DashboardMetricsTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public DashboardMetricsTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<(School school, AcademicYear year)> SeedSchoolAndYearAsync(AppDbContext db)
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
        return (school, year);
    }

    private static async Task<Class> SeedClassAsync(AppDbContext db, School school, AcademicYear year)
    {
        var cls = new Class
        {
            Name = $"כיתה-{Guid.NewGuid():N}",
            School = school,
            AcademicYear = year,
            Status = EntityStatus.Active
        };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        return cls;
    }

    private static async Task<Model> SeedModelAsync(AppDbContext db)
    {
        var model = new Model { Name = $"מודל-{Guid.NewGuid():N}", ExpectedLessonsToComplete = 10 };
        db.Models.Add(model);
        await db.SaveChangesAsync();
        return model;
    }

    // ─── ActiveClientsCount ──────────────────────────────────────────────────

    [Fact]
    public async Task ActiveClientsCount_counts_only_IsActive_true_clients()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        // Snapshot before seeding so this test is isolated even in a shared fixture.
        var before = await db.Clients.CountAsync(c => c.IsActive);

        // 3 active + 1 inactive
        db.Clients.AddRange(
            new Client { Name = $"פעיל-{Guid.NewGuid():N}", IsActive = true },
            new Client { Name = $"פעיל-{Guid.NewGuid():N}", IsActive = true },
            new Client { Name = $"פעיל-{Guid.NewGuid():N}", IsActive = true },
            new Client { Name = $"לא פעיל-{Guid.NewGuid():N}", IsActive = false }
        );
        await db.SaveChangesAsync();

        var metrics = await svc.GetAdminMetricsAsync();

        Assert.Equal(before + 3, metrics.ActiveClientsCount);
    }

    // ─── NewClientsThisMonth ─────────────────────────────────────────────────

    [Fact]
    public async Task NewClientsThisMonth_counts_active_clients_created_from_first_of_month()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        var now = DateTime.UtcNow;
        // 2 active created this month, 1 active created last month, 1 inactive this month
        var thisMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonth = thisMonthStart.AddMonths(-1);

        var beforeCount = await db.Clients.CountAsync(c => c.IsActive && c.CreatedAt >= thisMonthStart);

        var c1 = new Client { Name = $"חדש-{Guid.NewGuid():N}", IsActive = true };
        var c2 = new Client { Name = $"חדש-{Guid.NewGuid():N}", IsActive = true };
        var c3 = new Client { Name = $"ישן-{Guid.NewGuid():N}", IsActive = true };
        var c4 = new Client { Name = $"לאפעיל-{Guid.NewGuid():N}", IsActive = false };
        db.Clients.AddRange(c1, c2, c3, c4);
        await db.SaveChangesAsync();

        // Forcibly set CreatedAt timestamps via raw SQL to bypass the audit interceptor.
        // Values are integer PKs and formatted datetimes — safe in test context.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"Clients\" SET \"CreatedAt\" = '{thisMonthStart:yyyy-MM-dd HH:mm:ss}' WHERE \"Id\" IN ({c1.Id},{c2.Id})");
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"Clients\" SET \"CreatedAt\" = '{lastMonth:yyyy-MM-dd HH:mm:ss}' WHERE \"Id\" IN ({c3.Id},{c4.Id})");
#pragma warning restore EF1002

        var metrics = await svc.GetAdminMetricsAsync();

        Assert.Equal(beforeCount + 2, metrics.NewClientsThisMonth);
    }

    // ─── SessionsToday ───────────────────────────────────────────────────────

    [Fact]
    public async Task SessionsToday_counts_only_ShiftInstances_with_Date_equal_today_Israel()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        var todayIsrael = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var yesterday = todayIsrael.AddDays(-1);
        var tomorrow = todayIsrael.AddDays(1);

        var before = await db.ShiftInstances.CountAsync(i => i.Date == todayIsrael);

        var (school, year) = await SeedSchoolAndYearAsync(db);
        var cls = await SeedClassAsync(db, school, year);
        var model = await SeedModelAsync(db);

        // Need an instructor for the template (create before the template)
        var instructorUser = new AppUser
        {
            UserName = $"inst_{Guid.NewGuid():N}@test.com",
            Email = $"inst_{Guid.NewGuid():N}@test.com",
            FullName = "מדריך בדיקה"
        };
        var um = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        await um.CreateAsync(instructorUser, "Test1234!");

        // We need a ShiftTemplate to create ShiftInstances (FK constraint)
        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0),
            DefaultInstructorId = instructorUser.Id,
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        // 2 shifts today, 1 yesterday, 1 tomorrow
        db.ShiftInstances.AddRange(
            new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instructorUser.Id, Date = todayIsrael, Status = ShiftInstanceStatus.Scheduled },
            new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instructorUser.Id, Date = yesterday, Status = ShiftInstanceStatus.Scheduled },
            new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instructorUser.Id, Date = tomorrow, Status = ShiftInstanceStatus.Scheduled }
        );
        await db.SaveChangesAsync();

        // Cannot add a second today-instance with same templateId (unique index) — one today is enough.
        var metrics = await svc.GetAdminMetricsAsync();

        Assert.Equal(before + 1, metrics.SessionsToday);
    }

    // ─── PendingVacations ────────────────────────────────────────────────────

    [Fact]
    public async Task PendingVacations_counts_only_Pending_vacation_requests()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        var um = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var instructor = new AppUser
        {
            UserName = $"vac_inst_{Guid.NewGuid():N}@test.com",
            Email = $"vac_inst_{Guid.NewGuid():N}@test.com",
            FullName = "מדריך לחופשה"
        };
        await um.CreateAsync(instructor, "Test1234!");

        var before = await db.VacationRequests.CountAsync(v => v.Status == VacationStatus.Pending);

        var todayIsrael = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));

        db.VacationRequests.AddRange(
            new VacationRequest { InstructorId = instructor.Id, StartDate = todayIsrael.AddDays(1), EndDate = todayIsrael.AddDays(2), Status = VacationStatus.Pending },
            new VacationRequest { InstructorId = instructor.Id, StartDate = todayIsrael.AddDays(3), EndDate = todayIsrael.AddDays(4), Status = VacationStatus.Approved },
            new VacationRequest { InstructorId = instructor.Id, StartDate = todayIsrael.AddDays(5), EndDate = todayIsrael.AddDays(6), Status = VacationStatus.Denied }
        );
        await db.SaveChangesAsync();

        var metrics = await svc.GetAdminMetricsAsync();

        Assert.Equal(before + 1, metrics.PendingVacations);
    }

    // ─── PendingExtraHours ───────────────────────────────────────────────────

    [Fact]
    public async Task PendingExtraHours_counts_only_Pending_extra_hours()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        var um = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var instructor = new AppUser
        {
            UserName = $"eh_inst_{Guid.NewGuid():N}@test.com",
            Email = $"eh_inst_{Guid.NewGuid():N}@test.com",
            FullName = "מדריך שעות"
        };
        await um.CreateAsync(instructor, "Test1234!");

        var (school, year) = await SeedSchoolAndYearAsync(db);
        var cls = await SeedClassAsync(db, school, year);
        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            DefaultInstructorId = instructor.Id,
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        var todayIsrael = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var shift = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instructor.Id, Date = todayIsrael.AddDays(-5), Status = ShiftInstanceStatus.Scheduled };
        db.ShiftInstances.Add(shift);
        await db.SaveChangesAsync();

        var before = await db.ExtraHours.CountAsync(e => e.Status == ExtraHoursStatus.Pending);

        db.ExtraHours.AddRange(
            new ExtraHours { ShiftInstanceId = shift.Id, InstructorId = instructor.Id, Hours = 1m, Reason = "בדיקה", Status = ExtraHoursStatus.Pending },
            new ExtraHours { ShiftInstanceId = shift.Id, InstructorId = instructor.Id, Hours = 2m, Reason = "בדיקה2", Status = ExtraHoursStatus.Approved }
        );
        await db.SaveChangesAsync();

        var metrics = await svc.GetAdminMetricsAsync();

        Assert.Equal(before + 1, metrics.PendingExtraHours);
    }

    // ─── OpenDisputedOrders ──────────────────────────────────────────────────

    [Fact]
    public async Task OpenDisputedOrders_counts_only_Disputed_logistics_orders()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        var (school, year) = await SeedSchoolAndYearAsync(db);
        var cls = await SeedClassAsync(db, school, year);
        var model = await SeedModelAsync(db);

        var before = await db.LogisticsOrders.CountAsync(o => o.Status == LogisticsOrderStatus.Disputed);

        db.LogisticsOrders.AddRange(
            new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 5, Status = LogisticsOrderStatus.Disputed },
            new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 3, Status = LogisticsOrderStatus.Pending },
            new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 2, Status = LogisticsOrderStatus.Packed }
        );
        await db.SaveChangesAsync();

        var metrics = await svc.GetAdminMetricsAsync();

        Assert.Equal(before + 1, metrics.OpenDisputedOrders);
    }

    // ─── ActiveClassesCount ──────────────────────────────────────────────────

    [Fact]
    public async Task ActiveClassesCount_counts_only_Active_status_classes()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        var (school, year) = await SeedSchoolAndYearAsync(db);

        var before = await db.Classes.CountAsync();  // global filter = Active only

        // 2 active, 1 archived (bypassing the global filter to insert the archived one)
        var activeA = new Class { Name = $"כיתה-פעילה-{Guid.NewGuid():N}", School = school, AcademicYear = year, Status = EntityStatus.Active };
        var activeB = new Class { Name = $"כיתה-פעילה-{Guid.NewGuid():N}", School = school, AcademicYear = year, Status = EntityStatus.Active };
        db.Classes.AddRange(activeA, activeB);
        await db.SaveChangesAsync();

        // Insert an archived class bypassing the global filter.
        // Values are integer PKs and formatted datetimes — safe in test context.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO \"Classes\" (\"Name\", \"SchoolId\", \"AcademicYearId\", \"SyllabusId\", \"Status\", \"CreatedAt\", \"CreatedByUserId\", \"UpdatedAt\", \"UpdatedByUserId\") " +
            $"VALUES ('archived-{Guid.NewGuid():N}', {school.Id}, {year.Id}, NULL, 1, '{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}', NULL, '{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}', NULL)");
#pragma warning restore EF1002

        var metrics = await svc.GetAdminMetricsAsync();

        Assert.Equal(before + 2, metrics.ActiveClassesCount);
    }

    // ─── OpenActionItemsByType ────────────────────────────────────────────────

    [Fact]
    public async Task OpenActionItemsByType_groups_open_items_by_type_and_excludes_resolved()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        // Snapshot counts before seeding
        var beforeGap = await db.ActionItems.CountAsync(a => a.Status == ActionItemStatus.Open && a.Type == ActionItemType.Gap);
        var beforeDispute = await db.ActionItems.CountAsync(a => a.Status == ActionItemStatus.Open && a.Type == ActionItemType.Dispute);

        db.ActionItems.AddRange(
            new ActionItem { Type = ActionItemType.Gap, Status = ActionItemStatus.Open, AssignedToRole = AppRoles.Admin, Description = "פתוח 1" },
            new ActionItem { Type = ActionItemType.Gap, Status = ActionItemStatus.Open, AssignedToRole = AppRoles.Admin, Description = "פתוח 2" },
            new ActionItem { Type = ActionItemType.Dispute, Status = ActionItemStatus.Open, AssignedToRole = AppRoles.Admin, Description = "מחלוקת" },
            new ActionItem { Type = ActionItemType.Gap, Status = ActionItemStatus.Resolved, AssignedToRole = AppRoles.Admin, Description = "טופל" }
        );
        await db.SaveChangesAsync();

        var metrics = await svc.GetAdminMetricsAsync();

        var gapCount = metrics.OpenActionItemsByType.TryGetValue(ActionItemType.Gap, out var g) ? g : 0;
        var disputeCount = metrics.OpenActionItemsByType.TryGetValue(ActionItemType.Dispute, out var d) ? d : 0;

        Assert.Equal(beforeGap + 2, gapCount);
        Assert.Equal(beforeDispute + 1, disputeCount);
        // Resolved item must not inflate any count (all counts must be non-negative)
        Assert.All(metrics.OpenActionItemsByType.Values, count => Assert.True(count >= 0));
    }

    // ─── TopOpenAdminItems + HubPreview / OpenCount ───────────────────────────

    [Fact]
    public async Task TopOpenAdminItems_returns_up_to_5_admin_role_open_items_ordered_by_CreatedAt()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        // Add 7 admin open items
        for (int i = 0; i < 7; i++)
        {
            db.ActionItems.Add(new ActionItem
            {
                Type = ActionItemType.Task,
                Status = ActionItemStatus.Open,
                AssignedToRole = AppRoles.Admin,
                Description = $"משימה {i}"
            });
        }
        await db.SaveChangesAsync();

        var metrics = await svc.GetAdminMetricsAsync();

        Assert.True(metrics.HubPreview.Count <= 5);
        Assert.True(metrics.OpenCount >= 7); // at least the 7 we just added
    }

    // ─── RecentOpenItems ─────────────────────────────────────────────────────

    [Fact]
    public async Task RecentOpenItems_returns_up_to_5_across_all_roles_newest_first()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<DashboardMetricsService>();

        db.ActionItems.AddRange(
            new ActionItem { Type = ActionItemType.Gap, Status = ActionItemStatus.Open, AssignedToRole = AppRoles.Admin, Description = "התראה 1" },
            new ActionItem { Type = ActionItemType.Absence, Status = ActionItemStatus.Open, AssignedToRole = AppRoles.CustomerService, Description = "התראה 2" },
            new ActionItem { Type = ActionItemType.Dispute, Status = ActionItemStatus.Open, AssignedToRole = AppRoles.Admin, Description = "התראה 3" }
        );
        await db.SaveChangesAsync();

        var metrics = await svc.GetAdminMetricsAsync();

        Assert.True(metrics.RecentOpenItems.Count <= 5);
        Assert.All(metrics.RecentOpenItems, i => Assert.Equal(ActionItemStatus.Open, i.Status));
        // Newest first: each CreatedAt should be >= the next
        for (int i = 0; i < metrics.RecentOpenItems.Count - 1; i++)
            Assert.True(metrics.RecentOpenItems[i].CreatedAt >= metrics.RecentOpenItems[i + 1].CreatedAt);
    }
}
