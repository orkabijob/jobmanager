using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Modules.People;

/// <summary>R9 — how many classes + shift-templates a rollover copied into the target year.</summary>
public record RolloverResult(int ClassesCopied, int TemplatesCopied);

public class AcademicYearService
{
    private readonly AppDbContext _db;
    public AcademicYearService(AppDbContext db) => _db = db;

    public Task<List<AcademicYear>> ListAsync() =>
        _db.AcademicYears.OrderByDescending(y => y.StartDate).ToListAsync();

    public Task<AcademicYear?> GetCurrentAsync() =>
        _db.AcademicYears.FirstOrDefaultAsync(y => y.IsCurrent);

    // A newly created year is never current — promotion is an explicit SetCurrentAsync, so this
    // never touches the single-current partial index.
    public async Task<AcademicYear> CreateAsync(string label, DateOnly startDate, DateOnly endDate)
    {
        var year = new AcademicYear
        {
            Label = label,
            StartDate = startDate,
            EndDate = endDate,
            IsCurrent = false
        };
        _db.AcademicYears.Add(year);
        await _db.SaveChangesAsync();
        return year;
    }

    public async Task SetCurrentAsync(int academicYearId)
    {
        // Transactional: clear-before-set avoids partial-index violation (WHERE IsCurrent=true unique).
        using var tx = await _db.Database.BeginTransactionAsync();

        await _db.AcademicYears
            .Where(y => y.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(y => y.IsCurrent, false));

        var target = await _db.AcademicYears.FindAsync(academicYearId)
            ?? throw new InvalidOperationException($"שנת לימודים {academicYearId} לא נמצאה");
        target.IsCurrent = true;
        await _db.SaveChangesAsync();

        await tx.CommitAsync();
    }

    /// <summary>
    /// R9 — academic-year rollover: clones every Active class (name, school, syllabus) in the source
    /// year into the target year, and each of those classes' Active shift-templates (day/time/
    /// instructor). Does NOT copy enrollments (students re-enroll), shift instances (generated from
    /// templates), lesson logs, or orders. Idempotent: a class whose (school, name) already exists
    /// Active in the target year is skipped, so re-running is safe and won't hit the class-name
    /// partial-unique index. Transactional (all-or-nothing).
    /// </summary>
    public async Task<RolloverResult> RollOverAsync(int fromYearId, int toYearId)
    {
        if (fromYearId == toYearId)
            throw new InvalidOperationException("לא ניתן להעתיק שנת לימודים לעצמה");

        _ = await _db.AcademicYears.FindAsync(fromYearId)
            ?? throw new InvalidOperationException("שנת המקור לא נמצאה");
        _ = await _db.AcademicYears.FindAsync(toYearId)
            ?? throw new InvalidOperationException("שנת היעד לא נמצאה");

        var sourceClasses = await _db.Classes
            .Where(c => c.AcademicYearId == fromYearId && c.Status == EntityStatus.Active)
            .ToListAsync();

        // Idempotency: skip source classes already present (Active) in the target year.
        var existing = (await _db.Classes
                .Where(c => c.AcademicYearId == toYearId && c.Status == EntityStatus.Active)
                .Select(c => new { c.SchoolId, c.Name })
                .ToListAsync())
            .Select(x => (x.SchoolId, x.Name))
            .ToHashSet();

        int classesCopied = 0, templatesCopied = 0;
        using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var src in sourceClasses)
        {
            if (existing.Contains((src.SchoolId, src.Name)))
                continue;

            var newClass = new Class
            {
                Name = src.Name,
                SchoolId = src.SchoolId,
                AcademicYearId = toYearId,
                SyllabusId = src.SyllabusId,   // curriculum is reusable across years
                Status = EntityStatus.Active
            };
            _db.Classes.Add(newClass);
            await _db.SaveChangesAsync();   // materialize newClass.Id for the templates
            classesCopied++;

            var srcTemplates = await _db.ShiftTemplates
                .Where(t => t.ClassId == src.Id && t.Status == EntityStatus.Active)
                .ToListAsync();

            foreach (var t in srcTemplates)
            {
                _db.ShiftTemplates.Add(new ShiftTemplate
                {
                    ClassId = newClass.Id,
                    DefaultInstructorId = t.DefaultInstructorId,
                    DayOfWeek = t.DayOfWeek,
                    StartTime = t.StartTime,
                    EndTime = t.EndTime,
                    AcademicYearId = toYearId,
                    Status = EntityStatus.Active
                });
                templatesCopied++;
            }
            if (srcTemplates.Count > 0)
                await _db.SaveChangesAsync();
        }

        await tx.CommitAsync();
        return new RolloverResult(classesCopied, templatesCopied);
    }
}
