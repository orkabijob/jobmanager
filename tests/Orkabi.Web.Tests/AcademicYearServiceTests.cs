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

public class AcademicYearServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public AcademicYearServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static string Label() => $"y-{Guid.NewGuid():N}"[..18];

    // Seeds fromYear + toYear + a school + an Active class (with one Active shift-template) +
    // an enrollment in that class (which rollover must NOT copy). Returns the ids + class name.
    private static async Task<(int fromYearId, int toYearId, string className, int fromClassId)> SeedForRolloverAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var um = sp.GetRequiredService<UserManager<AppUser>>();

        var fromYear = new AcademicYear { Label = Label(), StartDate = new DateOnly(2024, 9, 1), EndDate = new DateOnly(2025, 6, 30), IsCurrent = false };
        var toYear = new AcademicYear { Label = Label(), StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), IsCurrent = false };
        var school = new School { Name = $"בס-{Guid.NewGuid():N}", City = "חיפה" };
        db.AcademicYears.AddRange(fromYear, toYear);
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        var email = $"roll-instr-{Guid.NewGuid():N}@t.test";
        var instr = new AppUser { UserName = email, Email = email };
        await um.CreateAsync(instr, "Passw0rd!");

        var className = $"כתה-{Guid.NewGuid():N}"[..14];
        var cls = new Class { Name = className, SchoolId = school.Id, AcademicYearId = fromYear.Id, Status = EntityStatus.Active };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        db.ShiftTemplates.Add(new ShiftTemplate { ClassId = cls.Id, DefaultInstructorId = instr.Id, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), AcademicYearId = fromYear.Id, Status = EntityStatus.Active });
        var client = new Client { Name = $"תלמיד-{Guid.NewGuid():N}"[..12], IsActive = true };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        db.Enrollments.Add(new Enrollment { ClientId = client.Id, ClassId = cls.Id, Status = EnrollmentStatus.Active, EnrolledAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        return (fromYear.Id, toYear.Id, className, cls.Id);
    }

    [Fact]
    public async Task RollOver_clones_active_classes_and_templates_not_enrollments()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<AcademicYearService>();

        var (fromId, toId, className, _) = await SeedForRolloverAsync(sp);

        var result = await svc.RollOverAsync(fromId, toId);

        Assert.Equal(1, result.ClassesCopied);
        Assert.Equal(1, result.TemplatesCopied);

        db.ChangeTracker.Clear();
        var newClass = await db.Classes.SingleAsync(c => c.AcademicYearId == toId && c.Name == className);
        Assert.Equal(EntityStatus.Active, newClass.Status);
        Assert.Equal(1, await db.ShiftTemplates.CountAsync(t => t.ClassId == newClass.Id));
        Assert.Equal(0, await db.Enrollments.CountAsync(e => e.ClassId == newClass.Id));   // enrollments NOT copied
    }

    [Fact]
    public async Task RollOver_is_idempotent()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<AcademicYearService>();

        var (fromId, toId, className, _) = await SeedForRolloverAsync(sp);

        await svc.RollOverAsync(fromId, toId);
        var second = await svc.RollOverAsync(fromId, toId);

        Assert.Equal(0, second.ClassesCopied);   // already rolled over → nothing new
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Classes.CountAsync(c => c.AcademicYearId == toId && c.Name == className));   // no duplicate
    }

    [Fact]
    public async Task RollOver_to_self_throws()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AcademicYearService>();

        var (fromId, _, _, _) = await SeedForRolloverAsync(sp);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RollOverAsync(fromId, fromId));
    }

    [Fact]
    public async Task RollOver_skips_archived_source_classes()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<AcademicYearService>();

        var (fromId, toId, _, fromClassId) = await SeedForRolloverAsync(sp);
        var cls = await db.Classes.FindAsync(fromClassId);
        cls!.Status = EntityStatus.Archived;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await svc.RollOverAsync(fromId, toId);
        Assert.Equal(0, result.ClassesCopied);   // archived source class excluded by the global filter
    }

    [Fact]
    public async Task CreateAsync_adds_a_non_current_year()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<AcademicYearService>();

        var label = Label();
        var created = await svc.CreateAsync(label, new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30));

        Assert.True(created.Id > 0);
        Assert.False(created.IsCurrent);   // creating a year never makes it current

        db.ChangeTracker.Clear();
        var reloaded = await db.AcademicYears.FindAsync(created.Id);
        Assert.Equal(label, reloaded!.Label);
        Assert.Equal(new DateOnly(2026, 9, 1), reloaded.StartDate);
        Assert.Equal(new DateOnly(2027, 6, 30), reloaded.EndDate);
        Assert.False(reloaded.IsCurrent);
    }

    [Fact]
    public async Task SetCurrentAsync_is_exclusive_across_transitions()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var svc = sp.GetRequiredService<AcademicYearService>();

        // Insert both as non-current (avoids any pre-existing-current partial-index clash on the shared db).
        var y1 = new AcademicYear { Label = Label(), StartDate = new DateOnly(2024, 9, 1), EndDate = new DateOnly(2025, 6, 30), IsCurrent = false };
        var y2 = new AcademicYear { Label = Label(), StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30), IsCurrent = false };
        db.AcademicYears.AddRange(y1, y2);
        await db.SaveChangesAsync();

        await svc.SetCurrentAsync(y1.Id);
        db.ChangeTracker.Clear();
        Assert.True((await db.AcademicYears.FindAsync(y1.Id))!.IsCurrent);

        await svc.SetCurrentAsync(y2.Id);
        db.ChangeTracker.Clear();
        var all = await db.AcademicYears.ToListAsync();
        Assert.Single(all.Where(y => y.IsCurrent));                          // exactly one current, table-wide
        Assert.True((await db.AcademicYears.FindAsync(y2.Id))!.IsCurrent);
        Assert.False((await db.AcademicYears.FindAsync(y1.Id))!.IsCurrent);  // previous current was cleared
    }

    [Fact]
    public async Task SetCurrentAsync_unknown_id_throws()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AcademicYearService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SetCurrentAsync(999999));
    }
}
