using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class ClassesPageTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ClassesPageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateCsClientAsync(SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"cs.classes{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.CustomerService);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"cs.classes{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    [Fact]
    public async Task Classes_index_hides_archived_by_default_and_shows_them_when_filtered()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_archive_filter");

        // Arrange: seed 1 school, 1 academic year, 1 Active class, 1 Archived class
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

            var school = new School { Name = "בית ספר בדיקה", City = "תל אביב" };
            db.Schools.Add(school);

            var year = new AcademicYear
            {
                Label = "תשפ\"ו",
                StartDate = new DateOnly(2025, 9, 1),
                EndDate = new DateOnly(2026, 7, 1),
                IsCurrent = true
            };
            db.AcademicYears.Add(year);
            await db.SaveChangesAsync();

            var activeClass = new Class
            {
                Name = "כיתה פעילה לבדיקה",
                SchoolId = school.Id,
                AcademicYearId = year.Id,
                Status = EntityStatus.Active
            };
            var archivedClass = new Class
            {
                Name = "כיתה בארכיון לבדיקה",
                SchoolId = school.Id,
                AcademicYearId = year.Id,
                Status = EntityStatus.Archived
            };
            db.Classes.Add(activeClass);
            db.Classes.Add(archivedClass);
            await db.SaveChangesAsync();
        }

        // Act 1: default index (no filter) — shows Active only
        var defaultResp = await client.GetAsync("/People/Classes");
        Assert.Equal(HttpStatusCode.OK, defaultResp.StatusCode);
        var defaultRaw = await defaultResp.Content.ReadAsStringAsync();
        // Razor HTML-encodes non-ASCII chars; decode so Hebrew assertions work.
        var defaultBody = System.Net.WebUtility.HtmlDecode(defaultRaw);

        Assert.Contains("כיתה פעילה לבדיקה", defaultBody);
        Assert.DoesNotContain("כיתה בארכיון לבדיקה", defaultBody);

        // Act 2: status=Archived filter — shows Archived only
        var archivedResp = await client.GetAsync("/People/Classes?status=Archived");
        Assert.Equal(HttpStatusCode.OK, archivedResp.StatusCode);
        var archivedRaw = await archivedResp.Content.ReadAsStringAsync();
        var archivedBody = System.Net.WebUtility.HtmlDecode(archivedRaw);

        Assert.Contains("כיתה בארכיון לבדיקה", archivedBody);
        Assert.DoesNotContain("כיתה פעילה לבדיקה", archivedBody);

        factory.Dispose();
    }
}
