using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;

namespace Orkabi.Web.Tests;

public class JobExecutionLogTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public JobExecutionLogTests(SqliteFixture sqlite) => _sqlite = sqlite;

    [Fact]
    public async Task JobExecutionLog_round_trips_on_sqlite()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var jobName = $"RoundTripJob-{Guid.NewGuid():N}";
        var ranAt = new DateTime(2026, 6, 24, 8, 0, 0, DateTimeKind.Utc);
        db.JobExecutionLogs.Add(new JobExecutionLog
        {
            JobName = jobName,
            ScheduledFor = new DateOnly(2026, 6, 24),
            RanAt = ranAt,
            Status = JobExecutionStatus.Succeeded
        });
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.JobExecutionLogs.SingleAsync(j => j.JobName == jobName);

        Assert.Equal(jobName, loaded.JobName);
        Assert.Equal(new DateOnly(2026, 6, 24), loaded.ScheduledFor);
        Assert.Equal(ranAt, loaded.RanAt);
        Assert.Equal(JobExecutionStatus.Succeeded, loaded.Status);
    }

    [Fact]
    public async Task Duplicate_job_name_and_scheduled_for_is_rejected()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();

        var jobName = $"DuplicateJob-{Guid.NewGuid():N}";

        using (var scope1 = factory.Services.CreateScope())
        {
            var db = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            db.JobExecutionLogs.Add(new JobExecutionLog
            {
                JobName = jobName,
                ScheduledFor = new DateOnly(2026, 6, 24),
                RanAt = DateTime.UtcNow,
                Status = JobExecutionStatus.Started
            });
            await db.SaveChangesAsync();
        }

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.JobExecutionLogs.Add(new JobExecutionLog
        {
            JobName = jobName,
            ScheduledFor = new DateOnly(2026, 6, 24), // same (JobName, ScheduledFor) → must be rejected
            RanAt = DateTime.UtcNow,
            Status = JobExecutionStatus.Started
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Different_scheduled_for_same_job_name_is_allowed()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var jobName = $"WeeklyReport-{Guid.NewGuid():N}";
        db.JobExecutionLogs.Add(new JobExecutionLog
        {
            JobName = jobName,
            ScheduledFor = new DateOnly(2026, 6, 23),
            RanAt = DateTime.UtcNow,
            Status = JobExecutionStatus.Succeeded
        });
        db.JobExecutionLogs.Add(new JobExecutionLog
        {
            JobName = jobName,
            ScheduledFor = new DateOnly(2026, 6, 24), // different date → must be allowed
            RanAt = DateTime.UtcNow,
            Status = JobExecutionStatus.Succeeded
        });

        await db.SaveChangesAsync(); // must not throw
        var count = await db.JobExecutionLogs.CountAsync(j => j.JobName == jobName);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Different_job_name_same_scheduled_for_is_allowed()
    {
        using var factory = new OrkabiAppFactory { ConnectionString = _sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var suffix = Guid.NewGuid().ToString("N");
        var jobA = $"JobA-{suffix}";
        var jobB = $"JobB-{suffix}";

        db.JobExecutionLogs.Add(new JobExecutionLog
        {
            JobName = jobA,
            ScheduledFor = new DateOnly(2026, 6, 24),
            RanAt = DateTime.UtcNow,
            Status = JobExecutionStatus.Succeeded
        });
        db.JobExecutionLogs.Add(new JobExecutionLog
        {
            JobName = jobB, // different job name → must be allowed
            ScheduledFor = new DateOnly(2026, 6, 24),
            RanAt = DateTime.UtcNow,
            Status = JobExecutionStatus.Succeeded
        });

        await db.SaveChangesAsync(); // must not throw
        var count = await db.JobExecutionLogs.CountAsync(j => j.JobName == jobA || j.JobName == jobB);
        Assert.Equal(2, count);
    }
}
