namespace Orkabi.Web.Modules.Logistics;

using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;

// NOT IArchivable — operational record; no archival filter.
public class LogisticsOrder : BaseEntity
{
    public int ClassId { get; set; }
    public People.Class Class { get; set; } = null!;

    public int ModelId { get; set; }
    public Curriculum.Model Model { get; set; } = null!;

    public int Quantity { get; set; } = 1;

    public LogisticsOrderStatus Status { get; set; } = LogisticsOrderStatus.Pending;

    /// <summary>Dispute notes; max 500 chars. Set when Status = Disputed.</summary>
    public string? DisputeNotes { get; set; }

    /// <summary>UTC timestamp set when Status transitions to Accepted (NOT on Packed).</summary>
    public DateTime? DeliveredAt { get; set; }
}
