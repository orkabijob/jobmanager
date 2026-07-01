using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class SchedulingPagesTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public SchedulingPagesTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateCsClientAsync(SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"cs.scheduling{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.CustomerService);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"cs.scheduling{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateAdminClientAsync(SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"admin.scheduling{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.Admin);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"admin.scheduling{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateInstructorClientAsync(SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"instructor.scheduling{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.Instructor);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"instructor.scheduling{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    // ── Authz ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_redirected_from_scheduling()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var resp = await client.GetAsync("/Scheduling");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Cs_user_can_open_scheduling_templates()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_templates");
        var resp = await client.GetAsync("/Scheduling/Templates");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_forbidden_from_substitutions()
    {
        var (factory, client) = await CreateInstructorClientAsync(_sqlite, "_subs");
        var resp = await client.GetAsync("/Scheduling/Substitutions");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Creating_template_generates_instances()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_tpl_inst");

        string className;
        int classId, instructorId, academicYearId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var school = new School { Name = "בית-ספר-תבנית", City = "תל אביב" };
            // Span the year around "today" (Israel clock) so generated instances land in the
            // Instances page's today-based window regardless of the calendar date the test runs on.
            var todayIl = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Orkabi.Web.Shared.IsraelClock.IsraelTz));
            var year = new AcademicYear
            {
                Label = "תשפ-תבנית",
                StartDate = todayIl.AddMonths(-2),
                EndDate = todayIl.AddMonths(2),
                IsCurrent = false
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            className = $"כיתה-תבנית-{Guid.NewGuid():N}"[..20];
            var cls = new Class { Name = className, School = school, AcademicYear = year, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();

            classId = cls.Id;
            academicYearId = year.Id;

            var instrEmail = $"instr.tpl{Guid.NewGuid():N}@test.test";
            var instr = new AppUser { UserName = instrEmail, Email = instrEmail };
            await um.CreateAsync(instr, "Passw0rd!");
            await um.AddToRoleAsync(instr, AppRoles.Instructor);
            instructorId = instr.Id;
        }

        var getResp = await client.GetAsync("/Scheduling/Templates/Create");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(getHtml);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.ClassId"] = classId.ToString(),
            ["Input.DefaultInstructorId"] = instructorId.ToString(),
            ["Input.DayOfWeek"] = "1",
            ["Input.StartTime"] = "09:00",
            ["Input.EndTime"] = "10:00",
            ["Input.AcademicYearId"] = academicYearId.ToString(),
            ["Input.StatusValue"] = "Active",
            ["__RequestVerificationToken"] = token
        });
        var postResp = await client.PostAsync("/Scheduling/Templates/Create", form);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        var instResp = await client.GetAsync("/Scheduling/Instances");
        Assert.Equal(HttpStatusCode.OK, instResp.StatusCode);
        var raw = await instResp.Content.ReadAsStringAsync();
        var body = System.Net.WebUtility.HtmlDecode(raw);
        Assert.Contains(className, body);

        factory.Dispose();
    }

    [Fact]
    public async Task Approving_substitution_swaps_row_and_sets_actual_instructor()
    {
        var (factory, adminClient) = await CreateAdminClientAsync(_sqlite, "_approve");

        int subRequestId, substituteId, instanceId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var school = new School { Name = "בית-ספר-החלפה", City = "חיפה" };
            var year = new AcademicYear
            {
                Label = "תשפ-החלפה",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                IsCurrent = false
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var cls = new Class { Name = "כיתה-החלפה", School = school, AcademicYear = year, Status = EntityStatus.Active };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();

            var reqEmail = $"req.sub{Guid.NewGuid():N}@test.test";
            var requester = new AppUser { UserName = reqEmail, Email = reqEmail };
            await um.CreateAsync(requester, "Passw0rd!");
            await um.AddToRoleAsync(requester, AppRoles.Instructor);

            var subEmail = $"sub.sub{Guid.NewGuid():N}@test.test";
            var substitute = new AppUser { UserName = subEmail, Email = subEmail };
            await um.CreateAsync(substitute, "Passw0rd!");
            await um.AddToRoleAsync(substitute, AppRoles.Instructor);
            substituteId = substitute.Id;

            var template = new ShiftTemplate
            {
                ClassId = cls.Id,
                DefaultInstructorId = requester.Id,
                DayOfWeek = DayOfWeek.Wednesday,
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(11, 0),
                AcademicYearId = year.Id,
                Status = EntityStatus.Active
            };
            db.ShiftTemplates.Add(template);
            await db.SaveChangesAsync();

            var instance = new ShiftInstance
            {
                TemplateId = template.Id,
                ActualInstructorId = requester.Id,
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                Status = ShiftInstanceStatus.Scheduled
            };
            db.ShiftInstances.Add(instance);
            await db.SaveChangesAsync();
            instanceId = instance.Id;

            var subRequest = new SubstitutionRequest
            {
                ShiftInstanceId = instance.Id,
                RequestingInstructorId = requester.Id,
                SubstituteInstructorId = substitute.Id,
                Status = SubstitutionStatus.Pending
            };
            db.SubstitutionRequests.Add(subRequest);
            await db.SaveChangesAsync();
            subRequestId = subRequest.Id;
        }

        var getResp = await adminClient.GetAsync("/Scheduling/Substitutions");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(getHtml);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var postResp = await adminClient.PostAsync($"/Scheduling/Substitutions?handler=Approve&id={subRequestId}", form);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var raw = await postResp.Content.ReadAsStringAsync();
        var body = System.Net.WebUtility.HtmlDecode(raw);
        Assert.Contains("אושר", body);
        Assert.DoesNotContain("ממתין", body);

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var inst = await db.ShiftInstances.FindAsync(instanceId);
            Assert.Equal(substituteId, inst!.ActualInstructorId);
        }

        factory.Dispose();
    }
}
