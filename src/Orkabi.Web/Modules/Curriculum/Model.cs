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
    public ICollection<Scheduling.LessonLog> LessonLogs { get; set; } = new List<Scheduling.LessonLog>();
}
