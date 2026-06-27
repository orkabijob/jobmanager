using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Operations;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;
using Orkabi.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace Orkabi.Web.Tests;

public class OperationsServiceTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _sqlite;
    public OperationsServiceTests(SqliteFixture sqlite) => _sqlite = sqlite;

    private static async Task<(AppDbContext db, OperationsService ops, AppUser instructor, ShiftInstance shift)>
        SetupAsync(SqliteFixture sqlite)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var email = $"ops-instructor-{Guid.NewGuid():N}@test.test";
        var instructor = new AppUser { UserName = email, Email = email };
        await um.CreateAsync(instructor, "Passw0rd!");
        await um.AddToRoleAsync(instructor, AppRoles.Instructor);

        var school = new Orkabi.Web.Modules.People.School { Name = "School", City = "Tel Aviv" };
        var year = new Orkabi.Web.Modules.People.AcademicYear
        {
            Label = $"Year-{Guid.NewGuid():N}"[..10],
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30)
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Orkabi.Web.Modules.People.Class
        {
            Name = $"Class-{Guid.NewGuid():N}"[..15],
            SchoolId = school.Id,
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DefaultInstructorId = instructor.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        var shift = new ShiftInstance
        {
            TemplateId = template.Id,
            ActualInstructorId = instructor.Id,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = ShiftInstanceStatus.Scheduled
        };
        db.ShiftInstances.Add(shift);
        await db.SaveChangesAsync();

        // Create a FRESH db instance from the same connection string (simulating service scope)
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(sqlite.ConnectionString)
            .Options;
        var freshDb = new AppDbContext(opts);
        var ops = new OperationsService(freshDb, new ActionItemService(freshDb));

        return (freshDb, ops, instructor, shift);
    }

    [Fact]
    public async Task SubmitExtraHours_creates_pending_record()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var result = await ops.SubmitExtraHoursAsync(shift.Id, instructor.Id, 1.5m, "הכנת חומרים");
        Assert.NotNull(result);
        Assert.Equal(ExtraHoursStatus.Pending, result.Status);
        Assert.Equal(1.5m, result.Hours);
    }

    [Fact]
    public async Task ApproveExtraHours_transitions_to_approved()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var submitted = await ops.SubmitExtraHoursAsync(shift.Id, instructor.Id, 2m, "הארכת מפגש");
        await ops.ApproveExtraHoursAsync(submitted.Id, instructor.Id);
        var approved = await db.ExtraHours.FindAsync(submitted.Id);
        Assert.Equal(ExtraHoursStatus.Approved, approved!.Status);
        Assert.NotNull(approved.ApprovedAt);
    }

    [Fact]
    public async Task ApproveExtraHours_double_approve_throws()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var submitted = await ops.SubmitExtraHoursAsync(shift.Id, instructor.Id, 1m, "סיבה");
        await ops.ApproveExtraHoursAsync(submitted.Id, instructor.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ops.ApproveExtraHoursAsync(submitted.Id, instructor.Id));
    }

    [Fact]
    public async Task DenyExtraHours_transitions_to_denied()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var submitted = await ops.SubmitExtraHoursAsync(shift.Id, instructor.Id, 2m, "הארכת מפגש");
        await ops.DenyExtraHoursAsync(submitted.Id, instructor.Id);
        var denied = await db.ExtraHours.FindAsync(submitted.Id);
        Assert.Equal(ExtraHoursStatus.Denied, denied!.Status);
        Assert.Equal(instructor.Id, denied.ApprovedByUserId);
        Assert.NotNull(denied.ApprovedAt);
    }

    [Fact]
    public async Task DenyExtraHours_on_non_pending_throws()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var submitted = await ops.SubmitExtraHoursAsync(shift.Id, instructor.Id, 1m, "סיבה");
        await ops.ApproveExtraHoursAsync(submitted.Id, instructor.Id);   // now Approved, not Pending
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ops.DenyExtraHoursAsync(submitted.Id, instructor.Id));
    }

    [Fact]
    public async Task SubmitIncident_creates_record()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var result = await ops.SubmitIncidentReportAsync(shift.Id, instructor.Id, IncidentSeverity.High, "תיאור");
        Assert.NotNull(result);
        Assert.Equal(IncidentSeverity.High, result.Severity);
    }

    [Fact]
    public async Task SubmitIncident_High_writes_severe_outbox_event()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var report = await ops.SubmitIncidentReportAsync(shift.Id, instructor.Id, IncidentSeverity.High, "אירוע חמור");
        var evt = await db.OutboxEvents.SingleOrDefaultAsync(e =>
            e.EventType == "IncidentSevere" && e.Payload.Contains($"\"incidentReportId\":{report.Id}"));
        Assert.NotNull(evt);
        Assert.Null(evt!.ProcessedAt);
    }

    [Fact]
    public async Task SubmitIncident_Medium_writes_no_severe_event()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var report = await ops.SubmitIncidentReportAsync(shift.Id, instructor.Id, IncidentSeverity.Medium, "אירוע בינוני");
        var any = await db.OutboxEvents.AnyAsync(e =>
            e.EventType == "IncidentSevere" && e.Payload.Contains($"\"incidentReportId\":{report.Id}"));
        Assert.False(any);
    }

    // ── Incident lifecycle (F2 part B) ─────────────────────────────────────────

    [Fact]
    public async Task CloseIncident_sets_status_closed()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var report = await ops.SubmitIncidentReportAsync(shift.Id, instructor.Id, IncidentSeverity.Medium, "אירוע");
        await ops.CloseIncidentAsync(report.Id, adminUserId: instructor.Id);
        db.ChangeTracker.Clear();
        var loaded = await db.IncidentReports.FindAsync(report.Id);
        Assert.Equal(IncidentStatus.Closed, loaded!.Status);
    }

    [Fact]
    public async Task EscalateIncident_sets_status_escalated()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var report = await ops.SubmitIncidentReportAsync(shift.Id, instructor.Id, IncidentSeverity.Medium, "אירוע");
        await ops.EscalateIncidentAsync(report.Id, adminUserId: instructor.Id);
        db.ChangeTracker.Clear();
        var loaded = await db.IncidentReports.FindAsync(report.Id);
        Assert.Equal(IncidentStatus.Escalated, loaded!.Status);
    }

    [Fact]
    public async Task CloseIncident_resolves_open_severe_ticket()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var report = await ops.SubmitIncidentReportAsync(shift.Id, instructor.Id, IncidentSeverity.High, "חמור");
        // The drainer would create this severe ticket; insert it directly to test the close→resolve link.
        db.ActionItems.Add(new ActionItem
        {
            Type = ActionItemType.Task,
            Status = ActionItemStatus.Open,
            AssignedToRole = AppRoles.Admin,
            RelatedEntityId = report.Id,
            DeduplicationKey = $"severe_incident_{report.Id}",
            Description = "severe"
        });
        await db.SaveChangesAsync();

        await ops.CloseIncidentAsync(report.Id, adminUserId: instructor.Id);

        db.ChangeTracker.Clear();
        var ticket = await db.ActionItems.FirstOrDefaultAsync(
            a => a.RelatedEntityId == report.Id && a.Type == ActionItemType.Task);
        Assert.Equal(ActionItemStatus.Resolved, ticket!.Status);
        Assert.Null(ticket.DeduplicationKey);
    }

    [Fact]
    public async Task CloseIncident_already_closed_throws()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var report = await ops.SubmitIncidentReportAsync(shift.Id, instructor.Id, IncidentSeverity.Low, "אירוע");
        await ops.CloseIncidentAsync(report.Id, instructor.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ops.CloseIncidentAsync(report.Id, instructor.Id));
    }

    [Fact]
    public async Task RequestVacation_creates_pending_record()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Orkabi.Web.Shared.IsraelClock.IsraelTz));
        var start = today.AddDays(1);
        var end = today.AddDays(5);
        var result = await ops.RequestVacationAsync(instructor.Id, start, end, "חופשה");
        Assert.Equal(VacationStatus.Pending, result.Status);
        Assert.Equal(start, result.StartDate);
    }

    [Fact]
    public async Task RequestVacation_start_in_past_throws()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await Assert.ThrowsAsync<ArgumentException>(
            () => ops.RequestVacationAsync(instructor.Id, yesterday, yesterday.AddDays(1), null));
    }

    [Fact]
    public async Task RequestVacation_end_before_start_throws()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Orkabi.Web.Shared.IsraelClock.IsraelTz));
        var start = today.AddDays(3);
        var end = today.AddDays(1);
        await Assert.ThrowsAsync<ArgumentException>(
            () => ops.RequestVacationAsync(instructor.Id, start, end, null));
    }

    [Fact]
    public async Task ApproveVacation_transitions_to_approved()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Orkabi.Web.Shared.IsraelClock.IsraelTz));
        var vac = await ops.RequestVacationAsync(instructor.Id, today.AddDays(1), today.AddDays(3), null);
        await ops.ApproveVacationAsync(vac.Id, instructor.Id);
        var approved = await db.VacationRequests.FindAsync(vac.Id);
        Assert.Equal(VacationStatus.Approved, approved!.Status);
    }

    [Fact]
    public async Task DenyVacation_transitions_to_denied()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Orkabi.Web.Shared.IsraelClock.IsraelTz));
        var vac = await ops.RequestVacationAsync(instructor.Id, today.AddDays(1), today.AddDays(3), null);
        await ops.DenyVacationAsync(vac.Id, instructor.Id, "אין כיסוי");
        var denied = await db.VacationRequests.FindAsync(vac.Id);
        Assert.Equal(VacationStatus.Denied, denied!.Status);
        Assert.Equal("אין כיסוי", denied.AdminNote);
    }

    [Fact]
    public async Task ApproveVacation_double_approve_throws()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Orkabi.Web.Shared.IsraelClock.IsraelTz));
        var vac = await ops.RequestVacationAsync(instructor.Id, today.AddDays(1), today.AddDays(3), null);
        await ops.ApproveVacationAsync(vac.Id, instructor.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ops.ApproveVacationAsync(vac.Id, instructor.Id));
    }

    // ── CancelVacationAsync (F11 — instructor withdraws their own pending request) ──

    [Fact]
    public async Task CancelVacation_sets_cancelled()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Orkabi.Web.Shared.IsraelClock.IsraelTz));
        var vac = await ops.RequestVacationAsync(instructor.Id, today.AddDays(1), today.AddDays(3), null);
        await ops.CancelVacationAsync(vac.Id, instructor.Id);
        db.ChangeTracker.Clear();
        Assert.Equal(VacationStatus.Cancelled, (await db.VacationRequests.FindAsync(vac.Id))!.Status);
    }

    [Fact]
    public async Task CancelVacation_by_non_owner_throws_and_leaves_pending()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Orkabi.Web.Shared.IsraelClock.IsraelTz));
        var vac = await ops.RequestVacationAsync(instructor.Id, today.AddDays(1), today.AddDays(3), null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ops.CancelVacationAsync(vac.Id, instructor.Id + 99999));
        db.ChangeTracker.Clear();
        Assert.Equal(VacationStatus.Pending, (await db.VacationRequests.FindAsync(vac.Id))!.Status);
    }

    [Fact]
    public async Task CancelVacation_non_pending_throws()
    {
        var (db, ops, instructor, shift) = await SetupAsync(_sqlite);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Orkabi.Web.Shared.IsraelClock.IsraelTz));
        var vac = await ops.RequestVacationAsync(instructor.Id, today.AddDays(1), today.AddDays(3), null);
        await ops.ApproveVacationAsync(vac.Id, instructor.Id);   // no longer Pending
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ops.CancelVacationAsync(vac.Id, instructor.Id));
    }

    // ── IDOR-lite ownership guards ────────────────────────────────────────────

    /// <summary>
    /// Seeds instructorA's shift and returns instructorB's Id alongside the
    /// shared db + ops service, so we can verify the guard rejects the cross-owner call.
    /// </summary>
    private static async Task<(AppDbContext db, OperationsService ops, int shiftId, int instructorBId)>
        SetupCrossOwnerAsync(SqliteFixture sqlite)
    {
        var factory = new OrkabiAppFactory { ConnectionString = sqlite.ConnectionString }.Prepared();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        // instructorA — owns the shift
        var emailA = $"owner-a-{Guid.NewGuid():N}@test.test";
        var instructorA = new AppUser { UserName = emailA, Email = emailA };
        await um.CreateAsync(instructorA, "Passw0rd!");
        await um.AddToRoleAsync(instructorA, AppRoles.Instructor);

        // instructorB — will attempt to submit against A's shift
        var emailB = $"intruder-b-{Guid.NewGuid():N}@test.test";
        var instructorB = new AppUser { UserName = emailB, Email = emailB };
        await um.CreateAsync(instructorB, "Passw0rd!");
        await um.AddToRoleAsync(instructorB, AppRoles.Instructor);

        var school = new Orkabi.Web.Modules.People.School { Name = "School-X", City = "Haifa" };
        var year = new Orkabi.Web.Modules.People.AcademicYear
        {
            Label = $"Y-{Guid.NewGuid():N}"[..10],
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 6, 30)
        };
        db.Schools.Add(school);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();

        var cls = new Orkabi.Web.Modules.People.Class
        {
            Name = $"Cls-{Guid.NewGuid():N}"[..15],
            SchoolId = school.Id,
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var template = new ShiftTemplate
        {
            ClassId = cls.Id,
            DefaultInstructorId = instructorA.Id,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 0),
            AcademicYearId = year.Id,
            Status = EntityStatus.Active
        };
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync();

        // shift belongs to instructorA
        var shift = new ShiftInstance
        {
            TemplateId = template.Id,
            ActualInstructorId = instructorA.Id,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
            Status = ShiftInstanceStatus.Scheduled
        };
        db.ShiftInstances.Add(shift);
        await db.SaveChangesAsync();

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(sqlite.ConnectionString)
            .Options;
        var freshDb = new AppDbContext(opts);
        var ops = new OperationsService(freshDb, new ActionItemService(freshDb));

        return (freshDb, ops, shift.Id, instructorB.Id);
    }

    [Fact]
    public async Task SubmitExtraHours_for_other_instructors_shift_throws()
    {
        var (db, ops, shiftId, instructorBId) = await SetupCrossOwnerAsync(_sqlite);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ops.SubmitExtraHoursAsync(shiftId, instructorBId, 1m, "ניסיון IDOR"));

        var rowCount = await db.ExtraHours.CountAsync(e => e.ShiftInstanceId == shiftId);
        Assert.Equal(0, rowCount);
    }

    [Fact]
    public async Task SubmitIncident_for_other_instructors_shift_throws()
    {
        var (db, ops, shiftId, instructorBId) = await SetupCrossOwnerAsync(_sqlite);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ops.SubmitIncidentReportAsync(shiftId, instructorBId, IncidentSeverity.Low, "ניסיון IDOR"));

        var rowCount = await db.IncidentReports.CountAsync(r => r.ShiftInstanceId == shiftId);
        Assert.Equal(0, rowCount);
    }
}
