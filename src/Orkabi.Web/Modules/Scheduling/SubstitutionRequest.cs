namespace Orkabi.Web.Modules.Scheduling;

using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;

// NOT IArchivable — audit record; no archival filter.
public class SubstitutionRequest : BaseEntity
{
    public int ShiftInstanceId { get; set; }
    public ShiftInstance ShiftInstance { get; set; } = null!;

    public int RequestingInstructorId { get; set; }
    public AppUser RequestingInstructor { get; set; } = null!;

    public int SubstituteInstructorId { get; set; }
    public AppUser SubstituteInstructor { get; set; } = null!;

    public SubstitutionStatus Status { get; set; } = SubstitutionStatus.Pending;

    public int? ApprovedByUserId { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public DateTime? ApprovedAt { get; set; }   // UTC
}
