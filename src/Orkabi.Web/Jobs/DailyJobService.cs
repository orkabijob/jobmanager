using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Jobs;

/// <summary>
/// Timer-free daily job logic. The date is injected by the caller (BackgroundService or test)
/// so the methods are fully deterministic and testable without a running clock.
/// </summary>
public class DailyJobService : IDailyJobRunner
{
    private readonly AppDbContext _db;
    private readonly IShiftInstanceGenerator _generator;
    private readonly ActionItemService _actionItems;

    public DailyJobService(
        AppDbContext db,
        IShiftInstanceGenerator generator,
        ActionItemService actionItems)
    {
        _db = db;
        _generator = generator;
        _actionItems = actionItems;
    }

    /// <inheritdoc/>
    public async Task RunBirthdayCheckAsync(DateOnly todayIsrael, CancellationToken ct = default)
    {
        var tomorrow = todayIsrael.AddDays(1);

        // Current academic year — used for the instructor-resolution query.
        var currentYear = await _db.AcademicYears
            .FirstOrDefaultAsync(y => y.IsCurrent, ct);

        // Load all active clients that have a birthday set.
        var clients = await _db.Clients
            .Where(c => c.IsActive && c.Birthday != null)
            .ToListAsync(ct);

        foreach (var client in clients)
        {
            var bday = client.Birthday!.Value;
            bool isDayOf = bday.Month == todayIsrael.Month && bday.Day == todayIsrael.Day;
            bool is24hBefore = bday.Month == tomorrow.Month && bday.Day == tomorrow.Day;

            if (!isDayOf && !is24hBefore)
                continue;

            // Resolve instructor(s) from non-Dropped / non-Completed enrollments.
            // Each active/tryout enrollment → the active ShiftTemplate for that class
            // in the current AcademicYear → DefaultInstructorId.
            // IgnoreQueryFilters() bypasses the archival filter on ShiftTemplate.
            var instructorIds = new HashSet<int>();

            if (currentYear is not null)
            {
                var activeEnrollments = await _db.Enrollments
                    .Where(e => e.ClientId == client.Id
                             && e.Status != EnrollmentStatus.Dropped
                             && e.Status != EnrollmentStatus.Completed)
                    .ToListAsync(ct);

                foreach (var enrollment in activeEnrollments)
                {
                    // There may be 0 or more active templates per class/year combo.
                    var templateInstructorIds = await _db.ShiftTemplates
                        .IgnoreQueryFilters()
                        .Where(t => t.ClassId == enrollment.ClassId
                                 && t.Status == EntityStatus.Active
                                 && t.AcademicYearId == currentYear.Id)
                        .Select(t => t.DefaultInstructorId)
                        .ToListAsync(ct);

                    foreach (var id in templateInstructorIds)
                        instructorIds.Add(id);
                }
            }

            // Determine the birthday occurrence date to use for the dedup key:
            //   day-of   → todayIsrael  (birthday is TODAY)
            //   24h-before → tomorrow   (birthday is TOMORROW)
            var birthdayOccurrence = isDayOf ? todayIsrael : tomorrow;

            if (instructorIds.Count == 0)
            {
                // No active enrollment/instructor → admin-only ticket.
                if (isDayOf)
                    await _actionItems.EnsureBirthdayDayOfActionItemAsync(client.Id, null, birthdayOccurrence);
                else
                    await _actionItems.EnsureBirthday24hActionItemAsync(client.Id, null, birthdayOccurrence);
            }
            else
            {
                // One ticket per instructor (+ the admin ticket created inside each call).
                foreach (var instructorId in instructorIds)
                {
                    if (isDayOf)
                        await _actionItems.EnsureBirthdayDayOfActionItemAsync(client.Id, instructorId, birthdayOccurrence);
                    else
                        await _actionItems.EnsureBirthday24hActionItemAsync(client.Id, instructorId, birthdayOccurrence);
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task RunShiftGenerationAsync(DateOnly todayIsrael, CancellationToken ct = default)
    {
        await _generator.GenerateAllActiveAsync(30, ct);
    }
}
