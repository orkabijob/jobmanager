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
