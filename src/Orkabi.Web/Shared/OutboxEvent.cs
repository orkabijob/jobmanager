namespace Orkabi.Web.Shared;

/// <summary>
/// Infrastructure outbox event — NOT BaseEntity.
/// The audit interceptor (AuditSaveChangesInterceptor) only stamps BaseEntity subclasses,
/// so CreatedAt is set explicitly by the service layer, not the interceptor.
/// </summary>
public class OutboxEvent
{
    public int Id { get; set; }

    /// <summary>Discriminator / event name, max 100 chars.</summary>
    public string EventType { get; set; } = "";

    /// <summary>JSON payload stored as plain text (not jsonb) — portable across SQLite + Npgsql.</summary>
    public string Payload { get; set; } = "";

    /// <summary>UTC scheduled delivery time; null means drain immediately.</summary>
    public DateTime? ScheduledFor { get; set; }

    /// <summary>UTC creation timestamp; set by the service, NOT the audit interceptor.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC processing timestamp; null means the event has not yet been processed.</summary>
    public DateTime? ProcessedAt { get; set; }
}
