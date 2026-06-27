using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;

namespace Orkabi.Web.Modules.People;

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
}
