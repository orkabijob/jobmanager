namespace Orkabi.Web.Modules.Scheduling;

using Orkabi.Web.Shared;

// NOT IArchivable — operational record; no archival filter.
public class LessonLog : BaseEntity
{
    public int ShiftInstanceId { get; set; }
    public ShiftInstance ShiftInstance { get; set; } = null!;

    public int ModelId { get; set; }
    public Curriculum.Model Model { get; set; } = null!;   // cross-module nav (allowed per architecture)

    public LessonLogStatus Status { get; set; } = LessonLogStatus.InProgress;
    public string? InstructorNotes { get; set; }

    /// <summary>
    /// Snapshot of Model.ExpectedLessonsToComplete captured at save time.
    /// Preserves the contract even if the model is later updated.
    /// </summary>
    public int ExpectedLessonsSnapshot { get; set; }

    public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
}
