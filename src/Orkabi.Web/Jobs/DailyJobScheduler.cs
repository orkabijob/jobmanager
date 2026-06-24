using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Orkabi.Web.Data;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Jobs;

/// <summary>
/// Thin timer shell that drives daily jobs with catch-up-on-wake semantics and
/// exactly-once execution via <see cref="JobExecutionLog"/> idempotency.
/// Runs every 5 minutes; an immediate pass fires on startup (Render wake-up catch-up).
/// Dormant under Testing environment — the loop never starts so test-suite timers are
/// not affected (the service still registers, starts, and parks on Task.Delay(Infinite)).
/// </summary>
public sealed class DailyJobScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _factory;
    private readonly ILogger<DailyJobScheduler> _logger;
    private readonly IHostEnvironment _env;

    public DailyJobScheduler(
        IServiceScopeFactory factory,
        ILogger<DailyJobScheduler> logger,
        IHostEnvironment env)
    {
        _factory = factory;
        _logger = logger;
        _env = env;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Testing gate: park forever and let the test host's cancellation token stop us cleanly.
        // The service is still registered and starts — it just never enters the real loop,
        // so no timer, no DB writes, and no interference with SQLite-based tests.
        if (_env.IsEnvironment("Testing"))
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        // Immediate catch-up pass on startup / Render wake (runs the jobs if they haven't
        // fired yet today — idempotency ensures a duplicate run on the same day is a no-op).
        await TryCatchPassAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryCatchPassAsync(stoppingToken);
        }
    }

    /// <summary>Wraps RunPassAsync so a failure in one pass doesn't kill the loop.</summary>
    private async Task TryCatchPassAsync(CancellationToken ct)
    {
        try
        {
            await RunPassAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host is shutting down — propagate so the loop exits cleanly.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DailyJobScheduler: unhandled error in RunPassAsync — will retry next tick");
        }
    }

    /// <summary>
    /// One scheduler tick: run both daily jobs (each in its own scope), then drain the outbox
    /// (also in its own scope). The JobExecutionLog unique index guarantees exactly-once
    /// execution per job per civil date even if multiple ticks fire or multiple instances run.
    /// </summary>
    private async Task RunPassAsync(CancellationToken ct)
    {
        var todayIsrael = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));

        await TryRunJobAsync(
            "BirthdayCheck",
            todayIsrael,
            (runner, date, token) => runner.RunBirthdayCheckAsync(date, token),
            ct);

        await TryRunJobAsync(
            "ShiftGeneration",
            todayIsrael,
            (runner, date, token) => runner.RunShiftGenerationAsync(date, token),
            ct);

        // Drain the outbox in its own dedicated scope — never share a DbContext across jobs.
        using var drainScope = _factory.CreateScope();
        await drainScope.ServiceProvider.GetRequiredService<IOutboxDrainer>().DrainAsync(ct);
    }

    /// <summary>
    /// Attempts to run <paramref name="body"/> for today's date under exactly-once semantics:
    /// <list type="bullet">
    ///   <item>Fast-skip: if a <see cref="JobExecutionLog"/> already exists for (jobName, today), return immediately.</item>
    ///   <item>Race guard: INSERT the log row; if a concurrent writer wins, SaveChanges throws
    ///         <see cref="DbUpdateException"/> (unique-index violation) → skip.</item>
    ///   <item>Execute: invoke <paramref name="body"/>; update Status to Succeeded or Failed.</item>
    /// </list>
    /// Each call creates its own DI scope and <see cref="AppDbContext"/> — never shares state
    /// with other jobs or the outbox drain.
    /// </summary>
    private async Task TryRunJobAsync(
        string jobName,
        DateOnly today,
        Func<IDailyJobRunner, DateOnly, CancellationToken, Task> body,
        CancellationToken ct)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Fast-skip: already ran (or another instance is running) for today.
        if (await db.JobExecutionLogs.AnyAsync(
                j => j.JobName == jobName && j.ScheduledFor == today, ct))
        {
            _logger.LogDebug("DailyJobScheduler: {Job} already logged for {Date} — skipping", jobName, today);
            return;
        }

        // Insert-first: claim the run slot before executing so concurrent schedulers are locked out.
        var log = new JobExecutionLog
        {
            JobName     = jobName,
            ScheduledFor = today,
            RanAt       = DateTime.UtcNow,
            Status      = JobExecutionStatus.Started,
        };
        db.JobExecutionLogs.Add(log);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another instance inserted first — the unique index rejected us; skip.
            db.ChangeTracker.Clear();
            _logger.LogDebug("DailyJobScheduler: {Job} for {Date} already claimed by a concurrent instance — skipping", jobName, today);
            return;
        }

        // Execute the job logic; update the status row when done.
        var runner = scope.ServiceProvider.GetRequiredService<IDailyJobRunner>();
        try
        {
            await body(runner, today, ct);
            log.Status = JobExecutionStatus.Succeeded;
            _logger.LogInformation("DailyJobScheduler: {Job} completed for {Date}", jobName, today);
        }
        catch (Exception ex)
        {
            log.Status = JobExecutionStatus.Failed;
            _logger.LogError(ex, "DailyJobScheduler: {Job} FAILED for {Date}", jobName, today);
        }
        finally
        {
            // Persist the final status (Succeeded or Failed).
            // Don't pass ct here — status flush must succeed even on cancellation.
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
