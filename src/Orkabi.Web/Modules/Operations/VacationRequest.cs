namespace Orkabi.Web.Modules.Operations;

using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;

// NOT IArchivable — operational record; no archival filter.
public class VacationRequest : BaseEntity
{
    public int InstructorId { get; set; }
    public AppUser Instructor { get; set; } = null!;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public VacationStatus Status { get; set; } = VacationStatus.Pending;

    public int? ApprovedByUserId { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>Optional admin note, e.g. denial reason; max 500 chars.</summary>
    public string? AdminNote { get; set; }
}
