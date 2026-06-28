using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class RosterTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public RosterTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateCsClientAsync(
        SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"cs.roster{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.CustomerService);
            }
        }
        var httpClient = await TestLogin.SignInAsync(factory, $"cs.roster{suffix}@test.test", "Passw0rd!");
        return (factory, httpClient);
    }

    /// <summary>Seeds School + AcademicYear + Class + Client and returns them.</summary>
    private static async Task<(School school, AcademicYear year, Class cls, Client client)>
        SeedAsync(AppDbContext db, string suffix = "")
    {
        var school = new School { Name = $"בית ספר {suffix}", City = "תל אביב" };
        var year = new AcademicYear
        {
            Label = $"תשפ\"ו-{suffix}",
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
        var client = new Client { Name = $"תלמיד-{Guid.NewGuid():N}", IsActive = true };
        db.Classes.Add(cls);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        return (school, year, cls, client);
    }

    // ── F15: manual "graduate" — CS marks an Active enrollment Completed ──

    [Fact]
    public async Task Complete_enrollment_sets_status_completed()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<EnrollmentService>();

        var (_, __, cls, client) = await SeedAsync(db, "comp");
        var enr = new Enrollment { ClassId = cls.Id, ClientId = client.Id, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow };
        db.Enrollments.Add(enr);
        await db.SaveChangesAsync();

        await svc.CompleteAsync(enr.Id);

        db.ChangeTracker.Clear();
        Assert.Equal(EnrollmentStatus.Completed, (await db.Enrollments.FindAsync(enr.Id))!.Status);
    }

    [Fact]
    public async Task Complete_non_active_enrollment_throws()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<EnrollmentService>();

        var (_, __, cls, client) = await SeedAsync(db, "compbad");
        var enr = new Enrollment { ClassId = cls.Id, ClientId = client.Id, Status = EnrollmentStatus.Dropped, EnrolledAt = DateTime.UtcNow };
        db.Enrollments.Add(enr);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompleteAsync(enr.Id));
    }

    [Fact]
    public async Task Cs_completes_enrollment_via_roster()
    {
        var (factory, httpClient) = await CreateCsClientAsync(_sqlite, "_complete");
        int classId, enrollmentId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var (_, __, cls, client) = await SeedAsync(db, "completepg");
            var enr = new Enrollment { ClassId = cls.Id, ClientId = client.Id, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow };
            db.Enrollments.Add(enr);
            await db.SaveChangesAsync();
            classId = cls.Id; enrollmentId = enr.Id;
        }

        var getResp = await httpClient.GetAsync($"/People/Classes/Roster/{classId}");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["enrollmentId"] = enrollmentId.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var postResp = await httpClient.PostAsync($"/People/Classes/Roster/{classId}?handler=Complete", form);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(EnrollmentStatus.Completed, (await db.Enrollments.FindAsync(enrollmentId))!.Status);
        }
        factory.Dispose();
    }

    [Fact]
    public async Task Toggle_tryout_on_completed_enrollment_throws_and_keeps_completed()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<EnrollmentService>();

        var (_, __, cls, client) = await SeedAsync(db, "comptog");
        var enr = new Enrollment { ClassId = cls.Id, ClientId = client.Id, Status = EnrollmentStatus.Completed, EnrolledAt = DateTime.UtcNow };
        db.Enrollments.Add(enr);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ToggleAsync(enr.Id, "tryout"));

        db.ChangeTracker.Clear();
        Assert.Equal(EnrollmentStatus.Completed, (await db.Enrollments.FindAsync(enr.Id))!.Status);  // not resurrected
    }

    [Fact]
    public async Task Cs_can_enroll_a_client_then_it_appears_on_the_roster()
    {
        var (factory, httpClient) = await CreateCsClientAsync(_sqlite, "_enroll");

        Class cls;
        Client seedClient;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var (_, __, c, cl) = await SeedAsync(db, "enroll");
            cls = c;
            seedClient = cl;
        }

        // GET the roster page to extract anti-forgery token
        var getResp = await httpClient.GetAsync($"/People/Classes/Roster/{cls.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var html = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(html);

        // POST Add handler
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["clientId"] = seedClient.Id.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var postResp = await httpClient.PostAsync(
            $"/People/Classes/Roster/{cls.Id}?handler=Add", form);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        // Follow redirect and verify client appears on roster
        var location = postResp.Headers.Location!.ToString();
        var rosterResp = await httpClient.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, rosterResp.StatusCode);
        var rosterRaw = await rosterResp.Content.ReadAsStringAsync();
        var rosterBody = System.Net.WebUtility.HtmlDecode(rosterRaw);
        Assert.Contains(seedClient.Name, rosterBody);

        factory.Dispose();
    }

    [Fact]
    public async Task Enrolling_same_client_twice_shows_friendly_error_not_500()
    {
        var (factory, httpClient) = await CreateCsClientAsync(_sqlite, "_dupcheck");

        Class cls;
        Client seedClient;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var (_, __, c, cl) = await SeedAsync(db, "dupcheck");
            cls = c;
            seedClient = cl;
        }

        // GET roster for anti-forgery token
        var getResp = await httpClient.GetAsync($"/People/Classes/Roster/{cls.Id}");
        var html = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(html);

        // POST Add — first enroll (should succeed → 302)
        var form1 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["clientId"] = seedClient.Id.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var firstPost = await httpClient.PostAsync(
            $"/People/Classes/Roster/{cls.Id}?handler=Add", form1);
        Assert.Equal(HttpStatusCode.Redirect, firstPost.StatusCode);

        // GET roster again for fresh anti-forgery token
        var getResp2 = await httpClient.GetAsync($"/People/Classes/Roster/{cls.Id}");
        var html2 = await getResp2.Content.ReadAsStringAsync();
        var token2 = AntiForgery.Extract(html2);

        // POST Add — second enroll (should redirect back, NOT 500)
        var form2 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["clientId"] = seedClient.Id.ToString(),
            ["__RequestVerificationToken"] = token2
        });
        var secondPost = await httpClient.PostAsync(
            $"/People/Classes/Roster/{cls.Id}?handler=Add", form2);
        Assert.Equal(HttpStatusCode.Redirect, secondPost.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, secondPost.StatusCode);

        // Follow redirect — page should show the friendly Hebrew error
        var location = secondPost.Headers.Location!.ToString();
        var errorPageResp = await httpClient.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, errorPageResp.StatusCode);
        var errorRaw = await errorPageResp.Content.ReadAsStringAsync();
        var errorBody = System.Net.WebUtility.HtmlDecode(errorRaw);
        Assert.Contains("התלמיד כבר רשום לכיתה זו", errorBody);

        factory.Dispose();
    }

    [Fact]
    public async Task Dropping_an_enrollment_moves_client_back_to_available()
    {
        var (factory, httpClient) = await CreateCsClientAsync(_sqlite, "_drop");

        Class cls;
        Client seedClient;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var (_, __, c, cl) = await SeedAsync(db, "drop");
            cls = c;
            seedClient = cl;
        }

        // GET roster for anti-forgery token and enroll
        var getResp = await httpClient.GetAsync($"/People/Classes/Roster/{cls.Id}");
        var html = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(html);

        // POST Add
        var addForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["clientId"] = seedClient.Id.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var addPost = await httpClient.PostAsync(
            $"/People/Classes/Roster/{cls.Id}?handler=Add", addForm);
        Assert.Equal(HttpStatusCode.Redirect, addPost.StatusCode);

        // Get the enrollment ID from DB
        int enrollmentId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrollment = db.Enrollments.First(e => e.ClientId == seedClient.Id && e.ClassId == cls.Id);
            enrollmentId = enrollment.Id;
        }

        // GET roster again for fresh token
        var getRoster = await httpClient.GetAsync($"/People/Classes/Roster/{cls.Id}");
        var rosterHtml = await getRoster.Content.ReadAsStringAsync();
        var rosterToken = AntiForgery.Extract(rosterHtml);

        // POST Remove
        var removeForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["enrollmentId"] = enrollmentId.ToString(),
            ["__RequestVerificationToken"] = rosterToken
        });
        var removePost = await httpClient.PostAsync(
            $"/People/Classes/Roster/{cls.Id}?handler=Remove", removeForm);
        Assert.Equal(HttpStatusCode.Redirect, removePost.StatusCode);

        // Follow redirect — client should now appear in available pane
        var location = removePost.Headers.Location!.ToString();
        var finalResp = await httpClient.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, finalResp.StatusCode);
        var finalRaw = await finalResp.Content.ReadAsStringAsync();
        var finalBody = System.Net.WebUtility.HtmlDecode(finalRaw);
        Assert.Contains(seedClient.Name, finalBody); // client back in available list

        factory.Dispose();
    }

    [Fact]
    public async Task Toggling_tryout_marks_enrollment_and_moves_it_to_the_tray()
    {
        var (factory, httpClient) = await CreateCsClientAsync(_sqlite, "_tryout");

        Class cls;
        Client seedClient;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var (_, __, c, cl) = await SeedAsync(db, "tryout");
            cls = c;
            seedClient = cl;
        }

        // GET roster and enroll client
        var getResp = await httpClient.GetAsync($"/People/Classes/Roster/{cls.Id}");
        var html = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(html);

        var addForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["clientId"] = seedClient.Id.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var addPost = await httpClient.PostAsync(
            $"/People/Classes/Roster/{cls.Id}?handler=Add", addForm);
        Assert.Equal(HttpStatusCode.Redirect, addPost.StatusCode);

        // Get enrollment ID from DB
        int enrollmentId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrollment = db.Enrollments.First(e => e.ClientId == seedClient.Id && e.ClassId == cls.Id);
            enrollmentId = enrollment.Id;
        }

        // GET roster again for fresh token
        var getRoster = await httpClient.GetAsync($"/People/Classes/Roster/{cls.Id}");
        var rosterHtml = await getRoster.Content.ReadAsStringAsync();
        var rosterToken = AntiForgery.Extract(rosterHtml);

        // POST Toggle tryout
        var toggleForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["enrollmentId"] = enrollmentId.ToString(),
            ["field"] = "tryout",
            ["__RequestVerificationToken"] = rosterToken
        });
        var togglePost = await httpClient.PostAsync(
            $"/People/Classes/Roster/{cls.Id}?handler=Toggle", toggleForm);
        Assert.Equal(HttpStatusCode.Redirect, togglePost.StatusCode);

        // Follow redirect — TRYOUT badge should appear for the client
        var location = togglePost.Headers.Location!.ToString();
        var finalResp = await httpClient.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, finalResp.StatusCode);
        var finalRaw = await finalResp.Content.ReadAsStringAsync();
        var finalBody = System.Net.WebUtility.HtmlDecode(finalRaw);
        Assert.Contains("TRYOUT", finalBody);

        factory.Dispose();
    }
}
