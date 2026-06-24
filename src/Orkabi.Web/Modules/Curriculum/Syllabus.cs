namespace Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Shared;

// IArchivable — Syllabus is the Curriculum aggregate root; gets the global query filter.
public class Syllabus : BaseEntity, IArchivable
{
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public ICollection<SyllabusModel> SyllabusModels { get; set; } = new List<SyllabusModel>();
    public ICollection<Orkabi.Web.Modules.People.Class> Classes { get; set; } = new List<Orkabi.Web.Modules.People.Class>();
}
