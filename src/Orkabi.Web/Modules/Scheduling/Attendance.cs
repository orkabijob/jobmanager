namespace Orkabi.Web.Modules.Scheduling;

using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;

// NOT IArchivable — operational record; no archival filter.
public class Attendance : BaseEntity
{
    public int LessonLogId { get; set; }
    public LessonLog LessonLog { get; set; } = null!;

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;

    /// <summary>
    /// Client-supplied idempotency key; globally unique across all Attendance rows.
    /// </summary>
    public string IdempotencyKey { get; set; } = "";
}
