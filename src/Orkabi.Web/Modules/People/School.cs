namespace Orkabi.Web.Modules.People;

public class School : Orkabi.Web.Shared.BaseEntity        // NOT IArchivable (no status in spec §4)
{
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string? ExternalWebsiteUrl { get; set; }
    public ICollection<Class> Classes { get; set; } = new List<Class>();
}
