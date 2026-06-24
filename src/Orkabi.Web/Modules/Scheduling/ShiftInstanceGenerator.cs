using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Modules.Scheduling;

public class ShiftInstanceGenerator : IShiftInstanceGenerator
{
    private readonly AppDbContext _db;

    public ShiftInstanceGenerator(AppDbContext db) => _db = db;

    public async Task GenerateForTemplateAsync(int templateId, int horizonDays = 30, CancellationToken ct = default)
    {
        // Load template with AcademicYear; bypass the global query filter so Archived templates
        // are visible (we need to check Status explicitly to skip them).
        var template = await _db.ShiftTemplates
            .IgnoreQueryFilters()
            .Include(t => t.AcademicYear)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);

        if (template is null || template.Status == EntityStatus.Archived)
            return;

        var windowStart = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));

        if (windowStart > template.AcademicYear.EndDate)
            return;

        var windowEnd = windowStart.AddDays(horizonDays) < template.AcademicYear.EndDate
            ? windowStart.AddDays(horizonDays)
            : template.AcademicYear.EndDate;

        // Load already-existing instance dates for this template within the window
        // so we can skip them without an AnyAsync call per date.
        var existingDates = await _db.ShiftInstances
            .Where(i => i.TemplateId == templateId && i.Date >= windowStart && i.Date <= windowEnd)
            .Select(i => i.Date)
            .ToHashSetAsync(ct);

        var current = windowStart;
        while (current <= windowEnd)
        {
            if (current.DayOfWeek == template.DayOfWeek && !existingDates.Contains(current))
            {
                _db.ShiftInstances.Add(new ShiftInstance
                {
                    TemplateId = templateId,
                    Date = current,
                    ActualInstructorId = template.DefaultInstructorId,
                    Status = ShiftInstanceStatus.Scheduled
                });
            }
            current = current.AddDays(1);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task GenerateAllActiveAsync(int horizonDays = 30, CancellationToken ct = default)
    {
        // Active templates only (global query filter already excludes Archived).
        var templateIds = await _db.ShiftTemplates
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var id in templateIds)
            await GenerateForTemplateAsync(id, horizonDays, ct);
    }
}
