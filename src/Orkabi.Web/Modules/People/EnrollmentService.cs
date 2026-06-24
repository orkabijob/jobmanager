using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;

namespace Orkabi.Web.Modules.People;

public class EnrollmentService
{
    private readonly AppDbContext _db;
    public EnrollmentService(AppDbContext db) => _db = db;

    public Task<List<Enrollment>> ListByClassAsync(int classId) =>
        _db.Enrollments
            .Where(e => e.ClassId == classId && e.Status != EnrollmentStatus.Dropped)
            .Include(e => e.Client)
            .OrderBy(e => e.Client.Name)
            .ToListAsync();

    public async Task<List<Client>> ListAvailableForClassAsync(int classId, string? q)
    {
        // Active clients who are NOT currently enrolled (non-Dropped) in this class.
        var enrolledClientIds = await _db.Enrollments
            .Where(e => e.ClassId == classId && e.Status != EnrollmentStatus.Dropped)
            .Select(e => e.ClientId)
            .ToListAsync();

        var query = _db.Clients
            .Where(c => c.IsActive && !enrolledClientIds.Contains(c.Id));

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(c => c.Name.Contains(q));

        return await query.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<Enrollment> EnrollAsync(int classId, int clientId)
    {
        // App-level duplicate guard — gives the friendly Hebrew message before the DB index fires.
        if (await _db.Enrollments.AnyAsync(e => e.ClassId == classId && e.ClientId == clientId && e.Status != EnrollmentStatus.Dropped))
            throw new InvalidOperationException("התלמיד כבר רשום לכיתה זו");

        var enrollment = new Enrollment
        {
            ClassId = classId,
            ClientId = clientId,
            Status = EnrollmentStatus.Active,
            EnrolledAt = DateTime.UtcNow
        };
        _db.Enrollments.Add(enrollment);
        await _db.SaveChangesAsync();
        return enrollment;
    }

    public async Task DropAsync(int enrollmentId)
    {
        var enrollment = await _db.Enrollments.FindAsync(enrollmentId)
            ?? throw new InvalidOperationException($"רישום {enrollmentId} לא נמצא");
        enrollment.Status = EnrollmentStatus.Dropped;
        await _db.SaveChangesAsync();
    }

    public async Task ToggleAsync(int enrollmentId, string field)
    {
        var enrollment = await _db.Enrollments.FindAsync(enrollmentId)
            ?? throw new InvalidOperationException($"רישום {enrollmentId} לא נמצא");

        switch (field)
        {
            case "tryout":
                enrollment.IsTryout = !enrollment.IsTryout;
                enrollment.Status = enrollment.IsTryout ? EnrollmentStatus.Tryout : EnrollmentStatus.Active;
                break;
            case "materials":
                enrollment.PaidMaterials = !enrollment.PaidMaterials;
                break;
            case "monthly":
                enrollment.PaidMonthly = !enrollment.PaidMonthly;
                break;
            default:
                throw new ArgumentException($"שדה לא מוכר: {field}", nameof(field));
        }

        await _db.SaveChangesAsync();
    }
}
