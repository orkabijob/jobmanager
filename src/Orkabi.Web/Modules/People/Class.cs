namespace Orkabi.Web.Modules.People;

// Enrollments navigation is DEFERRED to Task 2 (Enrollment entity does not exist yet).
public class Class : Orkabi.Web.Shared.BaseEntity, Orkabi.Web.Shared.IArchivable  // the ONLY archival aggregate
{
    public string Name { get; set; } = "";
    public int SchoolId { get; set; }
    public School School { get; set; } = null!;
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = null!;
    public Orkabi.Web.Shared.EntityStatus Status { get; set; } = Orkabi.Web.Shared.EntityStatus.Active;
    // SyllabusId DEFERRED to Slice 2 (Syllabus table does not exist yet).
    // Enrollments navigation DEFERRED to Task 2 (Enrollment entity does not exist yet).
}
