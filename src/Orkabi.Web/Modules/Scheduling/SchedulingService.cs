using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
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
    /// Resolves the "current" lesson model for a shift's class via the class's syllabus
    /// (first model by OrderIndex — the Slice-2 rule; precise "first incomplete" is a later
    /// refinement), then GETS-OR-CREATES the LessonLog for this shift instance with that model.
    /// Returns (null, null) when the class has no syllabus or the syllabus has no models —
    /// the caller then shows the "טרם שובץ דגם" state and disables submit (no LessonLog created).
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

        // No log yet — resolve the first syllabus model of the shift's class.
        var firstModel = await (
            from i in _db.ShiftInstances
            where i.Id == shiftInstanceId
            join c in _db.Classes.IgnoreQueryFilters() on i.Template.ClassId equals c.Id
            where c.SyllabusId != null
            from sm in _db.SyllabusModels.Where(sm => sm.SyllabusId == c.SyllabusId)
            orderby sm.OrderIndex
            select new { sm.ModelId, sm.Model.Name }
        ).FirstOrDefaultAsync();

        if (firstModel is null)
            return (null, null, null);   // no syllabus / no models → caller disables submit

        var log = await SaveLessonLogAsync(shiftInstanceId, firstModel.ModelId, LessonLogStatus.InProgress, null);
        return (log.Id, firstModel.ModelId, firstModel.Name);
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

            var log = new LessonLog
            {
                ShiftInstanceId = shiftInstanceId,
                ModelId = modelId,
                Status = status,
                InstructorNotes = notes,
                ExpectedLessonsSnapshot = model.ExpectedLessonsToComplete   // captured at CREATE only
            };
            _db.LessonLogs.Add(log);
            await _db.SaveChangesAsync();
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

        var request = await _db.SubstitutionRequests
            .Include(r => r.ShiftInstance)
            .FirstOrDefaultAsync(r => r.Id == substitutionRequestId && r.Status == SubstitutionStatus.Pending)
            ?? throw new InvalidOperationException($"בקשת החלפה {substitutionRequestId} לא נמצאה או שאינה ממתינה");

        request.Status = SubstitutionStatus.Approved;
        request.ApprovedByUserId = approverUserId;
        request.ApprovedAt = DateTime.UtcNow;
        request.ShiftInstance.ActualInstructorId = request.SubstituteInstructorId;

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
            return attendance;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();

            // Try to find by idempotency key first (same-key retry = idempotent return).
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
