using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;

namespace Orkabi.Web.Shared;

/// <summary>
/// Processes outbox events. Currently the Real-Gap monitor: a "LessonLogSaved" event
/// recomputes how many lessons a class has spent on a model and, when over pace (and the
/// model isn't already marked completed for that class), ensures an Admin gap action item.
///
/// IDEMPOTENCY: each event is stamped ProcessedAt only AFTER its handler succeeds. A handler
/// throw leaves ProcessedAt null → the event is retried on a later drain. Re-processing the
/// same event is safe because EnsureGapActionItemAsync is itself idempotent (dedup-key index).
/// </summary>
public class OutboxDrainer : IOutboxDrainer
{
    private readonly AppDbContext _db;
    private readonly ActionItemService _actionHub;
    private readonly ILogger<OutboxDrainer> _logger;

    public OutboxDrainer(AppDbContext db, ActionItemService actionHub, ILogger<OutboxDrainer> logger)
    {
        _db = db;
        _actionHub = actionHub;
        _logger = logger;
    }

    public async Task DrainAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Oldest-first, capped batch of unprocessed, due events.
        var events = await _db.OutboxEvents
            .Where(e => e.ProcessedAt == null && (e.ScheduledFor == null || e.ScheduledFor <= now))
            .OrderBy(e => e.Id)
            .Take(50)
            .ToListAsync(ct);

        foreach (var evt in events)
        {
            try
            {
                await ProcessEventAsync(evt, ct);
                // Stamp ONLY after the handler succeeds (and after EnsureGapActionItemAsync's
                // own SaveChanges + any ChangeTracker.Clear on its dedup-race path), so this
                // change to evt is never discarded. evt is re-attached + tracked below.
                evt.ProcessedAt = DateTime.UtcNow;
                _db.OutboxEvents.Update(evt);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Do NOT stamp ProcessedAt — the event retries on a later drain.
                _logger.LogError(ex,
                    "Failed to process outbox event {EventId} ({EventType}); will retry.",
                    evt.Id, evt.EventType);
                // Drop any partial tracked state from the failed handler so the next event
                // in the batch (and the next drain) starts clean.
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task ProcessEventAsync(OutboxEvent evt, CancellationToken ct)
    {
        switch (evt.EventType)
        {
            case "LessonLogSaved":
                await HandleLessonLogSavedAsync(evt, ct);
                break;

            case "AttendanceAbsent":
                await HandleAttendanceAbsentAsync(evt, ct);
                break;

            case "TryoutPresent":
                await HandleTryoutPresentAsync(evt, ct);
                break;

            case "IncidentSevere":
                await HandleIncidentSevereAsync(evt, ct);
                break;

            default:
                // Unknown type — log and let it be marked processed (avoid infinite retry).
                _logger.LogWarning("Unknown outbox event type {EventType} (id {EventId}); marking processed.",
                    evt.EventType, evt.Id);
                break;
        }
    }

    private async Task HandleLessonLogSavedAsync(OutboxEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<LessonLogSavedPayload>(evt.Payload)
            ?? throw new InvalidOperationException($"Empty LessonLogSaved payload (event {evt.Id}).");

        var classId = payload.classId;
        var modelId = payload.modelId;

        // IgnoreQueryFilters is REQUIRED: the ShiftTemplate archival filter would otherwise drop
        // lessons under an archived template, undercounting spent. Real pace counts ALL lessons.
        var spent = await _db.LessonLogs.IgnoreQueryFilters()
            .Where(l => l.ModelId == modelId && l.ShiftInstance.Template.ClassId == classId)
            .CountAsync(ct);

        var model = await _db.Models.FindAsync(new object?[] { modelId }, ct);
        if (model is null)
        {
            _logger.LogWarning("LessonLogSaved for missing model {ModelId} (event {EventId}); skipping.",
                modelId, evt.Id);
            return;
        }

        var alreadyCompleted = await _db.LessonLogs.IgnoreQueryFilters()
            .AnyAsync(l => l.ModelId == modelId
                        && l.ShiftInstance.Template.ClassId == classId
                        && l.Status == LessonLogStatus.Completed, ct);

        // LIVE expected (spec §5A), not the per-log snapshot. +1 tolerance band before flagging.
        if (spent > model.ExpectedLessonsToComplete + 1 && !alreadyCompleted)
            await _actionHub.EnsureGapActionItemAsync(classId, modelId, model.ExpectedLessonsToComplete, spent);
    }

    private async Task HandleAttendanceAbsentAsync(OutboxEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AttendanceAbsentPayload>(evt.Payload)
            ?? throw new InvalidOperationException($"Empty AttendanceAbsent payload (event {evt.Id}).");

        var clientId = payload.clientId;
        var lessonLogId = payload.lessonLogId;

        // Resolve classId and the date of this lesson (IgnoreQueryFilters: archived templates).
        var thisLesson = await _db.LessonLogs.IgnoreQueryFilters()
            .Where(l => l.Id == lessonLogId)
            .Select(l => new { classId = l.ShiftInstance.Template.ClassId, date = l.ShiftInstance.Date })
            .FirstOrDefaultAsync(ct);

        if (thisLesson is null)
        {
            _logger.LogWarning("AttendanceAbsent: LessonLog {LessonLogId} not found (event {EventId}); skipping.",
                lessonLogId, evt.Id);
            return;
        }

        // Find the client's most recent prior attendance in the same class (date < thisDate).
        var previous = await _db.Attendances.IgnoreQueryFilters()
            .Where(a => a.ClientId == clientId
                     && a.LessonLog.ShiftInstance.Template.ClassId == thisLesson.classId
                     && a.LessonLog.ShiftInstance.Date < thisLesson.date)
            .OrderByDescending(a => a.LessonLog.ShiftInstance.Date)
            .Select(a => a.Status)
            .FirstOrDefaultAsync(ct);

        // If previous attendance was also Absent → double consecutive absence.
        if (previous == AttendanceStatus.Absent)
            await _actionHub.EnsureDoubleAbsenceActionItemAsync(clientId, thisLesson.classId);
        // Else: no-op; the event will still be stamped processed.
    }

    private async Task HandleTryoutPresentAsync(OutboxEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<TryoutPresentPayload>(evt.Payload)
            ?? throw new InvalidOperationException($"Empty TryoutPresent payload (event {evt.Id}).");

        var clientId = payload.clientId;
        var classId = payload.classId;

        // Re-verify still tryout before acting (enrollment status may have changed since the event was queued).
        var stillTryout = await _db.Enrollments
            .AnyAsync(e => e.ClientId == clientId && e.ClassId == classId &&
                           (e.Status == EnrollmentStatus.Tryout || e.IsTryout), ct);

        if (stillTryout)
            await _actionHub.EnsureTryoutFollowupActionItemAsync(clientId, classId);
        // Else: no-op; the event will still be stamped processed.
    }

    private async Task HandleIncidentSevereAsync(OutboxEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<IncidentSeverePayload>(evt.Payload)
            ?? throw new InvalidOperationException($"Empty IncidentSevere payload (event {evt.Id}).");

        // EnsureSevereIncidentActionItemAsync is idempotent (dedup-key index), so a retry is safe.
        await _actionHub.EnsureSevereIncidentActionItemAsync(payload.incidentReportId);
    }

    // Mirrors the anonymous type serialized in SchedulingService.SaveLessonLogAsync.
    private sealed record LessonLogSavedPayload(int classId, int modelId);

    // Mirrors the anonymous type serialized in OperationsService.SubmitIncidentReportAsync.
    private sealed record IncidentSeverePayload(int incidentReportId);

    // Mirrors the anonymous types serialized in SchedulingService.RecordAttendanceAsync.
    private sealed record AttendanceAbsentPayload(int clientId, int lessonLogId);
    private sealed record TryoutPresentPayload(int clientId, int classId);
}
