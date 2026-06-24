using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Modules.Operations;

public class OperationsService
{
    private readonly AppDbContext _db;

    public OperationsService(AppDbContext db)
    {
        _db = db;
    }

    // ── Extra Hours ──────────────────────────────────────────────────────────

    public async Task<ExtraHours> SubmitExtraHoursAsync(
        int shiftInstanceId, int instructorId, decimal hours, string reason)
    {
        var record = new ExtraHours
        {
            ShiftInstanceId = shiftInstanceId,
            InstructorId = instructorId,
            Hours = hours,
            Reason = reason,
            Status = ExtraHoursStatus.Pending
        };
        _db.ExtraHours.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    public async Task ApproveExtraHoursAsync(int extraHoursId, int approverUserId)
    {
        var record = await _db.ExtraHours
            .FirstOrDefaultAsync(e => e.Id == extraHoursId && e.Status == ExtraHoursStatus.Pending)
            ?? throw new InvalidOperationException($"דיווח שעות {extraHoursId} לא נמצא או שאינו ממתין");

        record.Status = ExtraHoursStatus.Approved;
        record.ApprovedByUserId = approverUserId;
        record.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public Task<List<ExtraHours>> ListPendingExtraHoursAsync() =>
        _db.ExtraHours
            .Where(e => e.Status == ExtraHoursStatus.Pending)
            .Include(e => e.Instructor)
            .Include(e => e.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .Include(e => e.ApprovedByUser)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

    public Task<List<ExtraHours>> ListExtraHoursByInstructorAsync(int instructorId) =>
        _db.ExtraHours
            .Where(e => e.InstructorId == instructorId)
            .Include(e => e.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .Include(e => e.ApprovedByUser)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

    // ── Incident Report ──────────────────────────────────────────────────────

    public async Task<IncidentReport> SubmitIncidentReportAsync(
        int shiftInstanceId, int instructorId, IncidentSeverity severity, string description)
    {
        var report = new IncidentReport
        {
            ShiftInstanceId = shiftInstanceId,
            InstructorId = instructorId,
            Severity = severity,
            Description = description
        };
        _db.IncidentReports.Add(report);
        await _db.SaveChangesAsync();
        return report;
    }

    public Task<List<IncidentReport>> ListIncidentsAsync(IncidentSeverity? severity = null) =>
        _db.IncidentReports
            .Where(r => severity == null || r.Severity == severity)
            .Include(r => r.Instructor)
            .Include(r => r.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

    // ── Vacation Request ─────────────────────────────────────────────────────

    public async Task<VacationRequest> RequestVacationAsync(
        int instructorId, DateOnly startDate, DateOnly endDate, string? reason = null)
    {
        var todayIsrael = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));

        if (startDate > endDate)
            throw new ArgumentException("תאריך הסיום חייב להיות אחרי תאריך ההתחלה");
        if (startDate < todayIsrael)
            throw new ArgumentException("תאריך ההתחלה חייב להיות היום או בעתיד");

        var vacation = new VacationRequest
        {
            InstructorId = instructorId,
            StartDate = startDate,
            EndDate = endDate,
            Reason = reason,
            Status = VacationStatus.Pending
        };
        _db.VacationRequests.Add(vacation);
        await _db.SaveChangesAsync();
        return vacation;
    }

    public async Task ApproveVacationAsync(int vacationRequestId, int approverUserId)
    {
        var vacation = await _db.VacationRequests
            .FirstOrDefaultAsync(v => v.Id == vacationRequestId && v.Status == VacationStatus.Pending)
            ?? throw new InvalidOperationException($"בקשת חופשה {vacationRequestId} לא נמצאה או שאינה ממתינה");

        vacation.Status = VacationStatus.Approved;
        vacation.ApprovedByUserId = approverUserId;
        vacation.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DenyVacationAsync(int vacationRequestId, int approverUserId, string? adminNote)
    {
        var vacation = await _db.VacationRequests
            .FirstOrDefaultAsync(v => v.Id == vacationRequestId && v.Status == VacationStatus.Pending)
            ?? throw new InvalidOperationException($"בקשת חופשה {vacationRequestId} לא נמצאה או שאינה ממתינה");

        vacation.Status = VacationStatus.Denied;
        vacation.ApprovedByUserId = approverUserId;
        vacation.ApprovedAt = DateTime.UtcNow;
        vacation.AdminNote = adminNote;
        await _db.SaveChangesAsync();
    }

    public Task<List<VacationRequest>> ListPendingVacationsAsync() =>
        _db.VacationRequests
            .Where(v => v.Status == VacationStatus.Pending)
            .Include(v => v.Instructor)
            .Include(v => v.ApprovedByUser)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

    public Task<List<VacationRequest>> ListVacationsByInstructorAsync(int instructorId) =>
        _db.VacationRequests
            .Where(v => v.InstructorId == instructorId)
            .Include(v => v.ApprovedByUser)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
}
