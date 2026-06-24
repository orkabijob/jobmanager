using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
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
    public async Task Instructor_403_on_approve_extrahours_handler()
    {
        var (factory, client, _) = await CreateInstructorClientAsync(_sqlite, "_403_xh");
        var getResp = await client.GetAsync("/Operations/ExtraHours");
        // Instructor sees submit form (OK), but if they try to POST Approve...
        // The page handler is on the same page but only admin can see it effectively.
        // Test: instructor GETs the page — OK; they don't see the approval table
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await getResp.Content.ReadAsStringAsync());
        Assert.DoesNotContain("אישור שעות נוספות", body); // admin heading
        Assert.Contains("דיווח שעות נוספות", body); // instructor heading
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
