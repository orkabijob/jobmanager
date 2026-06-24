using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class ActionHubEntityTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public ActionHubEntityTests(SqliteFixture sqlite) => _sqlite = sqlite;

    // ── ActionItem: partial unique index on DeduplicationKey ──────────────

    [Fact]
    public async Task Duplicate_non_null_deduplication_key_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();

        using (var scope1 = factory.Services.CreateScope())
        {
            var db = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ActionItems.Add(new ActionItem
            {
                Type = ActionItemType.Task,
                Status = ActionItemStatus.Open,
                Description = "ראשון",
                DeduplicationKey = "dedup-key-001"
            });
            await db.SaveChangesAsync();
        }

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.ActionItems.Add(new ActionItem
        {
            Type = ActionItemType.Task,
            Status = ActionItemStatus.Open,
            Description = "שני — אסור",
            DeduplicationKey = "dedup-key-001"  // same non-null key → must be rejected
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Multiple_null_deduplication_keys_are_allowed()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ActionItems.Add(new ActionItem { Type = ActionItemType.Gap, Description = "ראשון ללא מפתח" });
        db.ActionItems.Add(new ActionItem { Type = ActionItemType.Gap, Description = "שני ללא מפתח" });
        db.ActionItems.Add(new ActionItem { Type = ActionItemType.Gap, Description = "שלישי ללא מפתח" });

        // Partial index only covers non-null keys, so all three nulls must save fine
        await db.SaveChangesAsync();

        var count = await db.ActionItems.CountAsync(a => a.DeduplicationKey == null);
        Assert.True(count >= 3);
    }

    // ── OutboxEvent round-trip ────────────────────────────────────────────

    [Fact]
    public async Task OutboxEvent_roundtrip_with_null_scheduled_and_processed()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var created = DateTime.UtcNow;
        db.OutboxEvents.Add(new Shared.OutboxEvent
        {
            EventType = "TestEvent",
            Payload = "{\"key\":\"value\"}",
            CreatedAt = created,
            ScheduledFor = null,
            ProcessedAt = null
        });
        await db.SaveChangesAsync();

        var loaded = await db.OutboxEvents.FirstAsync(e => e.EventType == "TestEvent");
        Assert.Equal("TestEvent", loaded.EventType);
        Assert.Equal("{\"key\":\"value\"}", loaded.Payload);
        Assert.Null(loaded.ScheduledFor);
        Assert.Null(loaded.ProcessedAt);
    }

    [Fact]
    public async Task OutboxEvent_roundtrip_with_scheduled_and_processed_set()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var scheduled = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var processed = new DateTime(2026, 7, 1, 12, 5, 0, DateTimeKind.Utc);
        var created = new DateTime(2026, 7, 1, 11, 59, 0, DateTimeKind.Utc);

        db.OutboxEvents.Add(new Shared.OutboxEvent
        {
            EventType = "ScheduledEvent",
            Payload = "{\"scheduled\":true}",
            CreatedAt = created,
            ScheduledFor = scheduled,
            ProcessedAt = processed
        });
        await db.SaveChangesAsync();

        var loaded = await db.OutboxEvents.FirstAsync(e => e.EventType == "ScheduledEvent");
        Assert.Equal(scheduled, loaded.ScheduledFor);
        Assert.Equal(processed, loaded.ProcessedAt);
    }
}
