namespace Orkabi.Web.Modules.Scheduling;

using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;

// NOT IArchivable — operational record; no archival filter.
public class ShiftInstance : BaseEntity
{
    public int TemplateId { get; set; }
    public ShiftTemplate Template { get; set; } = null!;

    public int? ActualInstructorId { get; set; }
    public AppUser? ActualInstructor { get; set; }

    public DateOnly Date { get; set; }
    public ShiftInstanceStatus Status { get; set; } = ShiftInstanceStatus.Scheduled;

    public ICollection<SubstitutionRequest> SubstitutionRequests { get; set; } = new List<SubstitutionRequest>();
    public LessonLog? LessonLog { get; set; }
}
