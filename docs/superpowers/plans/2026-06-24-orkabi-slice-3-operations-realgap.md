# Slice 3 — Operations + Real-Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development — fresh implementer per task, task-review gate after each, whole-branch review before merge. Steps use `- [ ]`.
> **Design reference (binding for UI tasks 5–6):** `docs/design/slice-3-operations-realgap-design.md`. **Spec:** `docs/superpowers/specs/2026-06-23-orkabi-design.md` §4 (Operations, Action_Item, OutboxEvent), §5A (Real-Gap), §3 (ActionHub = shared kernel).

**Goal:** Operations (Extra-Hours, Incident-Report, Vacation-Request with single-approval) + the **Outbox + Action-Item kernel** + the **Real-Gap pacing monitor** (Lesson_Log save → OutboxEvent in the same transaction → opportunistic drain → Admin Action_Item with a dedup key). Proves the domain-event→ticket path. The full Action Hub UI and the BackgroundService scheduler are LATER slices (5 and 4).

**Architecture:** New `Modules/Operations/` + `Modules/ActionHub/` + an Outbox in `Shared/`. Reuses all Slice-0/1/2 patterns (BaseEntity + audit interceptor; archival filter on aggregate roots ONLY; thin services; additive Npgsql migrations at boot; SQLite EnsureCreated tests). The drain is an opportunistic fire-and-forget middleware in Slice 3 (the BackgroundService loop is Slice 4 and will call the same `IOutboxDrainer`).

**Tech stack:** ASP.NET Core 8 Razor Pages + HTMX, EF Core 9 (Npgsql/Sqlite), ASP.NET Identity (int keys), xUnit + WebApplicationFactory.

## Global Constraints
- **Branch:** `slice-3-operations-realgap`; merge to `master` only at the deploy task (deploys are pre-authorized — proceed after whole-branch review + green).
- **Build/test gate:** `dotnet build` clean; `dotnet test` green at every task boundary (suite is currently 90).
- **Hebrew-only RTL;** logical CSS; numerals/dates/hours in `<bdi class="num">`.
- **No archival filter** on ANY Slice 3 entity (all operational/infrastructure). Int-backed enums via `.HasConversion<int>()`. FKs `Restrict` (instructor + approver FKs → AppUser).
- **Migrations additive;** generate via `dotnet ef migrations add <Name> --project src/Orkabi.Web`; keep history. After any migration, ensure the main `AppDbContextModelSnapshot.cs` is committed (a prior slice forgot it — verify `git status` includes it).
- **Cross-module:** entities may reference other modules' types (ActionItem has no nav to domain aggregates — uses `RelatedEntityId` free int). Services access `_db.*` freely.
- **VERIFIED facts:** EF9-SQLite enforces unique + partial unique indexes (filter syntax `"Col" IS NOT NULL` / `"Col" = v` works on both providers, use `<>`); `BeginTransactionAsync` works on SQLite (busy-timeout already in SqliteFixture).

---

## Task 1: Operations entities + AddOperations migration

**Files:** Create `Modules/Operations/ExtraHoursStatus.cs` (enums), `ExtraHours.cs`, `IncidentReport.cs`, `VacationRequest.cs`; modify `AppDbContext.cs`; migration `<ts>_AddOperations`; test `OperationsEntityTests.cs`.

**Enums (`ExtraHoursStatus.cs`):**
```csharp
namespace Orkabi.Web.Modules.Operations;
public enum ExtraHoursStatus { Pending = 0, Approved = 1 }
public enum IncidentSeverity { Low = 0, Medium = 1, High = 2 }
public enum VacationStatus { Pending = 0, Approved = 1, Denied = 2 }
```
**Entities** (all `: BaseEntity`, NOT IArchivable; instructor/approver FKs → `Identity.AppUser`, `Restrict`; `ShiftInstance` nav → Scheduling, `Restrict`):
```csharp
public class ExtraHours : BaseEntity {
    public int ShiftInstanceId; public Scheduling.ShiftInstance ShiftInstance = null!;
    public int InstructorId; public Identity.AppUser Instructor = null!;
    public decimal Hours;                 // HasPrecision(5,2); SQLite stores REAL (fine for .5/1/1.5/2 values)
    public string Reason = "";            // max 500
    public ExtraHoursStatus Status = ExtraHoursStatus.Pending;
    public int? ApprovedByUserId; public Identity.AppUser? ApprovedByUser; public DateTime? ApprovedAt;
}
public class IncidentReport : BaseEntity {   // submit-only, no approval, NO ActionItem in Slice 3
    public int ShiftInstanceId; public Scheduling.ShiftInstance ShiftInstance = null!;
    public int InstructorId; public Identity.AppUser Instructor = null!;
    public IncidentSeverity Severity;
    public string Description = "";        // max 2000
}
public class VacationRequest : BaseEntity {   // single-approval
    public int InstructorId; public Identity.AppUser Instructor = null!;
    public DateOnly StartDate; public DateOnly EndDate;
    public VacationStatus Status = VacationStatus.Pending;
    public int? ApprovedByUserId; public Identity.AppUser? ApprovedByUser; public DateTime? ApprovedAt;
    public string? AdminNote;             // max 500; denial reason
}
```
(Use proper `{ get; set; }` + `= null!;`/defaults per the existing entity style. Add a comment on `Hours` re: SQLite REAL vs Npgsql numeric.)

**Config (`OnModelCreating`):** add `using Operations = ...;`; 3 DbSets; enum `.HasConversion<int>()`; `Hours` `.HasPrecision(5,2)`; string max-lengths; FKs (instructor/approver → AspNetUsers via `.WithMany()`, ShiftInstance → ShiftInstances, ALL `Restrict`). NO query filters.

- [ ] Step 1: Write failing `OperationsEntityTests.cs` — entity round-trips; ExtraHours status default Pending; FK enforcement (seed instructor via UserManager + a shift instance). 
- [ ] Step 2: fail → Step 3 create entities + config → Step 4 pass.
- [ ] Step 5: `dotnet ef migrations add AddOperations`. Confirm `Up()` creates ExtraHours/IncidentReports/VacationRequests with Restrict FKs, no unexpected drops; **confirm AppDbContextModelSnapshot.cs is staged**.
- [ ] Step 6: build + full test green; commit `feat(operations): ExtraHours/IncidentReport/VacationRequest entities + migration`.

---

## Task 2: OutboxEvent + ActionItem + AddActionHubAndOutbox migration

**Files:** Create `Shared/OutboxEvent.cs`; `Modules/ActionHub/ActionItemType.cs` (enums) + `ActionItem.cs`; modify `AppDbContext.cs`; migration `<ts>_AddActionHubAndOutbox`; test `ActionHubEntityTests.cs`.

**OutboxEvent** (NOT BaseEntity — infrastructure; the audit interceptor must NOT touch it):
```csharp
namespace Orkabi.Web.Shared;
public class OutboxEvent {
    public int Id { get; set; }
    public string EventType { get; set; } = "";   // max 100
    public string Payload { get; set; } = "";      // JSON text (text on both providers; NOT jsonb — extensible string)
    public DateTime? ScheduledFor { get; set; }    // UTC; null = drain now
    public DateTime CreatedAt { get; set; }         // UTC; set by service, not interceptor
    public DateTime? ProcessedAt { get; set; }      // UTC; null = unprocessed
}
```
**ActionItem** (`Modules/ActionHub/`, `: BaseEntity`, NOT IArchivable):
```csharp
public enum ActionItemType { Absence=0, Gap=1, Dispute=2, Task=3, Birthday=4, TryoutFollowup=5 }
public enum ActionItemStatus { Open=0, Resolved=1 }
public class ActionItem : BaseEntity {
    public ActionItemType Type; public ActionItemStatus Status = ActionItemStatus.Open;
    public string? AssignedToRole;            // max 50; exactly-one-of with AssignedToUserId (service-enforced)
    public int? AssignedToUserId; public Identity.AppUser? AssignedToUser;
    public int? RelatedEntityId;              // free int, NOT a nav
    public string Description = "";            // Hebrew, max 1000
    public DateOnly? DueDate;
    public string? DeduplicationKey;          // max 200; partial-unique WHERE NOT NULL
}
```
**Config:** `using ActionHub = ...;`; 2 DbSets (`OutboxEvents`, `ActionItems`); enum conversions; `ActionItem.AssignedToUserId` FK → AspNetUsers `Restrict` (`.WithMany()`); partial unique index:
```csharp
b.Entity<ActionHub.ActionItem>().HasIndex(a => a.DeduplicationKey).HasFilter("\"DeduplicationKey\" IS NOT NULL").IsUnique();
```
**Audit interceptor:** verify `AuditSaveChangesInterceptor` only stamps `BaseEntity` subclasses — `OutboxEvent` is NOT BaseEntity so it's already excluded. Confirm (no change needed, but check).

- [ ] Step 1: Write failing `ActionHubEntityTests.cs` — dedup-key unique partial index REJECTS a duplicate non-null key (DbUpdateException); ALLOWS multiple NULL-key rows; OutboxEvent round-trips (ScheduledFor null + set).
- [ ] Step 2: fail → Step 3 create + config → Step 4 pass.
- [ ] Step 5: `dotnet ef migrations add AddActionHubAndOutbox`. Confirm `Up()` creates OutboxEvents + ActionItems with the partial unique index (`filter: "\"DeduplicationKey\" IS NOT NULL"`), Restrict FK; snapshot staged.
- [ ] Step 6: build + test green; commit `feat(actionhub): ActionItem kernel + OutboxEvent infra + migration`.

---

## Task 3: ActionItemService (minimal Slice-3 surface)

**Files:** Create `Modules/ActionHub/ActionItemService.cs`; register AddScoped in Program.cs; test `ActionItemServiceTests.cs`.

**Methods (only these two in Slice 3):**
```csharp
Task EnsureGapActionItemAsync(int classId, int modelId, int expected, int spent);
Task<List<ActionItem>> ListOpenForRoleAsync(string role);   // Status==Open && AssignedToRole==role, ordered by CreatedAt
```
**EnsureGapActionItemAsync:** dedupKey = `$"gap_{classId}_{modelId}"`. If an OPEN ActionItem with that key exists → return (no dup). Else build `ActionItem { Type=Gap, Status=Open, AssignedToRole=AppRoles.Admin, RelatedEntityId=classId, DeduplicationKey=dedupKey, Description=<Hebrew gap msg incl. class+model names, spent, expected> }`; `_db.ActionItems.Add`; try `SaveChangesAsync`; **catch `DbUpdateException`** (concurrent insert hit the unique index) → `_db.ChangeTracker.Clear()` and return (invariant holds). Fetch class name via `_db.Classes.IgnoreQueryFilters().FindAsync(classId)` + model name for the description.
> NOTE: dedup key is unique across ALL rows (Open+Resolved). Resolving a gap (Slice 5) must clear `DeduplicationKey` to free the slot for a recurrence. Document this on the service; do NOT build Resolve in Slice 3.
> Exactly-one-of (role XOR user): for Gap items we set role only. A general create helper enforcing exactly-one-of is Slice 5; keep Slice 3 minimal.

- [ ] Step 1: Failing `ActionItemServiceTests.cs`: `EnsureGap_creates_open_admin_gap_item` (dedup key, role=Admin, type=Gap); `EnsureGap_idempotent_second_call_no_duplicate` (call twice → 1 item); `ListOpenForRoleAsync_returns_admin_open_items`.
- [ ] Step 2: fail → Step 3 implement + register → Step 4 pass.
- [ ] Step 5: commit `feat(actionhub): ActionItemService (EnsureGapActionItemAsync + ListOpenForRoleAsync)`.

---

## Task 4: OutboxDrainer + wire SaveLessonLogAsync (HIGHEST RISK)

**Files:** Create `Shared/IOutboxDrainer.cs` + `OutboxDrainer.cs`; modify `Modules/Scheduling/SchedulingService.cs` (SaveLessonLogAsync CREATE path) + `Program.cs` (register + drain middleware); test `OutboxDrainerTests.cs`.

**SaveLessonLogAsync (CREATE path) modification:** it currently does a bare `_db.SaveChangesAsync()`. Change to: resolve `classId` (`await _db.ShiftInstances.Where(i=>i.Id==shiftInstanceId).Select(i=>i.Template.ClassId).FirstAsync()` — use `IgnoreQueryFilters()` so an archived template still resolves), build `OutboxEvent { EventType="LessonLogSaved", Payload=JsonSerializer.Serialize(new {classId, modelId}), CreatedAt=DateTime.UtcNow, ScheduledFor=null }`, `_db.OutboxEvents.Add(evt)`, then wrap the LessonLog Add + the OutboxEvent Add in ONE transaction: `await using var tx = await _db.Database.BeginTransactionAsync(); await _db.SaveChangesAsync(); await tx.CommitAsync();`. **Do NOT nest transactions** (replace the bare SaveChanges; the sequence is Begin → Add(log) → Add(evt) → SaveChanges → Commit). The UPDATE path (existing log) does NOT write an outbox event and keeps its bare SaveChanges. Preserve the snapshot-capture logic and all existing behavior — existing SchedulingServiceTests must stay green.

**IOutboxDrainer / OutboxDrainer:**
```csharp
public interface IOutboxDrainer { Task DrainAsync(CancellationToken ct = default); }
```
DrainAsync: fetch unprocessed due events `Where(e => e.ProcessedAt==null && (e.ScheduledFor==null || e.ScheduledFor<=now)).OrderBy(e=>e.Id).Take(50)`. For each: try `ProcessEventAsync` then set `ProcessedAt=UtcNow`; catch+log (do NOT set ProcessedAt on failure → retried). `SaveChangesAsync` per event. `ProcessEventAsync` switches on EventType: `"LessonLogSaved"` → deserialize `{classId,modelId}` → compute the gap (below) → if condition, `EnsureGapActionItemAsync`. Unknown type → log + mark processed (avoid infinite retry). Inject `AppDbContext` + `ActionItemService` + `ILogger`.

**Gap computation (in the drainer or a helper):**
```csharp
var spent = await _db.LessonLogs.IgnoreQueryFilters()
    .Where(l => l.ModelId==modelId && l.ShiftInstance.Template.ClassId==classId).CountAsync(ct);
var model = await _db.Models.FindAsync(modelId);
var alreadyCompleted = await _db.LessonLogs.IgnoreQueryFilters()
    .AnyAsync(l => l.ModelId==modelId && l.ShiftInstance.Template.ClassId==classId
                && l.Status==Scheduling.LessonLogStatus.Completed, ct);
if (spent > model.ExpectedLessonsToComplete + 1 && !alreadyCompleted)
    await _actionHub.EnsureGapActionItemAsync(classId, modelId, model.ExpectedLessonsToComplete, spent);
```
> `IgnoreQueryFilters()` is REQUIRED on these queries: the `ShiftTemplate` global filter would otherwise silently exclude lessons under archived templates, undercounting `spent`. (Confirmed correctness point from the architect.) Use LIVE `ExpectedLessonsToComplete` (spec §5A), not the per-log snapshot.

**Program.cs:** `AddScoped<IOutboxDrainer, OutboxDrainer>()`. Add an opportunistic drain middleware AFTER `app.UseAuthorization()`, BEFORE `app.MapRazorPages()`: on each authenticated request, after `next(ctx)`, fire-and-forget a drain in a FRESH scope from `IServiceScopeFactory` (the request scope is gone by then): `_ = Task.Run(async () => { using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IOutboxDrainer>().DrainAsync(); });`. Correctness backstop for double-drain is the dedup-key index (the BackgroundService is Slice 4).

- [ ] Step 1: Failing `OutboxDrainerTests.cs`: `SaveLessonLog_writes_outbox_event_in_same_transaction` (both LessonLog + OutboxEvent rows exist after a save); `Drain_LessonLogSaved_creates_gap_ticket_when_over_pace` (seed lessons so spent>expected+1 → drain → 1 Gap ActionItem); `Drain_no_ticket_when_within_pace`; `Drain_no_ticket_when_model_completed`; `Drain_idempotent_second_call_no_duplicate_ticket`; `Drain_marks_processed_at`; `Drain_counts_lessons_under_archived_template` (IgnoreQueryFilters correctness). Seed instructor users + class+syllabus+model+shifts.
- [ ] Step 2: fail → Step 3 implement (modify SaveLessonLogAsync, drainer, middleware) → Step 4 pass. **Run the existing SchedulingServiceTests too — confirm still green** (the transaction + outbox addition must not break them).
- [ ] Step 5: commit `feat(actionhub): Outbox drainer + Real-Gap monitor wired into LessonLog save (same-tx outbox + opportunistic drain)`.

---

## Task 5: OperationsService + Operations pages (UI)

**Files:** Create `Modules/Operations/OperationsService.cs` (register AddScoped); pages under `Pages/Operations/{ExtraHours,Incidents,Vacations}/`; tests `OperationsServiceTests.cs` + `OperationsPagesTests.cs`.
**Build UI to:** `docs/design/slice-3-operations-realgap-design.md` (Operations subnav, submit forms = instructor scale, approval lists = dense admin scale; reuse form-panel/data-table/segment/status-chip).

**OperationsService signatures:**
```csharp
Task<ExtraHours> SubmitExtraHoursAsync(int shiftInstanceId, int instructorId, decimal hours, string reason);
Task ApproveExtraHoursAsync(int extraHoursId, int approverUserId);        // guard Pending→Approved
Task<List<ExtraHours>> ListPendingExtraHoursAsync();                      // include Instructor + ShiftInstance.Template.Class
Task<List<ExtraHours>> ListExtraHoursByInstructorAsync(int instructorId);
Task<IncidentReport> SubmitIncidentReportAsync(int shiftInstanceId, int instructorId, IncidentSeverity severity, string description);
Task<List<IncidentReport>> ListIncidentsAsync(IncidentSeverity? severity = null);
Task<VacationRequest> RequestVacationAsync(int instructorId, DateOnly startDate, DateOnly endDate);  // validate start<=end, start>=today Israel
Task ApproveVacationAsync(int vacationRequestId, int approverUserId);     // guard Pending→Approved
Task DenyVacationAsync(int vacationRequestId, int approverUserId, string? adminNote);   // guard Pending→Denied
Task<List<VacationRequest>> ListPendingVacationsAsync();
Task<List<VacationRequest>> ListVacationsByInstructorAsync(int instructorId);
```
Approvals follow the Slice-2 `ApproveSubstitutionAsync` pattern (load + status guard + mutate + SaveChanges; single entity, no transaction needed).

**Pages:** instructor submit forms (`Create`) + admin/CS lists (`Index`) with approve/deny as HTMX row-swap handlers. Authz: submit = `Instructor` (or `InstructorOrAdmin`); approval lists = `Admin` (ExtraHours/Vacation approval is Admin); incident list = `CsOrAdmin`. Match the design doc's exact authz + Hebrew copy. Numerals/dates/hours in `<bdi class="num">`.

- [ ] Step 1: Failing `OperationsServiceTests.cs` (submit+approve ExtraHours; submit+approve/deny Vacation; submit Incident; double-approve guard throws) + `OperationsPagesTests.cs` (authz; an instructor submits → appears in admin pending list; admin approves → row swaps to Approved). HtmlDecode Hebrew bodies.
- [ ] Step 2: fail → Step 3 implement service + pages per design → Step 4 pass.
- [ ] Step 5: commit `feat(operations): OperationsService + Extra-Hours/Incident/Vacation pages (single-approval)`.

---

## Task 6: Minimal Action-Items Admin read page (UI)

**Files:** `Pages/Operations/ActionItems/Index.cshtml(.cs)` (or wherever the design doc places it); test in `OperationsPagesTests.cs`/new.
**Build to:** the design doc's minimal Action-Items view. `[Authorize(Roles = AppRoles.Admin)]`; calls `ActionItemService.ListOpenForRoleAsync(AppRoles.Admin)`; renders a simple list/table (type badge, description, created, related). READ-ONLY in Slice 3 (no resolve button — Slice 5). This is the visible proof of the Real-Gap path.

- [ ] Step 1: Failing test: non-Admin → 403/redirect; after seeding a Gap ActionItem (via ActionItemService), the page renders its Hebrew description (HtmlDecode).
- [ ] Step 2: fail → Step 3 implement → Step 4 pass.
- [ ] Step 5: commit `feat(actionhub): minimal Admin open-action-items read page (Real-Gap visibility)`.

---

## Task 7: Deploy + verify + handoff
- [ ] Step 1: Final `dotnet build -clp:ErrorsOnly && dotnet test` green; `dotnet ef migrations has-pending-model-changes` clean.
- [ ] Step 2: Whole-branch review (strongest model) — fix Critical/Important.
- [ ] Step 3: Merge to `master` (pre-authorized) → push → Render auto-deploys → boot applies `AddOperations` + `AddActionHubAndOutbox`.
- [ ] Step 4: Verify live: `/health` ok; an Operations route (e.g. `/Operations/Vacations` or `/Operations/ActionItems`) → 302 login for anon (proves new routes + migrations booted). Optionally smoke-test the gap path.
- [ ] Step 5: Update `docs/HANDOFF.md` + memory (Slice 3 live); commit.

---

## Self-Review (writing-plans checklist)
- **Spec §4 coverage:** ExtraHours/IncidentReport/VacationRequest ✔(T1); OutboxEvent + ActionItem(+dedup) ✔(T2); §5A Real-Gap (same-tx outbox → drain → Gap ActionItem, IgnoreQueryFilters count, live expected, dedup key) ✔(T4); single-approval ✔(T5); minimal hub visibility ✔(T6).
- **Archival:** NO filter on any Slice 3 entity ✔.
- **Highest risk:** T4 (transaction boundary, opportunistic-drain double-processing, dedup race, IgnoreQueryFilters count correctness) — extra test coverage there.
- **Deferred (disclosed):** IncidentReport→ActionItem, ResolveActionItemAsync, JobExecutionLog + BackgroundService scheduler, event-driven absence/dropout, tryout-followup + birthday scheduled events, Logistics, SELECT-FOR-UPDATE drain, stale-gap auto-resolve — all Slice 4/5 per the roadmap. NOT pulled forward.
