namespace Orkabi.Web.Shared;

/// <summary>
/// Infrastructure job execution log — NOT BaseEntity.
/// The audit interceptor (AuditSaveChangesInterceptor) only stamps BaseEntity subclasses,
/// so this entity is not touched by the interceptor.
/// The unique index on (JobName, ScheduledFor) acts as the idempotency gate:
/// a scheduler must insert a row here before running; a duplicate insert
/// throws DbUpdateException, meaning the job already ran for that date.
/// </summary>
public class JobExecutionLog
{
    public int Id { get; set; }

    /// <summary>Discriminator / job name, max 100 chars.</summary>
    public string JobName { get; set; } = "";

    /// <summary>The Israel civil date the run is FOR (the idempotency key date).</summary>
    public DateOnly ScheduledFor { get; set; }

    /// <summary>UTC timestamp when this execution started.</summary>
    public DateTime RanAt { get; set; }

    /// <summary>Execution outcome.</summary>
    public JobExecutionStatus Status { get; set; }
}

public enum JobExecutionStatus
{
    Started = 0,
    Succeeded = 1,
    Failed = 2
}
