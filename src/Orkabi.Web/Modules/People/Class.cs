namespace Orkabi.Web.Modules.People;

public class Class : Orkabi.Web.Shared.BaseEntity, Orkabi.Web.Shared.IArchivable  // the ONLY archival aggregate
{
    public string Name { get; set; } = "";
    public int SchoolId { get; set; }
    public School School { get; set; } = null!;
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = null!;
    public Orkabi.Web.Shared.EntityStatus Status { get; set; } = Orkabi.Web.Shared.EntityStatus.Active;
    // SyllabusId DEFERRED to Slice 2 (Syllabus table does not exist yet).
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}
