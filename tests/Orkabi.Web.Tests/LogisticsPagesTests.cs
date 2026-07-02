using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class LogisticsPagesTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public LogisticsPagesTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client, int userId)>
        CreateUserClientAsync(SqliteFixture sqlite, string role, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"{role.ToLower()}.log{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, role);
                existing = u;
            }
            var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
            return (factory, client, existing.Id);
        }
    }

    // ── Authz: anonymous ──────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_redirected_from_logistics_orders()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var resp = await client.GetAsync("/Logistics/Orders");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Anonymous_redirected_from_logistics_myorders()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var resp = await client.GetAsync("/Logistics/MyOrders");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    // ── R10: an approved substitute (actual, not template, instructor) sees the class kit ──

    [Fact]
    public async Task Substitute_sees_the_class_kit_in_my_orders()
    {
        var (factory, subClient, subId) = await CreateUserClientAsync(_sqlite, AppRoles.Instructor, "_sub_kit");
        string className;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var aEmail = $"a.instr.{Guid.NewGuid():N}"[..28] + "@t.t";
            var a = new AppUser { UserName = aEmail, Email = aEmail };
            await um.CreateAsync(a, "Passw0rd!");   // A owns the template; the sub does not

            var school = new School { Name = $"Sch-{Guid.NewGuid():N}"[..18], City = "ת" };
            var year = new AcademicYear { Label = $"Y-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school); db.AcademicYears.Add(year);
            await db.SaveChangesAsync();
            className = $"C-sub-{Guid.NewGuid():N}"[..15];
            var cls = new Class { Name = className, SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"M-{Guid.NewGuid():N}"[..12] };
            db.Classes.Add(cls); db.Models.Add(model);
            await db.SaveChangesAsync();
            var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = a.Id, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();
            // the signed-in user is only the ACTUAL instructor on an instance (an approved substitution)
            db.ShiftInstances.Add(new ShiftInstance { TemplateId = template.Id, ActualInstructorId = subId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Status = ShiftInstanceStatus.Scheduled });
            db.LogisticsOrders.Add(new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 1, Status = LogisticsOrderStatus.Packed });
            await db.SaveChangesAsync();
        }

        var body = WebUtility.HtmlDecode(await (await subClient.GetAsync("/Logistics/MyOrders")).Content.ReadAsStringAsync());
        Assert.Contains(className, body);   // R10: the substitute who actually teaches sees the class's kit
        factory.Dispose();
    }

    // ── Authz: Instructor cannot access Logistics orders list ─────────────────

    [Fact]
    public async Task Instructor_denied_logistics_orders_page()
    {
        var (factory, client, _) = await CreateUserClientAsync(_sqlite, AppRoles.Instructor, "_403_orders");
        var resp = await client.GetAsync("/Logistics/Orders");
        Assert.True(
            resp.StatusCode == HttpStatusCode.Redirect ||
            resp.StatusCode == HttpStatusCode.Forbidden,
            $"Expected redirect or 403, got {resp.StatusCode}");
        if (resp.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("AccessDenied", resp.Headers.Location?.ToString() ?? "");
        factory.Dispose();
    }

    // ── Authz: Instructor cannot mark Packed via handler ──────────────────────

    [Fact]
    public async Task Logistics_repacks_disputed_order_back_to_pending()
    {
        var (factory, client, _) = await CreateUserClientAsync(_sqlite, AppRoles.Logistics, "_repack");

        int orderId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var school = new School { Name = $"Sch-rp-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-rp-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school); db.AcademicYears.Add(year);
            await db.SaveChangesAsync();
            var cls = new Class { Name = $"C-rp-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"Mdl-rp-{Guid.NewGuid():N}"[..15] };
            db.Models.Add(model);
            await db.SaveChangesAsync();
            var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 5, Status = LogisticsOrderStatus.Disputed, DisputeNotes = "חסר ציוד" };
            db.LogisticsOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        var getResp = await client.GetAsync("/Logistics/Orders");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await client.PostAsync($"/Logistics/Orders?handler=Repack&id={orderId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await postResp.Content.ReadAsStringAsync());
        Assert.Contains("ממתין", body);          // re-rendered row now shows Pending
        Assert.DoesNotContain("במחלוקת", body);

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var rec = await db.LogisticsOrders.FindAsync(orderId);
            Assert.Equal(LogisticsOrderStatus.Pending, rec!.Status);
            Assert.Null(rec.DisputeNotes);
        }

        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_cannot_mark_packed_via_handler()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateUserClientAsync(_sqlite, AppRoles.Instructor, "_403_pack");
        var (logFactory, _, _) = await CreateUserClientAsync(_sqlite, AppRoles.Logistics, "_403_pack_log");

        int orderId;
        using (var s = logFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var school = new School { Name = $"Sch-lp-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-lp-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school); db.AcademicYears.Add(year);
            await db.SaveChangesAsync();
            var cls = new Class { Name = $"C-lp-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            // Need a model
            var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"Mdl-lp-{Guid.NewGuid():N}"[..15] };
            db.Models.Add(model);
            await db.SaveChangesAsync();
            var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 10, Status = LogisticsOrderStatus.Pending };
            db.LogisticsOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        // GET MyOrders page as instructor to get a valid antiforgery token
        var getResp = await instrClient.GetAsync("/Logistics/MyOrders");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // Instructor POSTs to Pack handler — should be forbidden
        var postResp = await instrClient.PostAsync($"/Logistics/Orders?handler=Pack&id={orderId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.True(
            postResp.StatusCode == HttpStatusCode.Redirect ||
            postResp.StatusCode == HttpStatusCode.Forbidden,
            $"Expected redirect/403, got {postResp.StatusCode}");
        if (postResp.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("AccessDenied", postResp.Headers.Location?.ToString() ?? "");

        // Order must still be Pending
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.LogisticsOrders.FindAsync(orderId);
            Assert.Equal(LogisticsOrderStatus.Pending, record!.Status);
        }

        instrFactory.Dispose();
        logFactory.Dispose();
    }

    // ── Logistics user can see orders page with Hebrew title ──────────────────

    [Fact]
    public async Task Logistics_user_sees_orders_page_with_hebrew_title()
    {
        var (factory, client, _) = await CreateUserClientAsync(_sqlite, AppRoles.Logistics, "_get_orders");
        var resp = await client.GetAsync("/Logistics/Orders");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains("לוגיסטיקה", body);
        Assert.Contains("הזמנות לוגיסטיקה", body);
        factory.Dispose();
    }

    // ── Logistics marks Pending → Packed (row swaps to נארז) ─────────────────

    [Fact]
    public async Task Logistics_marks_packed_row_swaps_to_packed()
    {
        var (factory, client, _) = await CreateUserClientAsync(_sqlite, AppRoles.Logistics, "_pack_row");

        int orderId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var school = new School { Name = $"Sch-pk-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-pk-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school); db.AcademicYears.Add(year);
            await db.SaveChangesAsync();
            var cls = new Class { Name = $"C-pk-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"Mdl-pk-{Guid.NewGuid():N}"[..15] };
            db.Models.Add(model);
            await db.SaveChangesAsync();
            var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 5, Status = LogisticsOrderStatus.Pending };
            db.LogisticsOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        var getResp = await client.GetAsync("/Logistics/Orders");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await client.PostAsync($"/Logistics/Orders?handler=Pack&id={orderId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await postResp.Content.ReadAsStringAsync());
        Assert.Contains("נארז", body);
        Assert.DoesNotContain("ממתין", body);

        // Confirm DB status changed
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.LogisticsOrders.FindAsync(orderId);
            Assert.Equal(LogisticsOrderStatus.Packed, record!.Status);
        }

        factory.Dispose();
    }

    // ── Admin seeds (generate) → orders appear in list ───────────────────────

    [Fact]
    public async Task Admin_generates_orders_appear_in_list()
    {
        var (factory, client, _) = await CreateUserClientAsync(_sqlite, AppRoles.Admin, "_gen_orders");

        int classId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            // Create a user to be the instructor for shifts
            var instrEmail = $"gen.instr.{Guid.NewGuid():N}"[..30] + "@t.t";
            var instrUser = new AppUser { UserName = instrEmail, Email = instrEmail };
            await um.CreateAsync(instrUser, "Passw0rd!");

            var school = new School { Name = $"Sch-gen-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-gen-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school); db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class { Name = $"C-gen-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            classId = cls.Id;

            // Create syllabus + model
            var syllabus = new Orkabi.Web.Modules.Curriculum.Syllabus { Name = $"Syl-gen-{Guid.NewGuid():N}"[..15] };
            db.Syllabi.Add(syllabus);
            await db.SaveChangesAsync();

            var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"Mdl-gen-{Guid.NewGuid():N}"[..15] };
            db.Models.Add(model);
            await db.SaveChangesAsync();

            var sm = new Orkabi.Web.Modules.Curriculum.SyllabusModel { SyllabusId = syllabus.Id, ModelId = model.Id, OrderIndex = 1 };
            db.SyllabusModels.Add(sm);
            cls.SyllabusId = syllabus.Id;
            await db.SaveChangesAsync();

            // Create a shift + lesson log so SeedOrdersForClassAsync will create an order
            var template = new ShiftTemplate
            {
                ClassId = cls.Id,
                DefaultInstructorId = instrUser.Id,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                AcademicYearId = year.Id,
                Status = EntityStatus.Active
            };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();

            var shift = new ShiftInstance
            {
                TemplateId = template.Id,
                ActualInstructorId = instrUser.Id,
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                Status = ShiftInstanceStatus.Scheduled
            };
            db.ShiftInstances.Add(shift);
            await db.SaveChangesAsync();

            var lessonLog = new Orkabi.Web.Modules.Scheduling.LessonLog
            {
                ShiftInstanceId = shift.Id,
                ModelId = model.Id
            };
            db.LessonLogs.Add(lessonLog);
            await db.SaveChangesAsync();
        }

        var getResp = await client.GetAsync("/Logistics/Orders");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await client.PostAsync($"/Logistics/Orders?handler=Generate&classId={classId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await postResp.Content.ReadAsStringAsync());
        // Should contain Pending status (newly created order)
        Assert.Contains("ממתין", body);

        factory.Dispose();
    }

    // ── Instructor disputes Packed order → status Disputed + ActionItem exists ──

    [Fact]
    public async Task Instructor_disputes_packed_order_creates_dispute_action_item()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateUserClientAsync(_sqlite, AppRoles.Instructor, "_dispute");

        int orderId;
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var school = new School { Name = $"Sch-dp-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-dp-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school); db.AcademicYears.Add(year);
            await db.SaveChangesAsync();
            var cls = new Class { Name = $"C-dp-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"Mdl-dp-{Guid.NewGuid():N}"[..15] };
            db.Models.Add(model);
            await db.SaveChangesAsync();
            // Link instructor to class via ShiftTemplate so the ownership check passes
            var template = new ShiftTemplate
            {
                ClassId = cls.Id,
                DefaultInstructorId = instrUserId,
                DayOfWeek = DayOfWeek.Tuesday,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                AcademicYearId = year.Id,
                Status = EntityStatus.Active
            };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();
            // Create a Packed order
            var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 24, Status = LogisticsOrderStatus.Packed };
            db.LogisticsOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        // GET MyOrders page to get antiforgery token
        var getResp = await instrClient.GetAsync("/Logistics/MyOrders");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // Instructor submits dispute
        var postResp = await instrClient.PostAsync($"/Logistics/MyOrders?handler=Dispute&id={orderId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["DisputeNotes"] = "חסרו 3 ערכות",
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await postResp.Content.ReadAsStringAsync());
        Assert.Contains("במחלוקת", body);

        // Confirm DB: order is Disputed
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.LogisticsOrders.FindAsync(orderId);
            Assert.Equal(LogisticsOrderStatus.Disputed, record!.Status);
            Assert.Equal("חסרו 3 ערכות", record.DisputeNotes);

            // Confirm dispute ActionItem exists with correct dedup key
            var actionItem = await db.ActionItems
                .FirstOrDefaultAsync(a => a.DeduplicationKey == $"dispute_{orderId}");
            Assert.NotNull(actionItem);
            Assert.Equal(ActionItemStatus.Open, actionItem!.Status);
        }

        instrFactory.Dispose();
    }

    // ── Instructor can accept a Packed order ──────────────────────────────────

    [Fact]
    public async Task Instructor_accepts_packed_order_card_swaps_to_accepted()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateUserClientAsync(_sqlite, AppRoles.Instructor, "_accept");

        int orderId;
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var school = new School { Name = $"Sch-ac-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-ac-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school); db.AcademicYears.Add(year);
            await db.SaveChangesAsync();
            var cls = new Class { Name = $"C-ac-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"Mdl-ac-{Guid.NewGuid():N}"[..15] };
            db.Models.Add(model);
            await db.SaveChangesAsync();
            // Link instructor to class via ShiftTemplate so the ownership check passes
            var template = new ShiftTemplate
            {
                ClassId = cls.Id,
                DefaultInstructorId = instrUserId,
                DayOfWeek = DayOfWeek.Wednesday,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                AcademicYearId = year.Id,
                Status = EntityStatus.Active
            };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();
            var order = new LogisticsOrder { ClassId = cls.Id, ModelId = model.Id, Quantity = 20, Status = LogisticsOrderStatus.Packed };
            db.LogisticsOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        var getResp = await instrClient.GetAsync("/Logistics/MyOrders");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await instrClient.PostAsync($"/Logistics/MyOrders?handler=Accept&id={orderId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await postResp.Content.ReadAsStringAsync());
        Assert.Contains("התקבל", body);

        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.LogisticsOrders.FindAsync(orderId);
            Assert.Equal(LogisticsOrderStatus.Accepted, record!.Status);
        }

        instrFactory.Dispose();
    }

    // ── Cross-class IDOR: instructor B cannot accept instructor A's order ─────

    [Fact]
    public async Task Instructor_cannot_accept_other_classes_order()
    {
        // Instructor A owns class X with a Packed order
        var (instrAFactory, _, instrAUserId) = await CreateUserClientAsync(_sqlite, AppRoles.Instructor, "_idor_ac_a");
        // Instructor B owns class Y — will attempt to act on class X's order
        var (instrBFactory, instrBClient, instrBUserId) = await CreateUserClientAsync(_sqlite, AppRoles.Instructor, "_idor_ac_b");

        int orderId;
        using (var s = instrAFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var school = new School { Name = $"Sch-ia-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-ia-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school); db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            // Class X — owned by instructor A
            var clsX = new Class { Name = $"Cx-ia-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(clsX);
            // Class Y — owned by instructor B
            var clsY = new Class { Name = $"Cy-ia-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(clsY);
            await db.SaveChangesAsync();

            var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"Mdl-ia-{Guid.NewGuid():N}"[..15] };
            db.Models.Add(model);
            await db.SaveChangesAsync();

            // ShiftTemplate linking A → class X
            db.ShiftTemplates.Add(new ShiftTemplate
            {
                ClassId = clsX.Id, DefaultInstructorId = instrAUserId, DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), AcademicYearId = year.Id, Status = EntityStatus.Active
            });
            // ShiftTemplate linking B → class Y (so B has some class of their own, but NOT class X)
            db.ShiftTemplates.Add(new ShiftTemplate
            {
                ClassId = clsY.Id, DefaultInstructorId = instrBUserId, DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(12, 0), AcademicYearId = year.Id, Status = EntityStatus.Active
            });
            await db.SaveChangesAsync();

            // Packed order belonging to class X
            var order = new LogisticsOrder { ClassId = clsX.Id, ModelId = model.Id, Quantity = 10, Status = LogisticsOrderStatus.Packed };
            db.LogisticsOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        // Sign in as instructor B and get an antiforgery token
        var getResp = await instrBClient.GetAsync("/Logistics/MyOrders");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // Instructor B attempts to Accept class X's order — must be denied
        var postResp = await instrBClient.PostAsync($"/Logistics/MyOrders?handler=Accept&id={orderId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.True(
            postResp.StatusCode == HttpStatusCode.Redirect ||
            postResp.StatusCode == HttpStatusCode.Forbidden,
            $"Expected redirect/403, got {postResp.StatusCode}");
        if (postResp.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("AccessDenied", postResp.Headers.Location?.ToString() ?? "");

        // Order must still be Packed and DeliveredAt must be null
        using (var s = instrBFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.LogisticsOrders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == orderId);
            Assert.NotNull(record);
            Assert.Equal(LogisticsOrderStatus.Packed, record!.Status);
            Assert.Null(record.DeliveredAt);
        }

        instrAFactory.Dispose();
        instrBFactory.Dispose();
    }

    // ── Cross-class IDOR: instructor B cannot dispute instructor A's order ────

    [Fact]
    public async Task Instructor_cannot_dispute_other_classes_order()
    {
        // Instructor A owns class X with a Packed order
        var (instrAFactory, _, instrAUserId) = await CreateUserClientAsync(_sqlite, AppRoles.Instructor, "_idor_dp_a");
        // Instructor B owns class Y — will attempt to act on class X's order
        var (instrBFactory, instrBClient, instrBUserId) = await CreateUserClientAsync(_sqlite, AppRoles.Instructor, "_idor_dp_b");

        int orderId;
        using (var s = instrAFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var school = new School { Name = $"Sch-id-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-id-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school); db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            // Class X — owned by instructor A
            var clsX = new Class { Name = $"Cx-id-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(clsX);
            // Class Y — owned by instructor B
            var clsY = new Class { Name = $"Cy-id-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(clsY);
            await db.SaveChangesAsync();

            var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"Mdl-id-{Guid.NewGuid():N}"[..15] };
            db.Models.Add(model);
            await db.SaveChangesAsync();

            // ShiftTemplate linking A → class X
            db.ShiftTemplates.Add(new ShiftTemplate
            {
                ClassId = clsX.Id, DefaultInstructorId = instrAUserId, DayOfWeek = DayOfWeek.Thursday,
                StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), AcademicYearId = year.Id, Status = EntityStatus.Active
            });
            // ShiftTemplate linking B → class Y (so B has some class of their own, but NOT class X)
            db.ShiftTemplates.Add(new ShiftTemplate
            {
                ClassId = clsY.Id, DefaultInstructorId = instrBUserId, DayOfWeek = DayOfWeek.Thursday,
                StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(12, 0), AcademicYearId = year.Id, Status = EntityStatus.Active
            });
            await db.SaveChangesAsync();

            // Packed order belonging to class X
            var order = new LogisticsOrder { ClassId = clsX.Id, ModelId = model.Id, Quantity = 15, Status = LogisticsOrderStatus.Packed };
            db.LogisticsOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        // Sign in as instructor B and get an antiforgery token
        var getResp = await instrBClient.GetAsync("/Logistics/MyOrders");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // Instructor B attempts to Dispute class X's order — must be denied
        var postResp = await instrBClient.PostAsync($"/Logistics/MyOrders?handler=Dispute&id={orderId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["DisputeNotes"] = "IDOR attempt",
                ["__RequestVerificationToken"] = token
            }));
        Assert.True(
            postResp.StatusCode == HttpStatusCode.Redirect ||
            postResp.StatusCode == HttpStatusCode.Forbidden,
            $"Expected redirect/403, got {postResp.StatusCode}");
        if (postResp.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("AccessDenied", postResp.Headers.Location?.ToString() ?? "");

        // Order must still be Packed AND no dispute ActionItem created
        using (var s = instrBFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.LogisticsOrders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == orderId);
            Assert.NotNull(record);
            Assert.Equal(LogisticsOrderStatus.Packed, record!.Status);

            var actionItem = await db.ActionItems
                .FirstOrDefaultAsync(a => a.DeduplicationKey == $"dispute_{orderId}");
            Assert.Null(actionItem);
        }

        instrAFactory.Dispose();
        instrBFactory.Dispose();
    }
}
