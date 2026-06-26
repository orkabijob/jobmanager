using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class InstructorHomeTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public InstructorHomeTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Seed helpers (mirror SchedulingServiceTests style) ─────────────────────

    private static async Task<AppUser> SeedInstructorAsync(IServiceProvider sp, string? fullName = null)
    {
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var email = $"instr-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = fullName };
        var result = await users.CreateAsync(user, "Passw0rd!");
        if (!result.Succeeded)
            throw new InvalidOperationException("Seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, AppRoles.Instructor);
        return user;
    }

    private static async Task<(Class cls, AcademicYear year)> SeedClassAsync(AppDbContext db, string className)
    {
        var school = new School { Name = $"בית-ספר-{Guid.NewGuid():N}", City = "תל אביב" };
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        var year = new AcademicYear
        {
            Label = $"שנה-{Guid.NewGuid():N}"[..9],
            StartDate = today.AddDays(-30),
            EndDate = today.AddDays(120),
            IsCurrent = false
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Class { Name = className, School = school, AcademicYear = year, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        return (cls, year);
    }

    private static async Task<ShiftInstance> SeedInstanceAsync(
        AppDbContext db, Class cls, AcademicYear year, AppUser instructor, DateOnly date)
    {
        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DefaultInstructorId = instructor.Id,
            DayOfWeek = date.DayOfWeek,
            StartTime = new TimeOnly(16, 0),
            EndTime = new TimeOnly(17, 30),
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        var instance = new ShiftInstance
        {
            TemplateId = template.Id,
            ActualInstructorId = instructor.Id,
            Date = date,
            Status = ShiftInstanceStatus.Scheduled
        };
        db.ShiftInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    [Fact]
    public async Task Instructor_home_shows_only_my_today_shift()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));

        string myClassName = $"כיתה-שלי-{Guid.NewGuid():N}"[..18];
        string otherClassName = $"כיתה-אחר-{Guid.NewGuid():N}"[..18];
        string myEmail;
        using (var s = factory.Services.CreateScope())
        {
            var sp = s.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var me = await SeedInstructorAsync(sp, "רון");
            myEmail = me.Email!;
            var other = await SeedInstructorAsync(sp);

            var (myCls, myYear) = await SeedClassAsync(db, myClassName);
            await SeedInstanceAsync(db, myCls, myYear, me, today);

            // Another instructor's today shift — must NOT show on my home.
            var (otherCls, otherYear) = await SeedClassAsync(db, otherClassName);
            await SeedInstanceAsync(db, otherCls, otherYear, other, today);
        }

        var client = await TestLogin.SignInAsync(factory, myEmail, "Passw0rd!");
        var resp = await client.GetAsync("/Dashboard/Instructor");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains(myClassName, body);
        Assert.DoesNotContain(otherClassName, body);
        Assert.Contains("קח נוכחות", body);   // the monolith

        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_home_shows_empty_state_when_no_shifts_today()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();

        string email;
        using (var s = factory.Services.CreateScope())
        {
            email = (await SeedInstructorAsync(s.ServiceProvider)).Email!;
        }

        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        var resp = await client.GetAsync("/Dashboard/Instructor");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("אין לך מפגשים היום", body);   // empty-state title
        Assert.DoesNotContain("קח נוכחות", body);        // no monolith when empty

        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_home_shows_tickets_strip_when_open_item_exists()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();

        string email;
        string desc = $"יום_הולדת_מחר_{Guid.NewGuid():N}"[..30];
        using (var s = factory.Services.CreateScope())
        {
            var sp = s.ServiceProvider;
            var me = await SeedInstructorAsync(sp, "רון");
            email = me.Email!;

            var db = sp.GetRequiredService<AppDbContext>();
            db.ActionItems.Add(new ActionItem
            {
                Type = ActionItemType.Birthday,
                Status = ActionItemStatus.Open,
                AssignedToRole = null,
                AssignedToUserId = me.Id,
                Description = desc,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
            });
            await db.SaveChangesAsync();
        }

        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        var resp = await client.GetAsync("/Dashboard/Instructor");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.Contains("המשימות שלי", body);          // tickets section label
        Assert.Contains(desc, body);                    // the ticket itself
        Assert.Contains("פתח את מרכז הפעולות", body);   // link to the hub

        factory.Dispose();
    }

    [Fact]
    public async Task Instructor_home_omits_tickets_strip_when_no_open_items()
    {
        var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();

        string email;
        using (var s = factory.Services.CreateScope())
        {
            email = (await SeedInstructorAsync(s.ServiceProvider)).Email!;
        }

        var client = await TestLogin.SignInAsync(factory, email, "Passw0rd!");
        var resp = await client.GetAsync("/Dashboard/Instructor");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        // No open tickets → the whole strip is omitted (deliberate per design §3.4).
        Assert.DoesNotContain("המשימות שלי", body);

        factory.Dispose();
    }
}
