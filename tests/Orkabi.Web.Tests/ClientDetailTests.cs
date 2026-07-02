using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

// F12 — client profile / enrollment overview (/People/Clients/Details/{id}).
public class ClientDetailTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ClientDetailTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> ClientAsync(SqliteFixture sqlite, string role, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        var email = $"cd.{role}.{suffix}@test.test";
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            if (await um.FindByEmailAsync(email) is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, role);
            }
        }
        return (factory, await TestLogin.SignInAsync(factory, email, "Passw0rd!"));
    }

    private static async Task<(int clientId, string classA, string classB)> SeedClientWithEnrollmentsAsync(OrkabiAppFactory f)
    {
        using var s = f.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var school = new School { Name = $"בס-{Guid.NewGuid():N}", City = "חיפה" };
        var year = new AcademicYear { Label = $"y{Guid.NewGuid():N}"[..8], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), IsCurrent = false };
        db.Schools.Add(school); db.AcademicYears.Add(year);
        await db.SaveChangesAsync();
        var clsA = new Class { Name = $"כתה-A-{Guid.NewGuid():N}"[..16], School = school, AcademicYear = year, Status = EntityStatus.Active };
        var clsB = new Class { Name = $"כתה-B-{Guid.NewGuid():N}"[..16], School = school, AcademicYear = year, Status = EntityStatus.Active };
        var client = new Client { Name = $"תלמיד-{Guid.NewGuid():N}"[..14], IsActive = true, ParentPhone = "050-1112222" };
        db.Classes.AddRange(clsA, clsB); db.Clients.Add(client);
        await db.SaveChangesAsync();
        db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = clsA.Id, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow });
        db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = clsB.Id, Status = EnrollmentStatus.Dropped, EnrolledAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (client.Id, clsA.Name, clsB.Name);
    }

    [Fact]
    public async Task Anonymous_redirected_to_login()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await c.GetAsync("/People/Clients/Details/1");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Instructor_forbidden()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.Instructor, "f");
        var (clientId, _, _) = await SeedClientWithEnrollmentsAsync(factory);
        var resp = await client.GetAsync($"/People/Clients/Details/{clientId}");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_sees_client_and_all_enrollments()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "v");
        var (clientId, classA, classB) = await SeedClientWithEnrollmentsAsync(factory);
        var resp = await client.GetAsync($"/People/Clients/Details/{clientId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Contains(classA, body);   // active enrollment shown
        Assert.Contains(classB, body);   // dropped enrollment shown too (full history)
        Assert.Contains("tel:050-1112222", body);   // R18: click-to-call parent phone
        factory.Dispose();
    }

    [Fact]
    public async Task Details_404_for_missing_client()
    {
        var (factory, client) = await ClientAsync(_sqlite, AppRoles.CustomerService, "missing");
        var resp = await client.GetAsync("/People/Clients/Details/999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        factory.Dispose();
    }

    [Fact]
    public async Task ListByClient_returns_all_enrollments_with_class()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var (clientId, _, _) = await SeedClientWithEnrollmentsAsync(factory);
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<EnrollmentService>();

        var list = await svc.ListByClientAsync(clientId);

        Assert.Equal(2, list.Count);
        Assert.All(list, e => Assert.NotNull(e.Class));
        factory.Dispose();
    }
}
