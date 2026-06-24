namespace Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Shared;

// NOT IArchivable — Model is a curriculum lookup; filtering it would cause silent null navigations.
public class Model : BaseEntity
{
    public string Name { get; set; } = "";
    public int ExpectedLessonsToComplete { get; set; }
    public string? MaterialLink { get; set; }
    public string? VideoLink { get; set; }
    public ICollection<SyllabusModel> SyllabusModels { get; set; } = new List<SyllabusModel>();
    // Model.LessonLogs deferred to Task 2 (Scheduling.LessonLog does not exist yet).
}
