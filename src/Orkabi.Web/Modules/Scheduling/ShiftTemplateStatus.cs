namespace Orkabi.Web.Modules.Scheduling;

// ShiftTemplate reuses the shared EntityStatus (Active/Archived) — see Shared/EntityStatus.cs.

public enum ShiftInstanceStatus { Scheduled = 0, Completed = 1, Cancelled = 2, Detached = 3 }
public enum SubstitutionStatus { Pending = 0, Approved = 1, Denied = 2, Cancelled = 3 }
public enum LessonLogStatus { InProgress = 0, Completed = 1 }
public enum AttendanceStatus { Present = 0, Absent = 1 }
