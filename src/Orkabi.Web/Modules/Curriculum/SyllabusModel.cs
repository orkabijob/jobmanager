namespace Orkabi.Web.Modules.Curriculum;

// Junction — NOT BaseEntity, NOT IArchivable. Identity is composite (SyllabusId, ModelId).
// OrderIndex is 1-based and unique per Syllabus (enforced by a unique index on (SyllabusId, OrderIndex)).
public class SyllabusModel
{
    public int SyllabusId { get; set; }
    public Syllabus Syllabus { get; set; } = null!;
    public int ModelId { get; set; }
    public Model Model { get; set; } = null!;
    public int OrderIndex { get; set; }
}
