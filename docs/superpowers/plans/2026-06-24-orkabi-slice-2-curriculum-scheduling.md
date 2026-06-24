# Slice 2 — Curriculum + Scheduling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development — one fresh implementer per task, a task-review gate after each, whole-branch review before merge. Steps use `- [ ]`.
> **Design reference (binding for all UI tasks 7–9):** `docs/design/slice-2-curriculum-scheduling-design.md` (exact Hebrew copy, markup classes, paste-ready CSS). **Spec:** `docs/superpowers/specs/2026-06-23-orkabi-design.md` §4/§5/§7/§8.

**Goal:** Curriculum (Models, Syllabus, ordered Syllabus_Models) + Scheduling (Shift_Template→Instance with `ShiftInstanceGenerator`, Substitution_Request, Lesson_Log with pacing snapshot, Attendance) + the signature instructor **attendance** screen and lesson-log; HTMX lands this slice; `Class.SyllabusId` FK added.

**Architecture:** New `Modules/Curriculum/` + `Modules/Scheduling/` bounded contexts on the Slice-0/1 patterns (BaseEntity + audit interceptor; `IArchivable` global filter on aggregate roots **only**; thin services; additive Npgsql migrations applied at boot; SQLite `EnsureCreated` for tests). Date-scoped instructor authorization is a DB-checked service guard (not a full `IAuthorizationHandler`).

**Tech stack:** ASP.NET Core 8 Razor Pages + HTMX (self-hosted) + a thin REST API for attendance, EF Core 9 (+ Npgsql/Sqlite), ASP.NET Identity (int keys), xUnit + WebApplicationFactory.

## Global Constraints

- **Branch:** all work on `slice-2-curriculum-scheduling`; merge to `master` only at the deploy task (Render auto-deploys `master` — requires explicit user sign-off).
- **Build/test gate:** `dotnet build` clean; `dotnet test` green at every task boundary.
- **Hebrew-only, RTL:** every user-facing string Hebrew (design-doc copy tables). Logical CSS only; numerals/times/dates/counts in `<bdi class="num">`; Hebrew weekday names.
- **Dates/times:** `DateOnly` for calendar dates, `TimeOnly` for shift times, `DateTime` UTC for instants. Int-backed enums via `.HasConversion<int>()`.
- **Archival filter ONLY on `Syllabus` + `ShiftTemplate`.** NEVER on Model/SyllabusModel/ShiftInstance/LessonLog/Attendance/SubstitutionRequest (operational status ≠ archival; and filtering a referenced lookup like Model causes silent null-navigation crashes).
- **FKs `Restrict`** for all domain relationships, EXCEPT `Class.SyllabusId` → `SetNull`.
- **Migrations additive only**; generate with `dotnet ef migrations add <Name> --project src/Orkabi.Web`; keep history.
- **Services fixed contracts:** implementers must match the service method signatures here (pages depend on them); do not invent new public service methods beyond these without flagging.
- **No placeholder data;** real data or proper empty states.
- **Cross-module note:** entities may reference other modules' types (e.g. `LessonLog.Model`); the "no cross-module DbSet reaching" rule binds SERVICES, not entities. Services access `_db.*` DbSets freely (the seam is the service layer).

---

## Task 1: Curriculum entities + AddCurriculum migration

**Files:**
- Create: `src/Orkabi.Web/Modules/Curriculum/Model.cs`, `Syllabus.cs`, `SyllabusModel.cs`
- Modify: `src/Orkabi.Web/Data/AppDbContext.cs` (3 DbSets + config)
- Create (EF): `Migrations/<ts>_AddCurriculum.cs`
- Test: `tests/Orkabi.Web.Tests/CurriculumEntityTests.cs`

**Entities:**
```csharp
namespace Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Shared;

public class Model : BaseEntity   // NOT IArchivable (lookup)
{
    public string Name { get; set; } = "";
    public int ExpectedLessonsToComplete { get; set; }
    public string? MaterialLink { get; set; }
    public string? VideoLink { get; set; }
    public ICollection<SyllabusModel> SyllabusModels { get; set; } = new List<SyllabusModel>();
    public ICollection<Scheduling.LessonLog> LessonLogs { get; set; } = new List<Scheduling.LessonLog>();  // add when LessonLog exists (Task 2)
}

public class Syllabus : BaseEntity, IArchivable
{
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public ICollection<SyllabusModel> SyllabusModels { get; set; } = new List<SyllabusModel>();
    public ICollection<People.Class> Classes { get; set; } = new List<People.Class>();  // inverse of Class.SyllabusId (Task 3)
}

public class SyllabusModel   // junction — NOT BaseEntity, NOT IArchivable; identity is (SyllabusId, ModelId)
{
    public int SyllabusId { get; set; }
    public Syllabus Syllabus { get; set; } = null!;
    public int ModelId { get; set; }
    public Model Model { get; set; } = null!;
    public int OrderIndex { get; set; }   // 1-based, unique per syllabus
}
```
> To keep Task 1 self-contained: OMIT `Model.LessonLogs` and `Syllabus.Classes` here (they reference types from Tasks 2/3). Add `Model.LessonLogs` in Task 2 and `Syllabus.Classes` in Task 3. Documented so it's intentional.

**Config (`OnModelCreating`):**
```csharp
public DbSet<Curriculum.Model> Models => Set<Curriculum.Model>();
public DbSet<Curriculum.Syllabus> Syllabi => Set<Curriculum.Syllabus>();
public DbSet<Curriculum.SyllabusModel> SyllabusModels => Set<Curriculum.SyllabusModel>();
// ...
// ARCHIVAL — Syllabus is the Curriculum aggregate root. Model/SyllabusModel get NO filter.
b.Entity<Curriculum.Syllabus>().HasQueryFilter(s => s.Status == Shared.EntityStatus.Active);
b.Entity<Curriculum.Syllabus>().Property(s => s.Status).HasConversion<int>();
b.Entity<Curriculum.Syllabus>().Property(s => s.Name).HasMaxLength(200).IsRequired();
b.Entity<Curriculum.Model>().Property(m => m.Name).HasMaxLength(200).IsRequired();
b.Entity<Curriculum.SyllabusModel>().HasKey(sm => new { sm.SyllabusId, sm.ModelId });
b.Entity<Curriculum.SyllabusModel>().HasOne(sm => sm.Syllabus).WithMany(s => s.SyllabusModels)
    .HasForeignKey(sm => sm.SyllabusId).OnDelete(DeleteBehavior.Restrict);
b.Entity<Curriculum.SyllabusModel>().HasOne(sm => sm.Model).WithMany(m => m.SyllabusModels)
    .HasForeignKey(sm => sm.ModelId).OnDelete(DeleteBehavior.Restrict);
b.Entity<Curriculum.SyllabusModel>().HasIndex(sm => new { sm.SyllabusId, sm.OrderIndex }).IsUnique();
```

- [ ] **Step 1: Write failing tests** `CurriculumEntityTests.cs`:
  - `Archived_syllabi_hidden_by_filter_and_visible_with_IgnoreQueryFilters` (add Active + Archived syllabus; filtered count 1, IgnoreQueryFilters 2)
  - `SyllabusModel_duplicate_model_in_same_syllabus_is_rejected` (composite PK → DbUpdateException)
  - `SyllabusModel_duplicate_order_index_in_same_syllabus_is_rejected` (unique index → DbUpdateException)
- [ ] **Step 2: Run → fail** (`dotnet test --filter CurriculumEntityTests`).
- [ ] **Step 3: Create entities + config** as above.
- [ ] **Step 4: Run → pass.**
- [ ] **Step 5: Migration** `dotnet ef migrations add AddCurriculum --project src/Orkabi.Web`. Confirm `Up()` creates `Models`, `Syllabi`, `SyllabusModels` (composite PK, Restrict FKs, the unique `(SyllabusId,OrderIndex)` index), no unexpected drops. Quote in report.
- [ ] **Step 6: Full `dotnet build` + `dotnet test` green; commit** `feat(curriculum): Model + Syllabus + SyllabusModel entities + archival filter + migration`.

---

## Task 2: Scheduling entities + enums + AddScheduling migration

**Files:**
- Create: `src/Orkabi.Web/Modules/Scheduling/ShiftTemplateStatus.cs` (all 5 enums), `ShiftTemplate.cs`, `ShiftInstance.cs`, `SubstitutionRequest.cs`, `LessonLog.cs`, `Attendance.cs`
- Modify: `AppDbContext.cs` (5 DbSets + config), `Modules/Curriculum/Model.cs` (add `LessonLogs` collection now)
- Create (EF): `Migrations/<ts>_AddScheduling.cs`
- Test: `tests/Orkabi.Web.Tests/SchedulingEntityTests.cs`

**Enums (one file `ShiftTemplateStatus.cs`):**
```csharp
namespace Orkabi.Web.Modules.Scheduling;
public enum ShiftInstanceStatus { Scheduled = 0, Completed = 1, Cancelled = 2, Detached = 3 }
public enum SubstitutionStatus { Pending = 0, Approved = 1, Denied = 2, Cancelled = 3 }
public enum LessonLogStatus { InProgress = 0, Completed = 1 }
public enum AttendanceStatus { Present = 0, Absent = 1 }
// ShiftTemplate uses the shared EntityStatus (Active/Archived) since it IS archivable.
```

**Entities** (all `BaseEntity`; instructor FKs → `Identity.AppUser` int):
```csharp
public class ShiftTemplate : BaseEntity, IArchivable {
    public int ClassId; public People.Class Class = null!;
    public int DefaultInstructorId; public Identity.AppUser DefaultInstructor = null!;
    public DayOfWeek DayOfWeek; public TimeOnly StartTime; public TimeOnly EndTime;
    public int AcademicYearId; public People.AcademicYear AcademicYear = null!;
    public EntityStatus Status = EntityStatus.Active;
    public ICollection<ShiftInstance> ShiftInstances = new List<ShiftInstance>();
}
public class ShiftInstance : BaseEntity {   // NOT IArchivable
    public int TemplateId; public ShiftTemplate Template = null!;
    public int? ActualInstructorId; public Identity.AppUser? ActualInstructor;
    public DateOnly Date; public ShiftInstanceStatus Status = ShiftInstanceStatus.Scheduled;
    public ICollection<SubstitutionRequest> SubstitutionRequests = new List<SubstitutionRequest>();
    public LessonLog? LessonLog;
}
public class SubstitutionRequest : BaseEntity {   // NOT IArchivable (audit)
    public int ShiftInstanceId; public ShiftInstance ShiftInstance = null!;
    public int RequestingInstructorId; public Identity.AppUser RequestingInstructor = null!;
    public int SubstituteInstructorId; public Identity.AppUser SubstituteInstructor = null!;
    public SubstitutionStatus Status = SubstitutionStatus.Pending;
    public int? ApprovedByUserId; public Identity.AppUser? ApprovedByUser; public DateTime? ApprovedAt;
}
public class LessonLog : BaseEntity {   // NOT IArchivable (operational)
    public int ShiftInstanceId; public ShiftInstance ShiftInstance = null!;
    public int ModelId; public Curriculum.Model Model = null!;   // cross-module nav (allowed)
    public LessonLogStatus Status = LessonLogStatus.InProgress;
    public string? InstructorNotes;
    public int ExpectedLessonsSnapshot;   // captured from Model.ExpectedLessonsToComplete at save
    public ICollection<Attendance> Attendances = new List<Attendance>();
}
public class Attendance : BaseEntity {   // NOT IArchivable
    public int LessonLogId; public LessonLog LessonLog = null!;
    public int ClientId; public People.Client Client = null!;
    public AttendanceStatus Status = AttendanceStatus.Present;
    public string IdempotencyKey = "";   // client-supplied; globally unique
}
```
> Use proper `{ get; set; }` accessors and `= null!;`/defaults per the Slice-1 entity style (the shorthand above is for brevity). Add `Model.LessonLogs` (`ICollection<Scheduling.LessonLog>`) to `Model.cs` now.

**Config highlights** (full FK/enum/index config — all FKs `Restrict`):
```csharp
// ARCHIVAL — ShiftTemplate only.
b.Entity<Scheduling.ShiftTemplate>().HasQueryFilter(t => t.Status == Shared.EntityStatus.Active);
b.Entity<Scheduling.ShiftTemplate>().Property(t => t.Status).HasConversion<int>();
b.Entity<Scheduling.ShiftTemplate>().Property(t => t.DayOfWeek).HasConversion<int>();
b.Entity<Scheduling.ShiftInstance>().Property(i => i.Status).HasConversion<int>();
b.Entity<Scheduling.SubstitutionRequest>().Property(r => r.Status).HasConversion<int>();
b.Entity<Scheduling.LessonLog>().Property(l => l.Status).HasConversion<int>();
b.Entity<Scheduling.Attendance>().Property(a => a.Status).HasConversion<int>();
b.Entity<Scheduling.Attendance>().Property(a => a.IdempotencyKey).HasMaxLength(100).IsRequired();
// FKs (all Restrict): ShiftTemplate→Class/DefaultInstructor/AcademicYear (.WithMany() no inverse on User/AY);
//   ShiftInstance→Template (WithMany ShiftInstances) + ActualInstructor (nullable, WithMany());
//   SubstitutionRequest→ShiftInstance (WithMany SubstitutionRequests)+Requesting/Substitute/ApprovedBy (WithMany());
//   LessonLog→ShiftInstance (one-to-one: HasOne(l=>l.ShiftInstance).WithOne(i=>i.LessonLog).HasForeignKey<LessonLog>(l=>l.ShiftInstanceId)) + Model (WithMany LessonLogs);
//   Attendance→LessonLog (WithMany Attendances)+Client (WithMany()).
// Indexes:
b.Entity<Scheduling.ShiftInstance>().HasIndex(i => new { i.TemplateId, i.Date }).IsUnique();
b.Entity<Scheduling.Attendance>().HasIndex(a => new { a.LessonLogId, a.ClientId }).IsUnique();
b.Entity<Scheduling.Attendance>().HasIndex(a => a.IdempotencyKey).IsUnique();
b.Entity<Scheduling.ShiftTemplate>().HasIndex(t => new { t.ClassId, t.DayOfWeek, t.AcademicYearId });
```

- [ ] **Step 1: Write failing tests** `SchedulingEntityTests.cs`:
  - `ShiftTemplate_archived_hidden_by_filter`
  - `ShiftInstance_duplicate_template_date_is_rejected`
  - `Attendance_duplicate_lesson_log_client_is_rejected`
  - `Attendance_duplicate_idempotency_key_is_rejected`
  - **`TimeOnly_round_trips_on_sqlite`** — insert a ShiftTemplate with `StartTime = new TimeOnly(16,0)`, `EndTime = new TimeOnly(17,30)`, read back, assert equal. **This is the top risk** (architect): EF 9 SQLite should map `TimeOnly`→TEXT natively. If this test FAILS, add an explicit value converter `HasConversion(v => v.ToString("HH:mm:ss"), v => TimeOnly.Parse(v))` on both time properties (root fix, not a workaround) and re-run.
  - Arrange helper: create instructor `AppUser`s via `UserManager` (templates FK to AspNetUsers). Add a shared `SeedInstructorAsync` helper.
- [ ] **Step 2: Run → fail.**
- [ ] **Step 3: Create entities + enums + config.**
- [ ] **Step 4: Run → pass** (apply the TimeOnly converter only if the round-trip test failed).
- [ ] **Step 5: Migration** `dotnet ef migrations add AddScheduling --project src/Orkabi.Web`. Confirm `Up()` creates all 5 tables, Restrict FKs to AspNetUsers/Classes/AcademicYears/Models/Clients, the unique indexes, no unexpected drops.
- [ ] **Step 6: Full build + test green; commit** `feat(scheduling): ShiftTemplate/Instance, SubstitutionRequest, LessonLog, Attendance + migration`.

---

## Task 3: Class.SyllabusId FK + AddClassSyllabusId migration

**Files:**
- Modify: `src/Orkabi.Web/Modules/People/Class.cs` (add `int? SyllabusId` + `Curriculum.Syllabus? Syllabus`), `Modules/Curriculum/Syllabus.cs` (ensure `Classes` collection present), `AppDbContext.cs` (FK config)
- Create (EF): `Migrations/<ts>_AddClassSyllabusId.cs`
- Test: existing `ArchivalFilterTests` must stay green; add `Class_can_link_to_syllabus` test.

**Config:**
```csharp
b.Entity<People.Class>().HasOne(c => c.Syllabus).WithMany(s => s.Classes)
    .HasForeignKey(c => c.SyllabusId).OnDelete(DeleteBehavior.SetNull);  // the one SetNull (archiving a syllabus must not block classes)
```
- [ ] **Step 1:** Add the nullable `SyllabusId` + nav to Class; ensure `Syllabus.Classes`. Add config.
- [ ] **Step 2: Migration** `dotnet ef migrations add AddClassSyllabusId --project src/Orkabi.Web`. Confirm `Up()` is ONLY `AddColumn SyllabusId` (nullable) + `CreateIndex` + `AddForeignKey` (onDelete SetNull). NO table recreation/drop.
- [ ] **Step 3:** Add a test `Class_links_and_unlinks_syllabus` (set SyllabusId, save, read back; set null, save).
- [ ] **Step 4: Build + test green; commit** `feat(people): add Class.SyllabusId FK (nullable, SetNull) + migration`.

---

## Task 4: ShiftInstanceGenerator + SchedulingService

**Files:**
- Create: `Modules/Scheduling/IShiftInstanceGenerator.cs`, `ShiftInstanceGenerator.cs`, `SchedulingService.cs`
- Modify: `Program.cs` (register both `AddScoped`)
- Test: `tests/Orkabi.Web.Tests/ShiftInstanceGeneratorTests.cs`, `SchedulingServiceTests.cs`

**Generator interface + algorithm:**
```csharp
public interface IShiftInstanceGenerator {
    Task GenerateForTemplateAsync(int templateId, int horizonDays = 30, CancellationToken ct = default);
    Task GenerateAllActiveAsync(int horizonDays = 30, CancellationToken ct = default);  // used by Slice-4 scheduler; testable now
}
```
Algorithm (single template): load template (+AcademicYear); if missing/Archived → return. `windowStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz))`; `windowEnd = Min(windowStart.AddDays(horizonDays), AcademicYear.EndDate)`; if `windowStart > AcademicYear.EndDate` return. Walk each date in `[windowStart, windowEnd]` where `date.DayOfWeek == template.DayOfWeek`; for each, **skip if `await _db.ShiftInstances.AnyAsync(i => i.TemplateId==id && i.Date==date)`** (idempotency — preserves Detached/Cancelled/Completed). Insert new `ShiftInstance { TemplateId, Date, ActualInstructorId = template.DefaultInstructorId, Status = Scheduled }`. One `SaveChangesAsync` per template. **In Slice 2 the generator is invoked on-demand only** (from CreateTemplateAsync/UpdateTemplateAsync); the BackgroundService scheduler is Slice 4.

**SchedulingService signatures (pages depend on these):**
```csharp
Task<ShiftTemplate> CreateTemplateAsync(ShiftTemplate t);    // create + GenerateForTemplateAsync(30)
Task UpdateTemplateAsync(ShiftTemplate t);                    // update + re-generate (idempotent)
Task ArchiveTemplateAsync(int templateId);                   // Status=Archived; instances preserved
Task<List<ShiftTemplate>> ListTemplatesAsync(int? classId, int? academicYearId);
Task<List<ShiftInstance>> ListInstancesAsync(DateOnly from, DateOnly to);   // include Template.Class + ActualInstructor
Task<List<ShiftInstance>> ListTodayForInstructorAsync(int userId);          // date==today Israel && actual==user
Task<bool> CanAccessShiftAsync(int shiftInstanceId, int userId);            // date-scoped guard (below)
Task<LessonLog> SaveLessonLogAsync(int shiftInstanceId, int modelId, LessonLogStatus status, string? notes);  // sets snapshot on create
Task<(int spent,int expected,bool over)> ComputePacingAsync(int classId, int modelId);  // "X of N" for lesson-log
Task<SubstitutionRequest> RequestSubstitutionAsync(int shiftInstanceId, int requestingInstructorId, int substituteInstructorId);
Task ApproveSubstitutionAsync(int substitutionRequestId, int approverUserId);  // tx: status+approvedBy/At + set instance.ActualInstructorId
Task DenySubstitutionAsync(int substitutionRequestId, int approverUserId);
Task CancelSubstitutionAsync(int substitutionRequestId, int requestingInstructorId);
Task<List<SubstitutionRequest>> ListPendingSubstitutionsAsync();
Task<Attendance> RecordAttendanceAsync(int lessonLogId, int clientId, AttendanceStatus status, string idempotencyKey);
Task<List<Attendance>> SubmitAttendanceAsync(int lessonLogId, IEnumerable<(int clientId, AttendanceStatus status)> marks, string idempotencyKey);
```

**Date-scoped auth guard** (security-critical — DB query, IsraelClock today, no caching):
```csharp
public async Task<bool> CanAccessShiftAsync(int shiftInstanceId, int userId) {
    var todayIsrael = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz));
    return await _db.ShiftInstances.AnyAsync(i =>
        i.Id == shiftInstanceId && i.ActualInstructorId == userId && i.Date == todayIsrael);
}
```

**SaveLessonLogAsync snapshot capture:** load `Model` (FindAsync), set `ExpectedLessonsSnapshot = model.ExpectedLessonsToComplete` at CREATE only (never overwrite on later updates).

**ApproveSubstitutionAsync:** transaction — load request (Pending), set `Status=Approved, ApprovedByUserId, ApprovedAt=UtcNow`, set `request.ShiftInstance.ActualInstructorId = request.SubstituteInstructorId`, commit.

**SubmitAttendanceAsync / RecordAttendanceAsync idempotency:** attempt insert(s); catch `DbUpdateException` from the global `IdempotencyKey` unique index → treat as already-submitted, load + return existing (NOT a 500). A `(LessonLogId,ClientId)` violation with a different key throws `InvalidOperationException` (Hebrew msg).

- [ ] **Step 1: Write failing tests** (`ShiftInstanceGeneratorTests` + `SchedulingServiceTests`):
  - `Generator_creates_instances_for_matching_day_of_week_in_window`
  - `Generator_is_idempotent_on_second_call`
  - `Generator_skips_existing_detached_instances`
  - `Generator_clamps_to_academic_year_end`
  - `CanAccessShift_true_only_for_assigned_instructor_on_today`
  - `SaveLessonLog_captures_snapshot_and_later_model_change_does_not_alter_it`
  - `Approve_substitution_sets_actual_instructor_atomically`
  - `SubmitAttendance_idempotency_key_returns_existing_on_retry`
  - `Attendance_uses_class_enrollments` — verify the lesson-log's attendable clients == `EnrollmentService.ListByClassAsync(classId)` (cross-module integration with Slice 1).
- [ ] **Step 2: Run → fail.**
- [ ] **Step 3: Implement** generator + service; register in `Program.cs`.
- [ ] **Step 4: Run → pass; full test; commit** `feat(scheduling): ShiftInstanceGenerator + SchedulingService (date-scoped auth, snapshot, substitution, idempotent attendance)`.

---

## Task 5: CurriculumService

**Files:** Create `Modules/Curriculum/CurriculumService.cs`; modify `Program.cs` (register); test `CurriculumServiceTests.cs`.

**Signatures:**
```csharp
Task<List<Model>> ListModelsAsync();
Task<Model?> GetModelAsync(int id);
Task<Model> CreateModelAsync(Model m);
Task UpdateModelAsync(Model m);
Task<List<Syllabus>> ListSyllabiAsync(EntityStatus? status = null);   // null=Active (filter); Archived via IgnoreQueryFilters
Task<Syllabus?> GetSyllabusAsync(int id);                              // include ordered SyllabusModels + Models
Task<Syllabus> CreateSyllabusAsync(Syllabus s, IEnumerable<(int modelId,int orderIndex)> models);
Task UpdateSyllabusAsync(Syllabus s, IEnumerable<(int modelId,int orderIndex)> models);  // tx: replace junction rows
Task ArchiveSyllabusAsync(int id);
Task AddModelToSyllabusAsync(int syllabusId, int modelId);   // appends at next order_index
Task ReorderAsync(int syllabusId, int modelId, int direction); // direction -1 up / +1 down; swap order_index
Task RemoveModelFromSyllabusAsync(int syllabusId, int modelId); // remove + compact order_index
```
- [ ] **Step 1: Failing tests:** model CRUD; syllabus create-with-ordered-models; reorder swaps order_index; remove compacts; archived syllabus hidden by default / returned when asked.
- [ ] **Step 2: fail → Step 3 implement → Step 4 pass.**
- [ ] **Step 5: commit** `feat(curriculum): CurriculumService (model/syllabus CRUD + ordered models + reorder)`.

---

## Task 6: HTMX setup (_Layout + antiforgery)

**Files:**
- Create: `src/Orkabi.Web/wwwroot/js/htmx.min.js` (self-host htmx 2.x stable)
- Modify: `src/Orkabi.Web/Pages/Shared/_Layout.cshtml` (inject IAntiforgery, meta token, htmx script, configRequest handler)
- Test: extend `RtlLayoutTests` to assert the served layout includes the htmx script + the `htmx-csrf` meta tag.

**_Layout additions** — in `<head>`:
```html
@inject Microsoft.AspNetCore.Antiforgery.IAntiforgery Antiforgery
...
<meta name="htmx-csrf" content="@(Antiforgery.GetAndStoreTokens(HttpContext).RequestToken)" />
```
before `</body>` (after glass-glint.js):
```html
<script src="~/js/htmx.min.js" defer asp-append-version="true"></script>
<script>
  document.body.addEventListener('htmx:configRequest', function (evt) {
    evt.detail.headers['RequestVerificationToken'] =
      document.querySelector('meta[name="htmx-csrf"]')?.content ?? '';
  });
</script>
```
- [ ] **Step 1:** Download htmx.min.js to wwwroot/js (verify it's the real minified lib, not a placeholder). 
- [ ] **Step 2:** Add the _Layout injections.
- [ ] **Step 3:** Extend RtlLayoutTests; build + test green.
- [ ] **Step 4: commit** `feat(ui): self-hosted HTMX + antiforgery header wiring in _Layout`.

---

## Task 7: Curriculum management pages (Models + Syllabi, HTMX ordering)

**Files:** `Pages/Curriculum/Index.cshtml(.cs)` (overview/subnav), `Pages/Curriculum/Models/{Index,Create,Edit}`, `Pages/Curriculum/Syllabi/{Index,Create,Edit}`, `Pages/Curriculum/Syllabi/_SyllabusModelList.cshtml` (HTMX partial). Test: `CurriculumPagesTests.cs`.

**Build to design doc §4** (dense admin scale; subnav `סקירה/סילבוסים/דגמים`). All `[Authorize(Roles = AppRoles.CsOrAdmin)]`. Inject `CurriculumService`. The ordered-model list reorders via HTMX (`hx-post` handlers `MoveUp`/`MoveDown`/`Remove`/`AddModel` → swap `#syllabus-models` partial). Up/down is the shipped baseline (drag is out of scope — see design §8). Models catalog form: name, expected-lessons (number ≥1), material/video URLs (optional, LTR).

- [ ] **Step 1: Failing tests:** authz (CS 200 / Instructor 403 / anon 302); Models CRUD round-trip; Syllabus create-with-models then reorder via the handler changes order in the rendered list (HtmlDecode Hebrew). 
- [ ] **Step 2: fail → 3 implement (per design §4) → 4 pass.**
- [ ] **Step 5: commit** `feat(curriculum): Models + Syllabi management pages (HTMX ordered models)`.

---

## Task 8: Scheduling admin pages (Templates + Instances + Substitutions)

**Files:** `Pages/Scheduling/Index.cshtml(.cs)` (subnav), `Pages/Scheduling/Templates/{Index,Create,Edit}`, `Pages/Scheduling/Instances/Index` (+ `_InstanceList.cshtml` HTMX partial + generate handler), `Pages/Scheduling/Substitutions/Index` (+ `_SubRow.cshtml` HTMX partial + approve/deny handlers). Test: `SchedulingPagesTests.cs`.

**Build to design doc §5** (dense admin; subnav `סקירה/תבניות/מופעים/החלפות`). Templates `[Authorize(CsOrAdmin)]`; **Substitutions approval `[Authorize(Roles = AppRoles.Admin)]`**. Template form: class/year/default-instructor selects, day-of-week (Hebrew), start/end `type=time`, status segment; on save calls `SchedulingService.CreateTemplateAsync`/`UpdateTemplateAsync` (triggers generator). Instances: grouped-by-date list (`ListInstancesAsync` over a horizon), a `צור מופעים` button that `hx-post`s generate and swaps `#instances-panel`; manually-edited (Detached) instances show a `נערך ידנית` chip. Substitutions: pending table; approve/deny `hx-post` swaps `_SubRow`.

- [ ] **Step 1: Failing tests:** authz incl. Instructor forbidden from substitution-approval; generate handler creates instances (count increases); approve sets `ShiftInstance.ActualInstructorId` to the substitute and the row swaps to approved.
- [ ] **Step 2: fail → 3 implement (per design §5) → 4 pass.**
- [ ] **Step 5: commit** `feat(scheduling): template + instance (generate) + substitution-approval pages`.

---

## Task 9: Instructor home + Attendance screen + Lesson-log (the signature surfaces)

**Files:** `Pages/Dashboard/Instructor.cshtml(.cs)` (replace stub → today's shifts + monolith), `Pages/Attendance/Index.cshtml(.cs)` (`@page "{shiftInstanceId:int}"`), the attendance JS (`wwwroot/js/attendance.js` — optimistic tap/submit), the REST endpoint `POST /api/attendance` (minimal API in `Program.cs` or a controller), `Pages/Attendance/Log.cshtml(.cs)` (+ `_LessonPacing.cshtml`, `_LessonStatus.cshtml` HTMX partials). Test: `AttendanceTests.cs`, `InstructorHomeTests.cs`.

**Build to design doc §1 (instructor home + Blue-Jay monolith), §2 (attendance — the signature tap-to-mark, THE most detailed surface; full paste-ready CSS is in the design doc §6), §3 (lesson-log HTMX pacing).**

Key behaviors:
- Instructor home `[Authorize(Roles = AppRoles.Instructor)]` (Admin may also view): `ListTodayForInstructorAsync(userId)`; render a `shift-card` + `.hero-cta` monolith per openable shift; locked monolith for future shifts; empty-state if none. NO placeholder data.
- Attendance page: guard with `CanAccessShiftAsync(shiftInstanceId, userId)` → `Forbid()` if false (Admin bypasses date-scope). Roster = the class's enrollments (via `EnrollmentService.ListByClassAsync`), tryouts in the tray. Marks are client-side; submit POSTs to `/api/attendance` with `{ lessonLogId or shiftInstanceId, marks[], idempotencyKey }`. The endpoint requires auth + the same date-scope check + antiforgery; calls `SchedulingService.SubmitAttendanceAsync`; returns 200 (or 409 if the idempotency key already used → friendly "already saved").
- Lesson-log: model `<select>` change `hx-post`s → `_LessonPacing` ("X of N" via `ComputePacingAsync`, overrun → warn); status/notes save `hx-post`s → `_LessonStatus`; marking Completed captures the snapshot (server).
- Progressive enhancement: with JS off, attendance falls back to a plain radio-per-row form POST to a page handler. The optimistic path is the design target.

- [ ] **Step 1: Failing integration tests** (`AttendanceTests`): instructor can open own today's shift (200) but not another's / not a non-today shift (403 via the guard); submitting attendance persists marks; re-submitting with the same idempotency key does not duplicate (returns existing). `InstructorHomeTests`: instructor sees only their today shifts; empty-state when none. (Seed instructor users + a class with enrollments + a shift instance dated today via IsraelClock.)
- [ ] **Step 2: fail → 3 implement (per design §1/§2/§3) → 4 pass.**
- [ ] **Step 5: commit** `feat(scheduling): instructor home + attendance (optimistic API) + lesson-log pacing (signature surfaces)`.

---

## Task 10: Deploy + verify + handoff

- [ ] **Step 1:** Final `dotnet build -clp:ErrorsOnly && dotnet test` green on the branch.
- [ ] **Step 2:** Self-review vs spec §4/§5/§7; whole-branch reviewer gate (strongest model) — fix Critical/Important.
- [ ] **Step 3:** Merge to `master` (requires user sign-off) → push → Render auto-deploys → boot applies `AddCurriculum`, `AddScheduling`, `AddClassSyllabusId` + verify.
- [ ] **Step 4:** Verify live: `/health` ok; a Scheduling/Curriculum route responds (e.g. `/Scheduling/Templates` → 302 login for anon, or 200 for CS); confirm migrations applied in the deploy log.
- [ ] **Step 5:** Update `docs/HANDOFF.md` (Slice 2 complete & live) + memory; commit.

---

## Self-Review (writing-plans checklist)
- **Spec §4 coverage:** Model/Syllabus/SyllabusModel ✔(T1); ShiftTemplate/Instance/SubstitutionRequest/LessonLog(+snapshot)/Attendance(+idempotency) ✔(T2); Class.SyllabusId ✔(T3). §5A snapshot ✔(T2/T4); §5B substitution + date-scoped access ✔(T4); §5D tryout tray (attendance UI) ✔(T9). §7 resource-based authz ✔(T4 guard). §8 DateOnly/TimeOnly/int-enums/additive-migrations ✔. HTMX ✔(T6,T7,T8,T9). ShiftInstanceGenerator ✔(T4).
- **Archival filter only on Syllabus + ShiftTemplate** ✔; operational-status entities unfiltered ✔.
- **Type consistency:** enum values fixed; FK behaviors Restrict except Class.SyllabusId SetNull; service signatures stable across T4/T5 → T7/T8/T9.
- **Deferred (disclosed):** BackgroundService scheduler + "regenerate all" UI → Slice 4; tryout-followup Outbox + Real-Gap monitor → Slice 3; drag-reorder (up/down shipped) → later; Motion-One polish on attendance (CSS transitions ship) → later; `AppUser.IsActive` → Slice 4; instances calendar-grid (list ships) → later.
