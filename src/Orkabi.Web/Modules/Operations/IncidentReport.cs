namespace Orkabi.Web.Modules.Operations;

using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;

// NOT IArchivable — operational record; no archival filter.
// Submit-only in Slice 3 — no approval flow, no ActionItem.
public class IncidentReport : BaseEntity
{
    public int ShiftInstanceId { get; set; }
    public ShiftInstance ShiftInstance { get; set; } = null!;

    public int InstructorId { get; set; }
    public AppUser Instructor { get; set; } = null!;

    public IncidentSeverity Severity { get; set; }

    /// <summary>Lifecycle status (F2): Open → Closed / Escalated. Defaults to Open.</summary>
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    /// <summary>Description of the incident; max 2000 chars.</summary>
    public string Description { get; set; } = "";
}
