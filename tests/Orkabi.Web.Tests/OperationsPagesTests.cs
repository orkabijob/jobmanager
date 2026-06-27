using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Operations;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class OperationsPagesTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public OperationsPagesTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client, int userId)>
        CreateInstructorClientAsync(SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"instr.ops{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.Instructor);
                existing = u;
            }
            var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
            return (factory, client, existing.Id);
        }
    }

    private static async Task<(OrkabiAppFactory factory, HttpClient client, int userId)>
        CreateAdminClientAsync(SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"admin.ops{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.Admin);
                existing = u;
            }
            var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
            return (factory, client, existing.Id);
        }
    }

    [Fact]
    public async Task Anonymous_redirected_from_operations()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var resp = await client.GetAsync("/Operations");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Instructor_can_view_extrahours_page()
    {
        var (factory, client, _) = await CreateInstructorClientAsync(_sqlite, "_view_xh");
        var resp = await client.GetAsync("/Operations/ExtraHours");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains("דיווח שעות נוספות", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_submits_extrahours_appears_in_admin_pending()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_submit_xh");
        var (adminFactory, adminClient, _) = await CreateAdminClientAsync(_sqlite, "_approve_xh");

        // Seed a shift for the instructor
        int shiftId;
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var school = new School { Name = $"School-xh-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear
            {
                Label = $"Y-xh-{Guid.NewGuid():N}"[..10],
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30)
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class { Name = $"Cls-xh-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();

            var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instrUserId, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();

            var shift = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instrUserId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Status = ShiftInstanceStatus.Scheduled };
            db.ShiftInstances.Add(shift);
            await db.SaveChangesAsync();
            shiftId = shift.Id;
        }

        // Get the instructor page to obtain CSRF token
        var getResp = await instrClient.GetAsync("/Operations/ExtraHours");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // Submit extra hours
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.ShiftInstanceId"] = shiftId.ToString(),
            ["Input.Hours"] = "1.5",
            ["Input.Reason"] = "הכנת חומרים",
            ["__RequestVerificationToken"] = token
        });
        var postResp = await instrClient.PostAsync("/Operations/ExtraHours", form);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        // Admin sees it in pending list
        var adminResp = await adminClient.GetAsync("/Operations/ExtraHours");
        Assert.Equal(HttpStatusCode.OK, adminResp.StatusCode);
        var adminBody = System.Net.WebUtility.HtmlDecode(await adminResp.Content.ReadAsStringAsync());
        Assert.Contains("ממתין", adminBody);

        instrFactory.Dispose();
        adminFactory.Dispose();
    }

    [Fact]
    public async Task Admin_approves_extrahours_row_swaps_to_approved()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_approve_xh2");
        var (adminFactory, adminClient, adminUserId) = await CreateAdminClientAsync(_sqlite, "_approve_xh2_a");

        int xhId;
        using (var s = adminFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"Sch-axh-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-axh-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class { Name = $"C-axh-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();

            var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instrUserId, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();

            var shift = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instrUserId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), Status = ShiftInstanceStatus.Scheduled };
            db.ShiftInstances.Add(shift);
            await db.SaveChangesAsync();

            var xh = new Orkabi.Web.Modules.Operations.ExtraHours { ShiftInstanceId = shift.Id, InstructorId = instrUserId, Hours = 2m, Reason = "הארכה", Status = ExtraHoursStatus.Pending };
            db.ExtraHours.Add(xh);
            await db.SaveChangesAsync();
            xhId = xh.Id;
        }

        var getResp = await adminClient.GetAsync("/Operations/ExtraHours");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await adminClient.PostAsync($"/Operations/ExtraHours?handler=Approve&id={xhId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await postResp.Content.ReadAsStringAsync());
        Assert.Contains("אושר", body);
        Assert.DoesNotContain("ממתין", body);

        instrFactory.Dispose();
        adminFactory.Dispose();
    }

    [Fact]
    public async Task Admin_denies_extrahours_row_swaps_to_rejected()
    {
        var (instrFactory, _, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_deny_xh");
        var (adminFactory, adminClient, _) = await CreateAdminClientAsync(_sqlite, "_deny_xh_a");

        int xhId;
        using (var s = adminFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"Sch-dxh-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-dxh-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class { Name = $"C-dxh-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();

            var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instrUserId, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();

            var shift = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instrUserId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), Status = ShiftInstanceStatus.Scheduled };
            db.ShiftInstances.Add(shift);
            await db.SaveChangesAsync();

            var xh = new Orkabi.Web.Modules.Operations.ExtraHours { ShiftInstanceId = shift.Id, InstructorId = instrUserId, Hours = 2m, Reason = "הארכה", Status = ExtraHoursStatus.Pending };
            db.ExtraHours.Add(xh);
            await db.SaveChangesAsync();
            xhId = xh.Id;
        }

        var getResp = await adminClient.GetAsync("/Operations/ExtraHours");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await adminClient.PostAsync($"/Operations/ExtraHours?handler=Deny&id={xhId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await postResp.Content.ReadAsStringAsync());
        Assert.Contains("נדחה", body);
        Assert.DoesNotContain("ממתין", body);

        using (var s = adminFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var rec = await db.ExtraHours.FindAsync(xhId);
            Assert.Equal(ExtraHoursStatus.Denied, rec!.Status);
        }

        instrFactory.Dispose();
        adminFactory.Dispose();
    }

    [Fact]
    public async Task Instructor_cannot_deny_extrahours_via_handler()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_deny403_xh");

        int xhId;
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"Sch-d403-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-d403-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class { Name = $"C-d403-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();

            var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instrUserId, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();

            var shift = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instrUserId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), Status = ShiftInstanceStatus.Scheduled };
            db.ShiftInstances.Add(shift);
            await db.SaveChangesAsync();

            var xh = new Orkabi.Web.Modules.Operations.ExtraHours { ShiftInstanceId = shift.Id, InstructorId = instrUserId, Hours = 1m, Reason = "הכנה", Status = ExtraHoursStatus.Pending };
            db.ExtraHours.Add(xh);
            await db.SaveChangesAsync();
            xhId = xh.Id;
        }

        var getResp = await instrClient.GetAsync("/Operations/ExtraHours");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await instrClient.PostAsync($"/Operations/ExtraHours?handler=Deny&id={xhId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains("AccessDenied", postResp.Headers.Location?.ToString());

        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var rec = await db.ExtraHours.FindAsync(xhId);
            Assert.Equal(ExtraHoursStatus.Pending, rec!.Status);  // still pending — deny was blocked
        }

        instrFactory.Dispose();
    }

    [Fact]
    public async Task Instructor_sees_denied_status_on_own_submission()
    {
        var (factory, client, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_mydeny_xh");

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"Sch-myd-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-myd-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class { Name = $"C-myd-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();

            var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instrUserId, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();

            var shift = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instrUserId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), Status = ShiftInstanceStatus.Scheduled };
            db.ShiftInstances.Add(shift);
            await db.SaveChangesAsync();

            // A denied submission owned by this instructor.
            var xh = new Orkabi.Web.Modules.Operations.ExtraHours { ShiftInstanceId = shift.Id, InstructorId = instrUserId, Hours = 2m, Reason = "הארכה", Status = ExtraHoursStatus.Denied, ApprovedByUserId = instrUserId, ApprovedAt = DateTime.UtcNow };
            db.ExtraHours.Add(xh);
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync("/Operations/ExtraHours");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains("נדחה", body);   // must NOT be silently shown as "אושר"

        factory.Dispose();
    }

    private static async Task<int> SeedOpenIncidentAsync(OrkabiAppFactory f, int instrUserId)
    {
        using var s = f.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var school = new School { Name = $"Sch-inc-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
        var year = new AcademicYear { Label = $"Y-inc-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
        db.Schools.Add(school); db.AcademicYears.Add(year);
        await db.SaveChangesAsync();
        var cls = new Class { Name = $"C-inc-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instrUserId, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();
        var shift = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instrUserId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), Status = ShiftInstanceStatus.Scheduled };
        db.ShiftInstances.Add(shift);
        await db.SaveChangesAsync();
        var inc = new Orkabi.Web.Modules.Operations.IncidentReport { ShiftInstanceId = shift.Id, InstructorId = instrUserId, Severity = IncidentSeverity.High, Description = "אירוע חמור", Status = IncidentStatus.Open };
        db.IncidentReports.Add(inc);
        await db.SaveChangesAsync();
        return inc.Id;
    }

    [Fact]
    public async Task Admin_closes_incident_sets_status_closed()
    {
        var (instrFactory, _, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_incclose");
        var (adminFactory, adminClient, _) = await CreateAdminClientAsync(_sqlite, "_incclose_a");
        var incId = await SeedOpenIncidentAsync(adminFactory, instrUserId);

        var tokenResp = await adminClient.GetAsync("/Admin/Users/Create");   // any admin form page → valid token
        var token = AntiForgery.Extract(await tokenResp.Content.ReadAsStringAsync());
        var postResp = await adminClient.PostAsync($"/Operations/Incidents?handler=Close&id={incId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.DoesNotContain("AccessDenied", postResp.Headers.Location?.ToString() ?? "");

        using (var s = adminFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var rec = await db.IncidentReports.FindAsync(incId);
            Assert.Equal(IncidentStatus.Closed, rec!.Status);
        }
        instrFactory.Dispose(); adminFactory.Dispose();
    }

    [Fact]
    public async Task Admin_escalates_incident_sets_status_escalated()
    {
        var (instrFactory, _, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_incesc");
        var (adminFactory, adminClient, _) = await CreateAdminClientAsync(_sqlite, "_incesc_a");
        var incId = await SeedOpenIncidentAsync(adminFactory, instrUserId);

        var tokenResp = await adminClient.GetAsync("/Admin/Users/Create");
        var token = AntiForgery.Extract(await tokenResp.Content.ReadAsStringAsync());
        var postResp = await adminClient.PostAsync($"/Operations/Incidents?handler=Escalate&id={incId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.DoesNotContain("AccessDenied", postResp.Headers.Location?.ToString() ?? "");

        using (var s = adminFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var rec = await db.IncidentReports.FindAsync(incId);
            Assert.Equal(IncidentStatus.Escalated, rec!.Status);
        }
        instrFactory.Dispose(); adminFactory.Dispose();
    }

    [Fact]
    public async Task Non_admin_cannot_close_incident()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_incclose403");
        var incId = await SeedOpenIncidentAsync(instrFactory, instrUserId);

        var getResp = await instrClient.GetAsync("/Operations/Incidents");   // instructor sees the submit form → token
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());
        var postResp = await instrClient.PostAsync($"/Operations/Incidents?handler=Close&id={incId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains("AccessDenied", postResp.Headers.Location?.ToString() ?? "");

        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var rec = await db.IncidentReports.FindAsync(incId);
            Assert.Equal(IncidentStatus.Open, rec!.Status);  // unchanged
        }
        instrFactory.Dispose();
    }

    [Fact]
    public async Task Instructor_cannot_approve_extrahours_via_handler()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_403_xh");
        var (adminFactory, _, adminUserId) = await CreateAdminClientAsync(_sqlite, "_403_xh_admin");

        int xhId;
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"Sch-403xh-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
            var year = new AcademicYear { Label = $"Y-403xh-{Guid.NewGuid():N}"[..10], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class { Name = $"C-403xh-{Guid.NewGuid():N}"[..15], SchoolId = school.Id, AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();

            var template = new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instrUserId, DayOfWeek = DayOfWeek.Thursday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0), AcademicYearId = year.Id, Status = EntityStatus.Active };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();

            var shift = new ShiftInstance { TemplateId = template.Id, ActualInstructorId = instrUserId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)), Status = ShiftInstanceStatus.Scheduled };
            db.ShiftInstances.Add(shift);
            await db.SaveChangesAsync();

            var xh = new Orkabi.Web.Modules.Operations.ExtraHours { ShiftInstanceId = shift.Id, InstructorId = instrUserId, Hours = 1m, Reason = "הכנה", Status = ExtraHoursStatus.Pending };
            db.ExtraHours.Add(xh);
            await db.SaveChangesAsync();
            xhId = xh.Id;
        }

        // GET the page as instructor to obtain antiforgery token
        var getResp = await instrClient.GetAsync("/Operations/ExtraHours");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // Instructor attempts to POST to the Approve handler
        var postResp = await instrClient.PostAsync($"/Operations/ExtraHours?handler=Approve&id={xhId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        // Cookie auth redirects Forbid() to AccessDenied — assert denied (not 200 success)
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains("AccessDenied", postResp.Headers.Location?.ToString());

        // Record must still be Pending — not approved
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.ExtraHours.FindAsync(xhId);
            Assert.Equal(ExtraHoursStatus.Pending, record!.Status);
        }

        instrFactory.Dispose();
        adminFactory.Dispose();
    }

    [Fact]
    public async Task Instructor_cannot_approve_or_reject_vacation_via_handler()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_403_vac");
        var (adminFactory, _, _) = await CreateAdminClientAsync(_sqlite, "_403_vac_admin");

        int vacId;
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
            var vac = new VacationRequest
            {
                InstructorId = instrUserId,
                StartDate = today.AddDays(3),
                EndDate = today.AddDays(7),
                Status = VacationStatus.Pending
            };
            db.VacationRequests.Add(vac);
            await db.SaveChangesAsync();
            vacId = vac.Id;
        }

        // GET the vacations page as instructor to obtain antiforgery token
        var getResp = await instrClient.GetAsync("/Operations/Vacations");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // Instructor attempts to POST to the Approve handler — cookie auth redirects Forbid() to AccessDenied
        var approveResp = await instrClient.PostAsync($"/Operations/Vacations?handler=Approve&id={vacId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, approveResp.StatusCode);
        Assert.Contains("AccessDenied", approveResp.Headers.Location?.ToString());

        // Record must still be Pending after attempted approval
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.VacationRequests.FindAsync(vacId);
            Assert.Equal(VacationStatus.Pending, record!.Status);
        }

        // Instructor attempts to POST to the Reject handler (fresh token)
        var getResp2 = await instrClient.GetAsync("/Operations/Vacations");
        var token2 = AntiForgery.Extract(await getResp2.Content.ReadAsStringAsync());
        var rejectResp = await instrClient.PostAsync($"/Operations/Vacations?handler=Reject&id={vacId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token2 }));
        Assert.Equal(HttpStatusCode.Redirect, rejectResp.StatusCode);
        Assert.Contains("AccessDenied", rejectResp.Headers.Location?.ToString());

        // Record must still be Pending after attempted rejection
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.VacationRequests.FindAsync(vacId);
            Assert.Equal(VacationStatus.Pending, record!.Status);
        }

        instrFactory.Dispose();
        adminFactory.Dispose();
    }

    // ── Task 6: Minimal Action-Items read page ──────────────────────────────

    /// <summary>
    /// Slice-5 Task 2: page is now open to all authenticated users.
    /// Instructor gets 200 (sees their own queue, not the Admin-only items).
    /// Adapted from the Slice-3 "Instructor_redirected_from_action_items_page" test which
    /// asserted 302/403 — that was correct for the old [Authorize(Roles = Admin)] gate.
    /// </summary>
    [Fact]
    public async Task Instructor_can_access_action_items_page_as_all_roles_hub()
    {
        var (factory, client, _) = await CreateInstructorClientAsync(_sqlite, "_ai_403");
        var resp = await client.GetAsync("/Operations/ActionItems");
        // Page is now role-aware and open to ALL authenticated users (Slice-5 Task 2)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains("מרכז הפעולות", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Admin_sees_open_gap_action_item_on_action_items_page()
    {
        var (factory, client, _) = await CreateAdminClientAsync(_sqlite, "_ai_read");

        // Seed a Gap ActionItem directly via the service
        using (var s = factory.Services.CreateScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ActionItemService>();
            // Use dummy classId/modelId (5001/5002) that won't collide with other tests
            await svc.EnsureGapActionItemAsync(5001, 5002, 8, 9);
        }

        var resp = await client.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        // The scope-note placeholder was removed in Slice-5 Task 2 (page is now the full Action Hub)
        // The page title is now מרכז הפעולות
        Assert.Contains("מרכז הפעולות", body);
        // The card must contain the Hebrew description seeded above (partial match on the description pattern)
        Assert.Contains("חריגת קצב", body);
        // Status chip for open item
        Assert.Contains("פתוח", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Admin_sees_empty_state_when_no_open_action_items()
    {
        var (factory, client, _) = await CreateAdminClientAsync(_sqlite, "_ai_empty");

        var resp = await client.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        // Either empty state or list renders; page must not error
        Assert.Contains("משימות פתוחות", body);
        factory.Dispose();
    }

    [Fact]
    public async Task Admin_approves_vacation_row_swaps_to_approved()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_vac_approve");
        var (adminFactory, adminClient, adminUserId) = await CreateAdminClientAsync(_sqlite, "_vac_approve_a");

        int vacId;
        using (var s = adminFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
            var vac = new VacationRequest
            {
                InstructorId = instrUserId,
                StartDate = today.AddDays(2),
                EndDate = today.AddDays(5),
                Status = VacationStatus.Pending
            };
            db.VacationRequests.Add(vac);
            await db.SaveChangesAsync();
            vacId = vac.Id;
        }

        var getResp = await adminClient.GetAsync("/Operations/Vacations");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await adminClient.PostAsync($"/Operations/Vacations?handler=Approve&id={vacId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await postResp.Content.ReadAsStringAsync());
        Assert.Contains("אושר", body);
        Assert.DoesNotContain("ממתין", body);

        instrFactory.Dispose();
        adminFactory.Dispose();
    }
}
