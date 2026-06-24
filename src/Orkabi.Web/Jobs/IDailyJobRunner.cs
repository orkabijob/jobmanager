namespace Orkabi.Web.Jobs;

public interface IDailyJobRunner
{
    /// <summary>
    /// Checks each active client's birthday against todayIsrael and emits birthday
    /// ActionItems (day-of and 24h-before). todayIsrael is injected by the caller
    /// (BackgroundService or test) so this method is timer-free and deterministic.
    /// </summary>
    Task RunBirthdayCheckAsync(DateOnly todayIsrael, CancellationToken ct = default);

    /// <summary>
    /// Delegates to IShiftInstanceGenerator.GenerateAllActiveAsync(30) to fill the
    /// 30-day rolling window for all active ShiftTemplates.
    /// </summary>
    Task RunShiftGenerationAsync(DateOnly todayIsrael, CancellationToken ct = default);
}
