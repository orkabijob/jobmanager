using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Modules.Scheduling;

public class SchedulingService
{
    private readonly AppDbContext _db;
    private readonly IShiftInstanceGenerator _generator;
    private readonly EnrollmentService _enrollmentService;

    public SchedulingService(AppDbContext db, IShiftInstanceGenerator generator, EnrollmentService enrollmentService)
    {
        _db = db;
        _generator = generator;
        _enrollmentService = enrollmentService;
    }

    /// <summary>
    /// Resolves the "current" lesson model for a shift's class (the first not-yet-completed syllabus
    /// model — see ResolveCurrentModelForClassAsync), then GETS-OR-CREATES the LessonLog for this
    /// shift instance with that model. Returns (null, null) when the class has no syllabus or the
    /// syllabus has no models — the caller then shows the "טרם שובץ דגם" state and disables submit.
    /// </summary>
    public async Task<(int? lessonLogId, int? modelId, string? modelName)>
        ResolveLessonLogForAttendanceAsync(int shiftInstanceId)
    {
        // An existing log wins — it already pins the model chosen for this lesson.
        var existing = await _db.LessonLogs
            .Include(l => l.Model)
            .FirstOrDefaultAsync(l => l.ShiftInstanceId == shiftInstanceId);
        if (existing is not null)
            return (existing.Id, existing.ModelId, existing.Model?.Name);

        // No log yet — resolve the class's CURRENT model (first not-yet-completed), then create it.
        var classId = await _db.ShiftInstances.IgnoreQueryFilters()
            .Where(i => i.Id == shiftInstanceId)
            .Select(i => (int?)i.Template.ClassId)
            .FirstOrDefaultAsync();
        if (classId is null)
            return (null, null, null);

        var (modelId, modelName) = await ResolveCurrentModelForClassAsync(classId.Value);
        if (modelId is null)
            return (null, null, null);   // no syllabus / no models → caller disables submit

        var log = await SaveLessonLogAsync(shiftInstanceId, modelId.Value, LessonLogStatus.InProgress, null);
        return (log.Id, modelId, modelName);
    }

    /// <summary>
    /// The class's "current" syllabus model = the first (by OrderIndex) whose count of Completed
    /// LessonLogs for this class is still below the model's ExpectedLessonsToComplete. If every model
    /// is complete, returns the last model. Returns (null, null) when the class has no syllabus or
    /// the syllabus has no models. (F20 — previously this was frozen at model #1.)
    /// </summary>
    public async Task<(int? modelId, string? modelName)> ResolveCurrentModelForClassAsync(int classId)
    {
        var syllabusId = await _db.Classes.IgnoreQueryFilters()
            .Where(c => c.Id == classId)
            .Select(c => c.SyllabusId)
            .FirstOrDefaultAsync();
        if (syllabusId is null)
            return (null, null);

        var models = await _db.SyllabusModels
            .Where(sm => sm.SyllabusId == syllabusId)
            .OrderBy(sm => sm.OrderIndex)
            .Select(sm => new { sm.ModelId, sm.Model.Name, sm.Model.ExpectedLessonsToComplete })
            .ToListAsync();
        if (models.Count == 0)
            return (null, null);

        // Completed lesson counts per model for THIS class. IgnoreQueryFilters so a lesson taught
        // under a since-archived template still counts as real progress (same reasoning as the
        // OutboxDrainer's Real-Gap monitor). NOTE: ComputePacingAsync does NOT yet use it — a
        // pre-existing inconsistency tracked as tech-debt, not changed here.
        var completed = await _db.LessonLogs.IgnoreQueryFilters()
            .Where(l => l.Status == LessonLogStatus.Completed
                     && l.ShiftInstance.Template.ClassId == classId)
            .GroupBy(l => l.ModelId)
            .Select(g => new { ModelId = g.Key, Count = g.Count() })
            .ToListAsync();
        var completedByModel = completed.ToDictionary(x => x.ModelId, x => x.Count);

        var current = models.FirstOrDefault(m =>
            (completedByModel.TryGetValue(m.ModelId, out var c) ? c : 0) < m.ExpectedLessonsToComplete)
            ?? models[^1];

        return (current.ModelId, current.Name);
    }

    // ── Template management ──────────────────────────────────────────────────

    public async Task<ShiftTemplate> CreateTemplateAsync(ShiftTemplate t)
    {
        _db.ShiftTemplates.Add(t);
        await _db.SaveChangesAsync();
        await _generator.GenerateForTemplateAsync(t.Id);
        return t;
    }

    public async Task UpdateTemplateAsync(ShiftTemplate t)
    {
        _db.ShiftTemplates.Update(t);
        await _db.SaveChangesAsync();
        await _generator.GenerateForTemplateAsync(t.Id);
    }

    public async Task ArchiveTemplateAsync(int templateId)
    {
        var template = await _db.ShiftTemplates.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"תבנית {templateId} לא נמצאה");
        template.Status = EntityStatus.Archived;
        await _db.SaveChangesAsync();
    }

    public Task<List<ShiftTemplate>> ListTemplatesAsync(int? classId, int? academicYearId) =>
        _db.ShiftTemplates
            .Where(t => (classId == null || t.ClassId == classId)
                     && (academicYearId == null || t.AcademicYearId == academicYearId))
            .Include(t => t.Class)
            .Include(t => t.DefaultInstructor)
            .Include(t => t.AcademicYear)
            .OrderBy(t => t.DayOfWeek)
            .ToListAsync();

    // ── Instance queries ─────────────────────────────────────────────────────

    public Task<List<ShiftInstance>> ListInstancesAsync(DateOnly from, DateOnly to) =>
        _db.ShiftInstances
            .Where(i => i.Date >= from && i.Date <= to)
            .Include(i => i.Template).ThenInclude(t => t.Class)
            .Include(i => i.ActualInstructor)
            .OrderBy(i => i.Date)
            .ToListAsync();

    public Task<List<ShiftInstance>> ListTodayForInstructorAsync(int userId)
    {
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        return _db.ShiftInstances
            .Where(i => i.Date == today && i.ActualInstructorId == userId)
            .Include(i => i.Template).ThenInclude(t => t.Class)
            .ToListAsync();
    }

    /// <summary>Returns the instructor's most recent ShiftInstances (up to 30) for use in operation submit forms.</summary>
    public Task<List<ShiftInstance>> ListRecentForInstructorAsync(int userId, int take = 30)
    {
        var cutoff = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz)).AddDays(-60);
        return _db.ShiftInstances
            .Where(i => i.ActualInstructorId == userId && i.Date >= cutoff)
            .Include(i => i.Template).ThenInclude(t => t.Class)
            .OrderByDescending(i => i.Date)
            .Take(take)
            .ToListAsync();
    }

    // Single shift instance with its Template→Class (+ School) loaded — for the attendance/log surfaces.
    public Task<ShiftInstance?> GetShiftInstanceAsync(int shiftInstanceId) =>
        _db.ShiftInstances
            .Include(i => i.Template).ThenInclude(t => t.Class).ThenInclude(c => c.School)
            .FirstOrDefaultAsync(i => i.Id == shiftInstanceId);

    // Security-critical: DB query only, no caching, date-scoped to today Israel.
    public Task<bool> CanAccessShiftAsync(int shiftInstanceId, int userId)
    {
        var todayIsrael = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
        return _db.ShiftInstances.AnyAsync(i =>
            i.Id == shiftInstanceId && i.ActualInstructorId == userId && i.Date == todayIsrael);
    }

    // ── Lesson log ───────────────────────────────────────────────────────────

    public async Task<LessonLog> SaveLessonLogAsync(
        int shiftInstanceId, int modelId, LessonLogStatus status, string? notes)
    {
        var existing = await _db.LessonLogs
            .FirstOrDefaultAsync(l => l.ShiftInstanceId == shiftInstanceId);

        if (existing is null)
        {
            var model = await _db.Models.FindAsync(modelId)
                ?? throw new InvalidOperationException($"מודל {modelId} לא נמצא");

            // Resolve the owning class via shift → template (IgnoreQueryFilters so an archived
            // template still resolves) — carried in the outbox payload for the Real-Gap monitor.
            var classId = await _db.ShiftInstances.IgnoreQueryFilters()
                .Where(i => i.Id == shiftInstanceId)
                .Select(i => i.Template.ClassId)
                .FirstAsync();

            var log = new LessonLog
            {
                ShiftInstanceId = shiftInstanceId,
                ModelId = modelId,
                Status = status,
                InstructorNotes = notes,
                ExpectedLessonsSnapshot = model.ExpectedLessonsToComplete   // captured at CREATE only
            };

            var evt = new OutboxEvent
            {
                EventType = "LessonLogSaved",
                Payload = JsonSerializer.Serialize(new { classId, modelId }),
                CreatedAt = DateTime.UtcNow,
                ScheduledFor = null
            };

            // ONE transaction: the LessonLog and its outbox event commit atomically (or not at
            // all). Do NOT nest transactions — SQLite errors on nested. The drainer reads the
            // event only after this commit; the dedup-key index backstops any double-drain.
            await using var tx = await _db.Database.BeginTransactionAsync();
            _db.LessonLogs.Add(log);
            _db.OutboxEvents.Add(evt);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return log;
        }

        // Update — never overwrite the snapshot.
        existing.ModelId = modelId;
        existing.Status = status;
        existing.InstructorNotes = notes;
        await _db.SaveChangesAsync();
        return existing;
    }

    // The LessonLog for a shift instance (if one exists), with its Model loaded.
    public Task<LessonLog?> GetLessonLogAsync(int shiftInstanceId) =>
        _db.LessonLogs.Include(l => l.Model)
            .FirstOrDefaultAsync(l => l.ShiftInstanceId == shiftInstanceId);

    // "X of N" — how many lessons of this model has this class completed, vs. expected.
    public async Task<(int spent, int expected, bool over)> ComputePacingAsync(int classId, int modelId)
    {
        // Count Completed lesson logs for this model across all shift instances for this class's templates.
        var spent = await _db.LessonLogs
            .Where(l => l.ModelId == modelId
                     && l.Status == LessonLogStatus.Completed
                     && l.ShiftInstance.Template.ClassId == classId)
            .CountAsync();

        var model = await _db.Models.FindAsync(modelId)
            ?? throw new InvalidOperationException($"מודל {modelId} לא נמצא");

        var expected = model.ExpectedLessonsToComplete;
        return (spent, expected, spent > expected);
    }

    // ── Substitution ─────────────────────────────────────────────────────────

    public async Task<SubstitutionRequest> RequestSubstitutionAsync(
        int shiftInstanceId, int requestingInstructorId, int substituteInstructorId)
    {
        var request = new SubstitutionRequest
        {
            ShiftInstanceId = shiftInstanceId,
            RequestingInstructorId = requestingInstructorId,
            SubstituteInstructorId = substituteInstructorId,
            Status = SubstitutionStatus.Pending
        };
        _db.SubstitutionRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    public async Task ApproveSubstitutionAsync(int substitutionRequestId, int approverUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        // IgnoreQueryFilters so the Template→Class chain loads even for an archived class/template
        // (needed for the notification text below).
        var request = await _db.SubstitutionRequests
            .IgnoreQueryFilters()
            .Include(r => r.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .FirstOrDefaultAsync(r => r.Id == substitutionRequestId && r.Status == SubstitutionStatus.Pending)
            ?? throw new InvalidOperationException($"בקשת החלפה {substitutionRequestId} לא נמצאה או שאינה ממתינה");

        request.Status = SubstitutionStatus.Approved;
        request.ApprovedByUserId = approverUserId;
        request.ApprovedAt = DateTime.UtcNow;
        request.ShiftInstance.ActualInstructorId = request.SubstituteInstructorId;

        // F14: notify the two affected instructors (user-assigned action items — they surface in the
        // instructor dashboard "my tickets"). Approve runs once per request (Pending-guarded), so the
        // dedup keys also guard against any double-fire.
        var className = request.ShiftInstance.Template?.Class?.Name ?? "";
        var dateStr = request.ShiftInstance.Date.ToString("dd/MM/yyyy");
        _db.ActionItems.Add(new ActionItem
        {
            Type = ActionItemType.Task,
            Status = ActionItemStatus.Open,
            AssignedToUserId = request.SubstituteInstructorId,
            AssignedToRole = null,
            RelatedEntityId = request.ShiftInstanceId,
            DeduplicationKey = $"sub_assigned_{substitutionRequestId}",
            Description = $"שובצת כמחליף/ה למשמרת: כיתה {className} · {dateStr}."
        });
        _db.ActionItems.Add(new ActionItem
        {
            Type = ActionItemType.Task,
            Status = ActionItemStatus.Open,
            AssignedToUserId = request.RequestingInstructorId,
            AssignedToRole = null,
            RelatedEntityId = request.ShiftInstanceId,
            DeduplicationKey = $"sub_reassigned_{substitutionRequestId}",
            Description = $"המשמרת שלך הועברה למחליף/ה: כיתה {className} · {dateStr}."
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task DenySubstitutionAsync(int substitutionRequestId, int approverUserId)
    {
        var request = await _db.SubstitutionRequests
            .FirstOrDefaultAsync(r => r.Id == substitutionRequestId && r.Status == SubstitutionStatus.Pending)
            ?? throw new InvalidOperationException($"בקשת החלפה {substitutionRequestId} לא נמצאה או שאינה ממתינה");

        request.Status = SubstitutionStatus.Denied;
        request.ApprovedByUserId = approverUserId;
        request.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task CancelSubstitutionAsync(int substitutionRequestId, int requestingInstructorId)
    {
        var request = await _db.SubstitutionRequests
            .FirstOrDefaultAsync(r => r.Id == substitutionRequestId
                                   && r.RequestingInstructorId == requestingInstructorId
                                   && r.Status == SubstitutionStatus.Pending)
            ?? throw new InvalidOperationException($"בקשת החלפה {substitutionRequestId} לא נמצאה");

        request.Status = SubstitutionStatus.Cancelled;
        await _db.SaveChangesAsync();
    }

    public Task<List<SubstitutionRequest>> ListPendingSubstitutionsAsync() =>
        _db.SubstitutionRequests
            .Where(r => r.Status == SubstitutionStatus.Pending)
            .Include(r => r.RequestingInstructor)
            .Include(r => r.SubstituteInstructor)
            .Include(r => r.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .OrderBy(r => r.ShiftInstance.Date)
            .ToListAsync();

    // ── Attendance ───────────────────────────────────────────────────────────

    public async Task<Attendance> RecordAttendanceAsync(
        int lessonLogId, int clientId, AttendanceStatus status, string idempotencyKey)
    {
        var attendance = new Attendance
        {
            LessonLogId = lessonLogId,
            ClientId = clientId,
            Status = status,
            IdempotencyKey = idempotencyKey
        };
        _db.Attendances.Add(attendance);

        try
        {
            await _db.SaveChangesAsync();
            // ── CREATE path only ─────────────────────────────────────────────
            // Attendance row was newly created.  Resolve classId once (IgnoreQueryFilters
            // so an archived template chain still resolves) and fire the appropriate outbox
            // event.  These appends are AFTER the attendance commit (append-after-commit):
            // a missed outbox ticket is preferable to rolling back the attendance row.
            var classId = await _db.LessonLogs.IgnoreQueryFilters()
                .Where(l => l.Id == lessonLogId)
                .Select(l => l.ShiftInstance.Template.ClassId)
                .FirstAsync();

            if (status == AttendanceStatus.Absent)
            {
                _db.OutboxEvents.Add(new OutboxEvent
                {
                    EventType = "AttendanceAbsent",
                    Payload = JsonSerializer.Serialize(new { clientId, lessonLogId }),
                    CreatedAt = DateTime.UtcNow,
                    ScheduledFor = null
                });
                await _db.SaveChangesAsync();
            }
            else if (status == AttendanceStatus.Present)
            {
                var isTryout = await _db.Enrollments.AnyAsync(e =>
                    e.ClientId == clientId && e.ClassId == classId &&
                    (e.Status == EnrollmentStatus.Tryout || e.IsTryout));

                if (isTryout)
                {
                    var nowIl = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz);
                    var t = nowIl.Date.AddDays(1).AddHours(8);
                    var schedUtc = TimeZoneInfo.ConvertTimeToUtc(t, IsraelClock.IsraelTz);

                    _db.OutboxEvents.Add(new OutboxEvent
                    {
                        EventType = "TryoutPresent",
                        Payload = JsonSerializer.Serialize(new { clientId, classId }),
                        CreatedAt = DateTime.UtcNow,
                        ScheduledFor = schedUtc
                    });
                    await _db.SaveChangesAsync();
                }
            }

            return attendance;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();

            // Try to find by idempotency key first (same-key retry = idempotent return).
            // NOTE: we do NOT append any outbox event here — the retry path must not double-fire.
            var byKey = await _db.Attendances
                .FirstOrDefaultAsync(a => a.IdempotencyKey == idempotencyKey);
            if (byKey is not null)
                return byKey;

            // (LessonLogId, ClientId) collision with a different key = logic error.
            throw new InvalidOperationException(
                $"נוכחות עבור תלמיד {clientId} בשיעור {lessonLogId} כבר קיימת עם מפתח שונה");
        }
    }

    // A batch idempotency key was "used" iff any Attendance row carries a per-row key derived
    // from it (SubmitAttendanceAsync stores "{batchKey}:{clientId}"). Lets the API report a
    // friendly 409 "already saved" without attempting (and absorbing) a duplicate write.
    public Task<bool> WasIdempotencyKeyUsedAsync(string batchKey) =>
        _db.Attendances.AnyAsync(a => a.IdempotencyKey.StartsWith(batchKey + ":"));

    public async Task<List<Attendance>> SubmitAttendanceAsync(
        int lessonLogId,
        IEnumerable<(int clientId, AttendanceStatus status)> marks,
        string idempotencyKey)
    {
        var results = new List<Attendance>();
        foreach (var (clientId, status) in marks)
        {
            // Derive a per-row key from the batch key + clientId so the globally-unique
            // IdempotencyKey index is satisfied across multiple clients in the same batch.
            // On retry the same derivation reproduces the same per-row keys → idempotent.
            var rowKey = $"{idempotencyKey}:{clientId}";
            var a = await RecordAttendanceAsync(lessonLogId, clientId, status, rowKey);
            results.Add(a);
        }
        return results;
    }
}
