namespace Orkabi.Web.Modules.People;

public class AcademicYear : Orkabi.Web.Shared.BaseEntity   // lookup — NOT IArchivable
{
    public string Label { get; set; } = "";        // e.g. "תשפ\"ו"
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public ICollection<Class> Classes { get; set; } = new List<Class>();
}
