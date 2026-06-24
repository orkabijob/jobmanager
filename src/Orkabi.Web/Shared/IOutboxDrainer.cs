namespace Orkabi.Web.Shared;

/// <summary>
/// Drains the OutboxEvent table: processes each unprocessed, due event exactly once
/// (idempotently — failed events are left unstamped to retry on a later drain).
/// </summary>
public interface IOutboxDrainer
{
    Task DrainAsync(CancellationToken ct = default);
}
