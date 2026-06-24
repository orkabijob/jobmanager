namespace Orkabi.Web.Modules.Operations;

using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;

// NOT IArchivable — operational record; no archival filter.
public class ExtraHours : BaseEntity
{
    public int ShiftInstanceId { get; set; }
    public ShiftInstance ShiftInstance { get; set; } = null!;

    public int InstructorId { get; set; }
    public AppUser Instructor { get; set; } = null!;

    /// <summary>
    /// Number of extra hours claimed.
    /// HasPrecision(5,2) → Npgsql stores exact numeric(5,2); SQLite stores as REAL
    /// (fine for the expected values: 0.5, 1, 1.5, 2).
    /// </summary>
    public decimal Hours { get; set; }

    /// <summary>Reason for the extra hours; max 500 chars.</summary>
    public string Reason { get; set; } = "";

    public ExtraHoursStatus Status { get; set; } = ExtraHoursStatus.Pending;

    public int? ApprovedByUserId { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public DateTime? ApprovedAt { get; set; }
}
