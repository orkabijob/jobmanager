namespace Orkabi.Web.Modules.Scheduling;

using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;

// IArchivable — ShiftTemplate is the Scheduling aggregate root; gets the global query filter.
public class ShiftTemplate : BaseEntity, IArchivable
{
    public int ClassId { get; set; }
    public Class Class { get; set; } = null!;

    public int DefaultInstructorId { get; set; }
    public AppUser DefaultInstructor { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = null!;

    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public ICollection<ShiftInstance> ShiftInstances { get; set; } = new List<ShiftInstance>();
}
