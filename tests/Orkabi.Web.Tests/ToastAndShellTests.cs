using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Logistics;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// Slice-5 Task 7: save-success toasts (HX-Trigger showToast), shared page-shell
/// partial, and the layout-level toast container. Written BEFORE implementation
/// (TDD: RED first).
/// </summary>
public class ToastAndShellTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ToastAndShellTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── helpers ────────────────────────────────────────────────────────────────

    private static async Task<(OrkabiAppFactory factory, HttpClient client, int userId)>
        CreateUserClientAsync(SqliteFixture sqlite, string role, string suffix)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using (var s = factory.Services.CreateScope())
        {
            var um = s.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"{role.ToLower()}.ts{suffix}@test.test";
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

    /// <summary>
    /// Seeds School + AcademicYear + Class + Model + a Pending LogisticsOrder.
    /// Returns the new order's id so the Pack handler can be invoked against it.
    /// </summary>
    private static async Task<int> SeedPendingOrderAsync(OrkabiAppFactory factory, string tag)
    {
        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();

        var school = new School { Name = $"Sch-{tag}-{Guid.NewGuid():N}"[..20], City = "Tel Aviv" };
        var year = new AcademicYear
        {
            Label = $"Y-{tag}-{Guid.NewGuid():N}"[..10],
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30)
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Class
        {
            Name = $"C-{tag}-{Guid.NewGuid():N}"[..15],
            SchoolId = school.Id,
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var model = new Orkabi.Web.Modules.Curriculum.Model { Name = $"Mdl-{tag}-{Guid.NewGuid():N}"[..15] };
        db.Models.Add(model);
        await db.SaveChangesAsync();

        var order = new LogisticsOrder
        {
            ClassId = cls.Id,
            ModelId = model.Id,
            Quantity = 6,
            Status = LogisticsOrderStatus.Pending
        };
        db.LogisticsOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    // ── A. Resolve success carries HX-Trigger showToast header ───────────────────

    [Fact]
    public async Task Resolve_success_response_carries_HX_Trigger_showToast_header()
    {
        var (factory, client, _) = await CreateUserClientAsync(_sqlite, AppRoles.Admin, "_resolve_toast");

        int itemId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new ActionItem
            {
                Type = ActionItemType.Gap,
                Status = ActionItemStatus.Open,
                AssignedToRole = AppRoles.Admin,
                AssignedToUserId = null,
                Description = "חריגת קצב: כיתה טוסט · דגם טוסט."
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

        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        Assert.True(postResp.Headers.Contains("HX-Trigger"), "Response is missing HX-Trigger header.");
        var trigger = string.Join(" ", postResp.Headers.GetValues("HX-Trigger"));
        Assert.Contains("showToast", trigger);

        factory.Dispose();
    }

    // ── B. Pack success carries HX-Trigger showToast header ──────────────────────

    [Fact]
    public async Task Pack_success_response_carries_HX_Trigger_showToast_header()
    {
        var (factory, client, _) = await CreateUserClientAsync(_sqlite, AppRoles.Logistics, "_pack_toast");

        var orderId = await SeedPendingOrderAsync(factory, "pt");

        var getResp = await client.GetAsync("/Logistics/PackingList");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var token = AntiForgery.Extract(await getResp.Content.ReadAsStringAsync());

        var postResp = await client.PostAsync(
            $"/Logistics/PackingList?handler=Pack&id={orderId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        Assert.True(postResp.Headers.Contains("HX-Trigger"), "Response is missing HX-Trigger header.");
        var trigger = string.Join(" ", postResp.Headers.GetValues("HX-Trigger"));
        Assert.Contains("showToast", trigger);

        factory.Dispose();
    }

    // ── C. Page-shell renders title + active subnav on packing list ──────────────

    [Fact]
    public async Task PageShell_partial_renders_title_and_active_subnav_on_packing_list()
    {
        var (factory, client, _) = await CreateUserClientAsync(_sqlite, AppRoles.Logistics, "_shell");

        var resp = await client.GetAsync("/Logistics/PackingList");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("עורקבי", body);        // wordmark
        Assert.Contains("לוגיסטיקה", body);      // section title
        Assert.Contains("רשימת אריזה", body);    // active subnav label
        Assert.Contains("is-active", body);      // active-state class present

        factory.Dispose();
    }

    // ── D. Layout renders the toast container ────────────────────────────────────

    [Fact]
    public async Task Layout_renders_toast_container()
    {
        var (factory, client, _) = await CreateUserClientAsync(_sqlite, AppRoles.Logistics, "_container");

        var resp = await client.GetAsync("/Logistics/PackingList");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();

        Assert.Contains("toast-container", body);

        factory.Dispose();
    }
}
