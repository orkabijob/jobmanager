namespace Orkabi.Web.Modules.People;

public class Client : Orkabi.Web.Shared.BaseEntity   // NOT IArchivable — uses IsActive (orthogonal to archival)
{
    public string Name { get; set; } = "";
    public string? ParentPhone { get; set; }
    public int? Age { get; set; }
    public string? Address { get; set; }
    public DateOnly? Birthday { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}
