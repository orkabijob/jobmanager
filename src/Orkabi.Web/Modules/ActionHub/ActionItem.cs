using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Modules.ActionHub;

/// <summary>
/// Kernel action item — BaseEntity (audit-stamped), NOT IArchivable (no query filter).
/// AssignedToRole and AssignedToUserId are service-enforced exactly-one-of (not DB constraint).
/// </summary>
public class ActionItem : BaseEntity
{
    public ActionItemType Type { get; set; }

    public ActionItemStatus Status { get; set; } = ActionItemStatus.Open;

    /// <summary>Role name the item is assigned to; max 50 chars. Mutually exclusive with AssignedToUserId (service-enforced).</summary>
    public string? AssignedToRole { get; set; }

    /// <summary>FK → AspNetUsers; Restrict delete. Mutually exclusive with AssignedToRole (service-enforced).</summary>
    public int? AssignedToUserId { get; set; }
    public AppUser? AssignedToUser { get; set; }

    /// <summary>Free integer reference to a related entity in another module; NOT a navigation property.</summary>
    public int? RelatedEntityId { get; set; }

    /// <summary>Human-readable description (Hebrew), max 1000 chars.</summary>
    public string Description { get; set; } = "";

    public DateOnly? DueDate { get; set; }

    /// <summary>
    /// Idempotency / dedup key; max 200 chars.
    /// A partial unique index enforces uniqueness only among non-null values
    /// so that multiple items without a key can coexist.
    /// When an item is Resolved, this key is NULLED so that the unique-index slot is freed
    /// and automation can create a fresh recurrence for the same entity.
    /// </summary>
    public string? DeduplicationKey { get; set; }

    /// <summary>FK → AspNetUsers; SetNull on user delete. Set when Status=Resolved.</summary>
    public int? ResolvedByUserId { get; set; }

    /// <summary>UTC timestamp when the item was resolved. Set when Status=Resolved.</summary>
    public DateTime? ResolvedAt { get; set; }
}
