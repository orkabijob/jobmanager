using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class SyllabusClassLinkTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public SyllabusClassLinkTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task Class_links_and_unlinks_syllabus()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();

        int schoolId, yearId, syllabusId, classId;

        // ── Scope 1: seed prerequisites + link ──────────────────────────────
        using (var scope1 = factory.Services.CreateScope())
        {
            var db = scope1.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "תל אביב" };
            var year = new AcademicYear
            {
                Label = Guid.NewGuid().ToString("N")[..18],
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                IsCurrent = false
            };
            var syllabus = new Syllabus
            {
                Name = $"סילבוס-{Guid.NewGuid():N}",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                Status = EntityStatus.Active
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            db.Syllabi.Add(syllabus);
            await db.SaveChangesAsync();

            schoolId = school.Id;
            yearId = year.Id;
            syllabusId = syllabus.Id;

            var cls = new Class
            {
                Name = $"כיתה-{Guid.NewGuid():N}",
                SchoolId = schoolId,
                AcademicYearId = yearId,
                SyllabusId = syllabusId,
                Status = EntityStatus.Active
            };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            classId = cls.Id;
        }

        // ── Scope 2: reload + assert linked ─────────────────────────────────
        using (var scope2 = factory.Services.CreateScope())
        {
            var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.Classes.IgnoreQueryFilters().SingleAsync(c => c.Id == classId);
            Assert.Equal(syllabusId, cls.SyllabusId);
        }

        // ── Scope 3: unlink (set null) + save ───────────────────────────────
        using (var scope3 = factory.Services.CreateScope())
        {
            var db = scope3.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.Classes.IgnoreQueryFilters().SingleAsync(c => c.Id == classId);
            cls.SyllabusId = null;
            await db.SaveChangesAsync();
        }

        // ── Scope 4: reload + assert unlinked ───────────────────────────────
        using (var scope4 = factory.Services.CreateScope())
        {
            var db = scope4.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.Classes.IgnoreQueryFilters().SingleAsync(c => c.Id == classId);
            Assert.Null(cls.SyllabusId);
        }
    }

    // ── Page-level tests: Edit form assigns SyllabusId via POST ──────────────

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateCsClientAsync(
        SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"cs.syllink{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.CustomerService);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"cs.syllink{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    [Fact]
    public async Task Edit_class_POST_sets_SyllabusId_and_GET_preselects_it()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_post_set");

        int schoolId, yearId, syllabusId, classId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "חיפה" };
            var year = new AcademicYear
            {
                Label = Guid.NewGuid().ToString("N")[..18],
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                IsCurrent = false
            };
            var syllabus = new Syllabus
            {
                Name = $"סילבוס-לבדיקה-{Guid.NewGuid():N}",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                Status = EntityStatus.Active
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            db.Syllabi.Add(syllabus);
            await db.SaveChangesAsync();

            schoolId = school.Id;
            yearId = year.Id;
            syllabusId = syllabus.Id;

            var cls = new Class
            {
                Name = $"כיתה-לבדיקה-{Guid.NewGuid():N}",
                SchoolId = schoolId,
                AcademicYearId = yearId,
                SyllabusId = null,
                Status = EntityStatus.Active
            };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            classId = cls.Id;
        }

        // GET the edit page — should render without a preselected syllabus
        var getResp = await client.GetAsync($"/People/Classes/Edit/{classId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, getResp.StatusCode);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(getHtml);

        // POST with SyllabusId set
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"]           = "כיתה-לבדיקה-מעודכנת",
            ["Input.SchoolId"]       = schoolId.ToString(),
            ["Input.AcademicYearId"] = yearId.ToString(),
            ["Input.Status"]         = "Active",
            ["Input.SyllabusId"]     = syllabusId.ToString(),
            ["__RequestVerificationToken"] = token
        });
        var postResp = await client.PostAsync($"/People/Classes/Edit/{classId}", form);
        // Successful edit redirects to Index
        Assert.Equal(System.Net.HttpStatusCode.Redirect, postResp.StatusCode);

        // Verify DB: SyllabusId is persisted
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.Classes.IgnoreQueryFilters().SingleAsync(c => c.Id == classId);
            Assert.Equal(syllabusId, cls.SyllabusId);
        }

        // GET edit again — the syllabus option should be selected (value="syllabusId" selected)
        var getResp2 = await client.GetAsync($"/People/Classes/Edit/{classId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, getResp2.StatusCode);
        var getHtml2 = System.Net.WebUtility.HtmlDecode(await getResp2.Content.ReadAsStringAsync());
        Assert.Contains($"value=\"{syllabusId}\"", getHtml2);

        factory.Dispose();
    }

    [Fact]
    public async Task Edit_class_POST_clears_SyllabusId_when_none_selected()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_post_clear");

        int schoolId, yearId, syllabusId, classId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "ירושלים" };
            var year = new AcademicYear
            {
                Label = Guid.NewGuid().ToString("N")[..18],
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                IsCurrent = false
            };
            var syllabus = new Syllabus
            {
                Name = $"סילבוס-לניקוי-{Guid.NewGuid():N}",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                Status = EntityStatus.Active
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            db.Syllabi.Add(syllabus);
            await db.SaveChangesAsync();

            schoolId = school.Id;
            yearId = year.Id;
            syllabusId = syllabus.Id;

            // Class starts with SyllabusId set
            var cls = new Class
            {
                Name = $"כיתה-עם-סילבוס-{Guid.NewGuid():N}",
                SchoolId = schoolId,
                AcademicYearId = yearId,
                SyllabusId = syllabusId,
                Status = EntityStatus.Active
            };
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            classId = cls.Id;
        }

        // GET edit page and post with SyllabusId = "" (none/null)
        var getResp = await client.GetAsync($"/People/Classes/Edit/{classId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"]           = "כיתה-ללא-סילבוס",
            ["Input.SchoolId"]       = schoolId.ToString(),
            ["Input.AcademicYearId"] = yearId.ToString(),
            ["Input.Status"]         = "Active",
            ["Input.SyllabusId"]     = "",   // ← none selected = null
            ["__RequestVerificationToken"] = token
        });
        var postResp = await client.PostAsync($"/People/Classes/Edit/{classId}", form);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, postResp.StatusCode);

        // Verify DB: SyllabusId is null
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.Classes.IgnoreQueryFilters().SingleAsync(c => c.Id == classId);
            Assert.Null(cls.SyllabusId);
        }

        factory.Dispose();
    }

    [Fact]
    public async Task Syllabi_index_shows_linked_class_count()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_index_count");

        int syllabusId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "באר שבע" };
            var year = new AcademicYear
            {
                Label = Guid.NewGuid().ToString("N")[..18],
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                IsCurrent = false
            };
            var syllabus = new Syllabus
            {
                Name = $"סילבוס-ספירה-{Guid.NewGuid():N}",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 6, 30),
                Status = EntityStatus.Active
            };
            db.Schools.Add(school);
            db.AcademicYears.Add(year);
            db.Syllabi.Add(syllabus);
            await db.SaveChangesAsync();

            syllabusId = syllabus.Id;

            // Seed 2 active classes linked to this syllabus
            db.Classes.Add(new Class
            {
                Name = $"כיתה-א-{Guid.NewGuid():N}",
                SchoolId = school.Id,
                AcademicYearId = year.Id,
                SyllabusId = syllabusId,
                Status = EntityStatus.Active
            });
            db.Classes.Add(new Class
            {
                Name = $"כיתה-ב-{Guid.NewGuid():N}",
                SchoolId = school.Id,
                AcademicYearId = year.Id,
                SyllabusId = syllabusId,
                Status = EntityStatus.Active
            });
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync("/Curriculum/Syllabi");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var raw = await resp.Content.ReadAsStringAsync();
        var body = System.Net.WebUtility.HtmlDecode(raw);

        // The count "2" should appear inside a <bdi class="num"> in the כיתות column
        Assert.Contains(">2<", body);

        factory.Dispose();
    }
}
