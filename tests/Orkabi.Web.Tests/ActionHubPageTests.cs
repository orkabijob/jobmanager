using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// TDD tests for Slice-5 Task 2: role-aware Action Hub with resolve + polling.
/// These tests were written BEFORE implementation (TDD: RED first).
/// </summary>
public class ActionHubPageTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ActionHubPageTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── helpers ────────────────────────────────────────────────────────────────

    private static async Task<(OrkabiAppFactory factory, HttpClient client, int userId)>
        CreateInstructorClientAsync(SqliteFixture sqlite, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"instr.hub{suffix}@test.test";
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
        CreateAdminClientAsync(SqliteFixture sqlite, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"admin.hub{suffix}@test.test";
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

    private static ActionItem MakeAdminItem(string description = "חריגת קצב: כיתה טסט · דגם טסט — בוצעו 9 שיעורים מתוך 8 צפויים.") =>
        new ActionItem
        {
            Type = ActionItemType.Gap,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.Admin,
            AssignedToUserId = null,
            Description = description,
        };

    private static ActionItem MakeInstructorUserItem(int instructorUserId, string description = "יום הולדת מחר: דנה לוי.") =>
        new ActionItem
        {
            Type = ActionItemType.Birthday,
            Status = ActionItemStatus.Open,
            AssignedToRole = null,
            AssignedToUserId = instructorUserId,
            Description = description,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };

    private static ActionItem MakeInstructorRoleItem(string description = "מעקב ניסיון: יש ליצור קשר לגבי ריטה שוורץ.") =>
        new ActionItem
        {
            Type = ActionItemType.TryoutFollowup,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.Instructor,
            AssignedToUserId = null,
            Description = description,
        };

    // ── test 1: anonymous → 302 ─────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_redirected_from_action_hub()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var resp = await client.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location?.ToString());
    }

    // ── test 2: Instructor sees their items, NOT Admin-only items ────────────

    [Fact]
    public async Task Instructor_sees_only_their_items_not_admin_items()
    {
        var (factory, client, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_scope");

        // Seed an Admin-only Gap item + an Instructor user-assigned Birthday item
        string adminDesc = $"חריגת_קצב_סקופ_{Guid.NewGuid():N}"[..40];
        string instrDesc = $"יום_הולדת_סקופ_{Guid.NewGuid():N}"[..40];
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ActionItems.Add(new ActionItem
            {
                Type = ActionItemType.Gap,
                Status = ActionItemStatus.Open,
                AssignedToRole = AppRoles.Admin,
                AssignedToUserId = null,
                Description = adminDesc
            });
            db.ActionItems.Add(new ActionItem
            {
                Type = ActionItemType.Birthday,
                Status = ActionItemStatus.Open,
                AssignedToRole = null,
                AssignedToUserId = instrUserId,
                Description = instrDesc,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
            });
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        // Must contain the instructor's item
        Assert.Contains(instrDesc, body);
        // Must NOT contain the Admin-only item
        Assert.DoesNotContain(adminDesc, body);

        factory.Dispose();
    }

    // ── test 3: Admin can resolve an action item; DB → Resolved + DeduplicationKey null ─

    [Fact]
    public async Task Admin_resolves_action_item_card_removed_and_db_resolved()
    {
        var (factory, client, adminUserId) = await CreateAdminClientAsync(_sqlite, "_resolve");

        int itemId;
        string dedupKey = $"gap_resolve_test_{Guid.NewGuid():N}"[..40];
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new ActionItem
            {
                Type = ActionItemType.Gap,
                Status = ActionItemStatus.Open,
                AssignedToRole = AppRoles.Admin,
                AssignedToUserId = null,
                Description = "חריגת קצב: כיתה טסט · דגם טסט — בוצעו 9 שיעורים מתוך 8 צפויים.",
                DeduplicationKey = dedupKey
            };
            db.ActionItems.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        }

        // GET the page to get the antiforgery token
        var getResp = await client.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // POST to resolve
        var postResp = await client.PostAsync(
            $"/Operations/ActionItems?handler=Resolve&id={itemId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        // Resolve returns 200 (content swap) or redirect
        Assert.True(
            postResp.StatusCode == HttpStatusCode.OK || postResp.StatusCode == HttpStatusCode.Redirect,
            $"Expected 200 or redirect from resolve handler, got {postResp.StatusCode}");

        // DB: item must be Resolved and DeduplicationKey null
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.ActionItems.FindAsync(itemId);
            Assert.NotNull(item);
            Assert.Equal(ActionItemStatus.Resolved, item!.Status);
            Assert.Null(item.DeduplicationKey);
        }

        factory.Dispose();
    }

    // ── test 4: Instructor CANNOT resolve an Admin-assigned item (authz-leak test) ──

    [Fact]
    public async Task Instructor_cannot_resolve_admin_assigned_item()
    {
        var (instrFactory, instrClient, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_authzleak");

        // Seed an Admin-role item (instructor should NOT be able to resolve this)
        int adminItemId;
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new ActionItem
            {
                Type = ActionItemType.Gap,
                Status = ActionItemStatus.Open,
                AssignedToRole = AppRoles.Admin,
                AssignedToUserId = null,
                Description = "חריגת קצב: כיתה טסט אדמין · דגם טסט."
            };
            db.ActionItems.Add(item);
            await db.SaveChangesAsync();
            adminItemId = item.Id;
        }

        // Instructor GETs /Operations/ActionItems (now open to all authenticated)
        var getResp = await instrClient.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        // Instructor attempts to resolve the Admin item
        var postResp = await instrClient.PostAsync(
            $"/Operations/ActionItems?handler=Resolve&id={adminItemId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        // Must be forbidden (302 → AccessDenied, or 403)
        Assert.True(
            postResp.StatusCode == HttpStatusCode.Redirect ||
            postResp.StatusCode == HttpStatusCode.Forbidden,
            $"Expected redirect or 403, got {postResp.StatusCode}");
        if (postResp.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("AccessDenied", postResp.Headers.Location?.ToString() ?? "");

        // DB: item must still be Open
        using (var s = instrFactory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.ActionItems.FindAsync(adminItemId);
            Assert.NotNull(item);
            Assert.Equal(ActionItemStatus.Open, item!.Status);
        }

        instrFactory.Dispose();
    }

    // ── test 5: polling fragment ?handler=List returns 200 with open items ──

    [Fact]
    public async Task Polling_fragment_returns_open_items_200()
    {
        var (factory, client, _) = await CreateAdminClientAsync(_sqlite, "_poll");

        // Seed one open item
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ActionItems.Add(new ActionItem
            {
                Type = ActionItemType.Dispute,
                Status = ActionItemStatus.Open,
                AssignedToRole = AppRoles.Admin,
                AssignedToUserId = null,
                Description = "מחלוקת: כיתה פולינג · דגם פולינג."
            });
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync("/Operations/ActionItems?handler=List");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        // The fragment must contain action-card markup
        Assert.Contains("action-card", body);

        factory.Dispose();
    }

    // ── test 6: empty state renders when no open items ───────────────────────

    [Fact]
    public async Task Empty_state_renders_when_no_items_in_queue()
    {
        // Use a fresh factory with a unique user so no seeded items overlap
        var (factory, client, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_empty_hub");

        // Do NOT seed any items assigned to this instructor
        var resp = await client.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        // The empty state title should appear
        Assert.Contains("אין משימות פתוחות", body);

        factory.Dispose();
    }

    // ── test 7: page title / hub copy renders ────────────────────────────────

    [Fact]
    public async Task Action_hub_page_title_and_hebrew_copy_render()
    {
        var (factory, client, _) = await CreateAdminClientAsync(_sqlite, "_title");

        var resp = await client.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("מרכז הפעולות", body);
        Assert.Contains("מתעדכן אוטומטית", body);
        Assert.Contains("סמן כטופל", body);

        factory.Dispose();
    }

    // ── test 8: Instructor can resolve their own user-assigned item ──────────

    [Fact]
    public async Task Instructor_can_resolve_their_own_user_assigned_item()
    {
        var (factory, client, instrUserId) = await CreateInstructorClientAsync(_sqlite, "_own_resolve");

        int itemId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new ActionItem
            {
                Type = ActionItemType.Birthday,
                Status = ActionItemStatus.Open,
                AssignedToRole = null,
                AssignedToUserId = instrUserId,
                Description = "יום הולדת מחר: תלמיד טסט.",
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
            };
            db.ActionItems.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        }

        var getResp = await client.GetAsync("/Operations/ActionItems");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await client.PostAsync(
            $"/Operations/ActionItems?handler=Resolve&id={itemId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.True(
            postResp.StatusCode == HttpStatusCode.OK || postResp.StatusCode == HttpStatusCode.Redirect,
            $"Expected 200 or redirect, got {postResp.StatusCode}");

        // DB: item must be Resolved
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.ActionItems.FindAsync(itemId);
            Assert.NotNull(item);
            Assert.Equal(ActionItemStatus.Resolved, item!.Status);
        }

        factory.Dispose();
    }
}
