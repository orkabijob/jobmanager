using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

/// <summary>
/// Tests for ClientService.DeactivateAsync — the event-driven mass-dropout hook.
///
/// Invariant under test: when a client is deactivated and ≥1 OTHER client in a shared class
/// also went inactive within the last 7 days, the pair together (≥2 within 7d) triggers a
/// MassDropout Admin ActionItem for that class (dedup key: dropout_mass_{classId}).
///
/// "Within 7 days" is approximated via UpdatedAt on the Client (BaseEntity).
/// The audit interceptor stamps UpdatedAt on every SaveChanges, so DeactivateAsync produces
/// an accurate timestamp for the client just deactivated. For pre-existing inactive clients
/// their UpdatedAt is whatever was last saved — an unrelated edit could bump it, but the
/// IsActive==false filter bounds the false-positive surface, and a 7-day window is tolerant
/// enough that marginal edge cases are acceptable.
/// </summary>
public class ClientDeactivationTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ClientDeactivationTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<Class> SeedClassAsync(AppDbContext db)
    {
        var school = new School { Name = $"בית ספר {Guid.NewGuid():N}", City = "תל אביב" };
        var year = new AcademicYear
        {
            Label = $"תשפ\"-{Guid.NewGuid().ToString("N")[..4]}",
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
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        return cls;
    }

    private static async Task<Client> SeedClientAsync(AppDbContext db, bool isActive = true)
    {
        var client = new Client { Name = $"לקוח-{Guid.NewGuid():N}", IsActive = isActive };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    private static async Task EnrollAsync(AppDbContext db, int clientId, int classId)
    {
        db.Enrollments.Add(new Enrollment
        {
            ClientId = clientId,
            ClassId = classId,
            Status = EnrollmentStatus.Active,
            EnrolledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Directly sets IsActive=false and UpdatedAt to a past timestamp via parameterized SQL,
    /// bypassing the audit interceptor so we can simulate a deactivation at a specific time.
    /// This is test-only; production paths always go through DeactivateAsync.
    /// </summary>
    private static async Task SetClientInactiveWithTimestampAsync(AppDbContext db, int clientId, DateTime updatedAt)
    {
        await db.Database.ExecuteSqlAsync(
            $"UPDATE Clients SET IsActive = 0, UpdatedAt = {updatedAt:O} WHERE Id = {clientId}");
        db.ChangeTracker.Clear();
    }

    // ── Happy path: 3-client class, 2 prior inactive within 7d → MassDropout item ─────────

    [Fact]
    public async Task DeactivateAsync_two_prior_inactive_within_7d_creates_mass_dropout_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClientService>();

        var cls = await SeedClassAsync(db);
        var client1 = await SeedClientAsync(db);
        var client2 = await SeedClientAsync(db);
        var client3 = await SeedClientAsync(db);

        await EnrollAsync(db, client1.Id, cls.Id);
        await EnrollAsync(db, client2.Id, cls.Id);
        await EnrollAsync(db, client3.Id, cls.Id);

        // Simulate client1 and client2 having gone inactive 2 days ago (within 7d window).
        var twoDaysAgo = DateTime.UtcNow.AddDays(-2);
        await SetClientInactiveWithTimestampAsync(db, client1.Id, twoDaysAgo);
        await SetClientInactiveWithTimestampAsync(db, client2.Id, twoDaysAgo);

        // Deactivate client3 via the service — finds 2 other inactive within 7d → trigger.
        await svc.DeactivateAsync(client3.Id);

        var dedupKey = $"dropout_mass_{cls.Id}";
        var item = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        Assert.NotNull(item);
        Assert.Equal(AppRoles.Admin, item!.AssignedToRole);
        Assert.Equal(ActionItemType.Absence, item.Type);
    }

    // ── Threshold: exactly 1 other inactive within 7d → 2 total → item triggered ─────────

    [Fact]
    public async Task DeactivateAsync_exactly_one_prior_inactive_within_7d_triggers_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClientService>();

        var cls = await SeedClassAsync(db);
        var clientPrior = await SeedClientAsync(db);
        var clientNow = await SeedClientAsync(db);

        await EnrollAsync(db, clientPrior.Id, cls.Id);
        await EnrollAsync(db, clientNow.Id, cls.Id);

        // Exactly 1 prior inactive within 7d.
        var threeDaysAgo = DateTime.UtcNow.AddDays(-3);
        await SetClientInactiveWithTimestampAsync(db, clientPrior.Id, threeDaysAgo);

        // DeactivateAsync on clientNow: 1 other inactive within 7d → current makes it 2 → trigger.
        await svc.DeactivateAsync(clientNow.Id);

        var dedupKey = $"dropout_mass_{cls.Id}";
        var item = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        Assert.NotNull(item);
    }

    // ── No-trigger: only 1 client deactivated (no prior inactive in class) → no item ──────

    [Fact]
    public async Task DeactivateAsync_single_deactivation_no_prior_inactive_creates_no_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClientService>();

        var cls = await SeedClassAsync(db);
        var client1 = await SeedClientAsync(db);
        var client2 = await SeedClientAsync(db);  // active, stays active

        await EnrollAsync(db, client1.Id, cls.Id);
        await EnrollAsync(db, client2.Id, cls.Id);

        // Only deactivate client1 — no prior inactive in this class.
        await svc.DeactivateAsync(client1.Id);

        var dedupKey = $"dropout_mass_{cls.Id}";
        var item = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        Assert.Null(item);
    }

    // ── No-trigger: prior inactive is outside 7d window (10 days ago) ────────────────────

    [Fact]
    public async Task DeactivateAsync_prior_inactive_outside_7d_window_creates_no_item()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClientService>();

        var cls = await SeedClassAsync(db);
        var clientOld = await SeedClientAsync(db);  // inactive 10 days ago — outside window
        var clientNew = await SeedClientAsync(db);  // deactivated now via DeactivateAsync

        await EnrollAsync(db, clientOld.Id, cls.Id);
        await EnrollAsync(db, clientNew.Id, cls.Id);

        // Set clientOld inactive with UpdatedAt 10 days ago (outside the 7-day window).
        var tenDaysAgo = DateTime.UtcNow.AddDays(-10);
        await SetClientInactiveWithTimestampAsync(db, clientOld.Id, tenDaysAgo);

        // Deactivate clientNew — should find 0 OTHER inactive within 7d.
        await svc.DeactivateAsync(clientNew.Id);

        var dedupKey = $"dropout_mass_{cls.Id}";
        var item = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        Assert.Null(item);
    }

    // ── Idempotent: DeactivateAsync on already-inactive client → no-op, no new item ──────

    [Fact]
    public async Task DeactivateAsync_already_inactive_client_is_noop_no_item_created()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClientService>();

        var cls = await SeedClassAsync(db);
        var client = await SeedClientAsync(db, isActive: false);  // already inactive

        await EnrollAsync(db, client.Id, cls.Id);

        // Should be a no-op: does not throw, does not create any item.
        await svc.DeactivateAsync(client.Id);

        var dedupKey = $"dropout_mass_{cls.Id}";
        var count = await db.ActionItems.CountAsync(a => a.DeduplicationKey == dedupKey && a.Status == ActionItemStatus.Open);
        Assert.Equal(0, count);
    }

    // ── Class isolation: inactive clients in OTHER classes do not trigger ─────────────────

    [Fact]
    public async Task DeactivateAsync_inactive_in_other_class_does_not_trigger_for_unrelated_class()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ClientService>();

        var cls1 = await SeedClassAsync(db);  // target class: client deactivated here
        var cls2 = await SeedClassAsync(db);  // other class: inactive clients here should not count

        var clientInCls1 = await SeedClientAsync(db);
        var inactiveInCls2a = await SeedClientAsync(db);
        var inactiveInCls2b = await SeedClientAsync(db);

        await EnrollAsync(db, clientInCls1.Id, cls1.Id);
        await EnrollAsync(db, inactiveInCls2a.Id, cls2.Id);
        await EnrollAsync(db, inactiveInCls2b.Id, cls2.Id);

        // Make two clients inactive in cls2 within 7d — should not affect cls1.
        var oneDayAgo = DateTime.UtcNow.AddDays(-1);
        await SetClientInactiveWithTimestampAsync(db, inactiveInCls2a.Id, oneDayAgo);
        await SetClientInactiveWithTimestampAsync(db, inactiveInCls2b.Id, oneDayAgo);

        // Deactivate clientInCls1 — the other inactives are in cls2, not cls1.
        await svc.DeactivateAsync(clientInCls1.Id);

        var dedupKey1 = $"dropout_mass_{cls1.Id}";
        var item1 = await db.ActionItems.FirstOrDefaultAsync(a => a.DeduplicationKey == dedupKey1 && a.Status == ActionItemStatus.Open);
        Assert.Null(item1);  // no trigger for cls1
    }
}
