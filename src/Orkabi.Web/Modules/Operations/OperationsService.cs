using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Modules.Operations;

public class OperationsService
{
    private readonly AppDbContext _db;
    private readonly ActionItemService _actionHub;

    public OperationsService(AppDbContext db, ActionItemService actionHub)
    {
        _db = db;
        _actionHub = actionHub;
    }

    // ── Extra Hours ──────────────────────────────────────────────────────────

    public async Task<ExtraHours> SubmitExtraHoursAsync(
        int shiftInstanceId, int instructorId, decimal hours, string reason)
    {
        var ownsShift = await _db.ShiftInstances.IgnoreQueryFilters()
            .AnyAsync(i => i.Id == shiftInstanceId && i.ActualInstructorId == instructorId);
        if (!ownsShift)
            throw new InvalidOperationException("אין הרשאה לדווח עבור משמרת זו");

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

    // Mirror of approve: deny a pending extra-hours report. Reuses ApprovedBy*/ApprovedAt as the
    // reviewer + decision timestamp (same shape as DenyVacationAsync, which also reuses them).
    public async Task DenyExtraHoursAsync(int extraHoursId, int approverUserId)
    {
        var record = await _db.ExtraHours
            .FirstOrDefaultAsync(e => e.Id == extraHoursId && e.Status == ExtraHoursStatus.Pending)
            ?? throw new InvalidOperationException($"דיווח שעות {extraHoursId} לא נמצא או שאינו ממתין");

        record.Status = ExtraHoursStatus.Denied;
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
        var ownsShift = await _db.ShiftInstances.IgnoreQueryFilters()
            .AnyAsync(i => i.Id == shiftInstanceId && i.ActualInstructorId == instructorId);
        if (!ownsShift)
            throw new InvalidOperationException("אין הרשאה לדווח עבור משמרת זו");

        var report = new IncidentReport
        {
            ShiftInstanceId = shiftInstanceId,
            InstructorId = instructorId,
            Severity = severity,
            Description = description
        };

        // A High-severity incident raises an Admin action item via the outbox kernel. One transaction
        // so the report and its event commit atomically; the drainer creates the ticket after commit.
        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.IncidentReports.Add(report);
        await _db.SaveChangesAsync();   // report.Id is assigned here

        if (severity == IncidentSeverity.High)
        {
            _db.OutboxEvents.Add(new OutboxEvent
            {
                EventType = "IncidentSevere",
                Payload = JsonSerializer.Serialize(new { incidentReportId = report.Id }),
                CreatedAt = DateTime.UtcNow,
                ScheduledFor = null
            });
            await _db.SaveChangesAsync();
        }

        await tx.CommitAsync();
        return report;
    }

    public Task<List<IncidentReport>> ListIncidentsAsync(IncidentSeverity? severity = null) =>
        _db.IncidentReports
            .Where(r => severity == null || r.Severity == severity)
            .Include(r => r.Instructor)
            .Include(r => r.ShiftInstance).ThenInclude(i => i.Template).ThenInclude(t => t.Class)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

    /// <summary>
    /// Admin closes an incident (Open or Escalated → Closed) and clears its severe-incident action
    /// item if one is open — closing the loop. One transaction so the status + ticket commit together.
    /// </summary>
    public async Task CloseIncidentAsync(int incidentId, int adminUserId)
    {
        var incident = await _db.IncidentReports.FindAsync(incidentId)
            ?? throw new InvalidOperationException($"דיווח אירוע {incidentId} לא נמצא");
        if (incident.Status == IncidentStatus.Closed)
            throw new InvalidOperationException($"דיווח אירוע {incidentId} כבר סגור");

        await using var tx = await _db.Database.BeginTransactionAsync();
        incident.Status = IncidentStatus.Closed;
        await _db.SaveChangesAsync();

        var ticket = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.DeduplicationKey == $"severe_incident_{incidentId}"
                                   && a.Status == ActionItemStatus.Open);
        if (ticket is not null)
            await _actionHub.ResolveActionItemAsync(ticket.Id, adminUserId);

        await tx.CommitAsync();
    }

    /// <summary>Admin escalates an Open incident (Open → Escalated). The severe ticket stays open.</summary>
    public async Task EscalateIncidentAsync(int incidentId, int adminUserId)
    {
        var incident = await _db.IncidentReports.FindAsync(incidentId)
            ?? throw new InvalidOperationException($"דיווח אירוע {incidentId} לא נמצא");
        if (incident.Status != IncidentStatus.Open)
            throw new InvalidOperationException("ניתן להסלים רק דיווח פתוח");

        incident.Status = IncidentStatus.Escalated;
        await _db.SaveChangesAsync();
    }

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
