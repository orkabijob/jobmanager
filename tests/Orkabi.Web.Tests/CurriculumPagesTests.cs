using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class CurriculumPagesTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public CurriculumPagesTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateCsClientAsync(SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"cs.curriculum{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.CustomerService);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"cs.curriculum{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    private static async Task<(OrkabiAppFactory factory, HttpClient client)> CreateInstructorClientAsync(SqliteFixture sqlite, string suffix = "")
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"instructor.curriculum{suffix}@test.test";
            var existing = await um.FindByEmailAsync(email);
            if (existing is null)
            {
                var u = new AppUser { UserName = email, Email = email };
                await um.CreateAsync(u, "Passw0rd!");
                await um.AddToRoleAsync(u, AppRoles.Instructor);
            }
        }
        var client = await TestLogin.SignInAsync(factory, $"instructor.curriculum{suffix}@test.test", "Passw0rd!");
        return (factory, client);
    }

    // ── F16: model delete (FK-guarded) ─────────────────────────────────────────────

    [Fact]
    public async Task Cs_deletes_unused_model()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_delmodel");
        int modelId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var m = new Model { Name = $"מ-{Guid.NewGuid():N}"[..12], ExpectedLessonsToComplete = 2 };
            db.Models.Add(m);
            await db.SaveChangesAsync();
            modelId = m.Id;
        }

        var getResp = await client.GetAsync($"/Curriculum/Models/Edit/{modelId}");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());
        var postResp = await client.PostAsync($"/Curriculum/Models/Edit/{modelId}?handler=Delete",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(await db.Models.FindAsync(modelId));
        }
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_cannot_delete_model_in_use()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_delmodelinuse");
        int modelId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var m = new Model { Name = $"מ-{Guid.NewGuid():N}"[..12], ExpectedLessonsToComplete = 2 };
            var syl = new Syllabus { Name = $"ס-{Guid.NewGuid():N}"[..12], StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), Status = EntityStatus.Active };
            db.Models.Add(m); db.Syllabi.Add(syl);
            await db.SaveChangesAsync();
            db.SyllabusModels.Add(new SyllabusModel { SyllabusId = syl.Id, ModelId = m.Id, OrderIndex = 1 });
            await db.SaveChangesAsync();
            modelId = m.Id;
        }

        var getResp = await client.GetAsync($"/Curriculum/Models/Edit/{modelId}");
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());
        var postResp = await client.PostAsync($"/Curriculum/Models/Edit/{modelId}?handler=Delete",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);   // re-render with the FK-guard error

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(await db.Models.FindAsync(modelId));  // still there
        }
        factory.Dispose();
    }

    // ── Authz ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_redirected_to_login_from_curriculum()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var resp = await client.GetAsync("/Curriculum");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Instructor_forbidden_from_curriculum()
    {
        var (factory, client) = await CreateInstructorClientAsync(_sqlite, "_authz");
        var resp = await client.GetAsync("/Curriculum");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("AccessDenied", resp.Headers.Location?.ToString());
        factory.Dispose();
    }

    [Fact]
    public async Task Cs_user_can_open_curriculum_index()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_index");
        var resp = await client.GetAsync("/Curriculum");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        factory.Dispose();
    }

    // ── Models CRUD ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Can_create_model_and_see_it_in_list()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_models_crud");

        var getResp = await client.GetAsync("/Curriculum/Models/Create");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(getHtml);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "דגם בדיקה",
            ["Input.ExpectedLessonsToComplete"] = "6",
            ["__RequestVerificationToken"] = token
        });
        var postResp = await client.PostAsync("/Curriculum/Models/Create", form);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        var listResp = await client.GetAsync("/Curriculum/Models");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var raw = await listResp.Content.ReadAsStringAsync();
        var body = System.Net.WebUtility.HtmlDecode(raw);
        Assert.Contains("דגם בדיקה", body);

        factory.Dispose();
    }

    // ── Syllabi + HTMX reorder ────────────────────────────────────────────────────

    [Fact]
    public async Task Syllabus_reorder_moves_model_down_in_rendered_list()
    {
        var (factory, client) = await CreateCsClientAsync(_sqlite, "_syl_reorder");

        int syllabusId;
        int model1Id, model2Id;
        using (var s = factory.Services.CreateScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<CurriculumService>();

            var m1 = await svc.CreateModelAsync(new Orkabi.Web.Modules.Curriculum.Model
            {
                Name = "דגם אחד לבדיקה",
                ExpectedLessonsToComplete = 4
            });
            var m2 = await svc.CreateModelAsync(new Orkabi.Web.Modules.Curriculum.Model
            {
                Name = "דגם שני לבדיקה",
                ExpectedLessonsToComplete = 5
            });
            model1Id = m1.Id;
            model2Id = m2.Id;

            var syl = await svc.CreateSyllabusAsync(
                new Syllabus
                {
                    Name = "סילבוס לבדיקה",
                    StartDate = new DateOnly(2025, 9, 1),
                    EndDate = new DateOnly(2026, 6, 30)
                },
                new[] { (model1Id, 1), (model2Id, 2) }
            );
            syllabusId = syl.Id;
        }

        var getResp = await client.GetAsync($"/Curriculum/Syllabi/Edit/{syllabusId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var editHtml = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(editHtml);

        var editBody = System.Net.WebUtility.HtmlDecode(editHtml);
        var pos1Before = editBody.IndexOf("דגם אחד לבדיקה", StringComparison.Ordinal);
        var pos2Before = editBody.IndexOf("דגם שני לבדיקה", StringComparison.Ordinal);
        Assert.True(pos1Before >= 0, "model1 should be found in initial render");
        Assert.True(pos2Before >= 0, "model2 should be found in initial render");
        Assert.True(pos1Before < pos2Before, "Before reorder: model1 should appear before model2");

        var moveForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var moveResp = await client.PostAsync(
            $"/Curriculum/Syllabi/Edit/{syllabusId}?handler=MoveDown&modelId={model1Id}",
            moveForm);
        Assert.Equal(HttpStatusCode.OK, moveResp.StatusCode);

        var raw = await moveResp.Content.ReadAsStringAsync();
        var body = System.Net.WebUtility.HtmlDecode(raw);

        var pos1After = body.IndexOf("דגם אחד לבדיקה", StringComparison.Ordinal);
        var pos2After = body.IndexOf("דגם שני לבדיקה", StringComparison.Ordinal);
        Assert.True(pos1After >= 0, "model1 should be found after reorder");
        Assert.True(pos2After >= 0, "model2 should be found after reorder");
        Assert.True(pos2After < pos1After, "After MoveDown: model2 should appear before model1");

        factory.Dispose();
    }
}
