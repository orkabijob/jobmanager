# Slice 4 — Logistics + Automations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development — fresh implementer per task, task-review gate after each, whole-branch review before merge. Steps use `- [ ]`.
> **Design reference (binding for UI task 9):** `docs/design/slice-4-logistics-design.md`. **Spec:** `docs/superpowers/specs/2026-06-23-orkabi-design.md` §4/§5C/§5D/§5E/§6.

**Goal:** The in-process **`BackgroundService` scheduler** (PeriodicTimer Asia/Jerusalem + catch-up-on-wake + `JobExecutionLog`) running birthdays + shift-generation; the **event-driven automations** (double-consecutive-absence, mass drop-out, deferred tryout follow-up) via the Slice-3 Outbox + new drainer branches; and **Logistics** (LogisticsOrder + dispute loop + SupplyPacingService). Extends the Slice-3 Outbox/Action-Item kernel.

**Architecture:** New `Modules/Logistics/` + `Jobs/` + `JobExecutionLog` in `Shared/`. The scheduler is a singleton `IHostedService` that creates a DI scope per run; all job logic is extracted into a scoped `IDailyJobRunner` so it is testable WITHOUT a real timer. Event automations write same-transaction OutboxEvents (consistent with Real-Gap) drained by new `OutboxDrainer` branches; mass-dropout is inline. All new Action-Item creators are dedup-keyed. Reuses all Slice-0..3 patterns (BaseEntity/audit, archival filter on aggregate roots only, thin services, additive Npgsql migrations at boot, SQLite EnsureCreated tests, the `!Testing` env gate).

**Tech stack:** ASP.NET Core 8 Razor Pages + HTMX, EF Core 9 (Npgsql/Sqlite), ASP.NET Identity (int keys), `Microsoft.Extensions.Hosting` BackgroundService, xUnit + WebApplicationFactory.

## Global Constraints
- **Branch:** `slice-4-logistics-automations`; merge to `master` only at the deploy task (deploys pre-authorized — proceed after whole-branch review + green).
- **Build/test gate:** `dotnet build` clean; `dotnet test` green at every task boundary (suite is currently 134). After any migration, ensure `AppDbContextModelSnapshot.cs` is staged (`git status`) AND run `dotnet ef migrations has-pending-model-changes` (must be clean) before committing.
- **Implementers COMMIT with explicit paths** (never `git add -A`) — avoids the commit-bundling race.
- **Hebrew-only RTL;** logical CSS; numerals/dates/quantities in `<bdi class="num">`.
- **Archival:** NO query filter on LogisticsOrder or JobExecutionLog (operational/infra). Int-backed enums `.HasConversion<int>()`. FKs `Restrict`.
- **Scheduler MUST be gated under Testing** (the BackgroundService loop must not run in tests — `IHostEnvironment.IsEnvironment("Testing")` → `await Task.Delay(Timeout.Infinite, ct)`). Tests exercise job logic via `IDailyJobRunner` directly.
- **Scope-per-run:** the BackgroundService injects ONLY `IServiceScopeFactory` + `ILogger` + `IHostEnvironment` — never `AppDbContext` directly. Each run creates `CreateScope()`.
- **Dedup-key invariant:** every new ActionItem creator's dedup key is unique across ALL rows (Open+Resolved); resolving (Slice 5) must null it. Document on each method. Catch `DbUpdateException` + `ChangeTracker.Clear()` on the unique-index race (the established pattern).
- **VERIFIED facts:** EF9-SQLite enforces unique + partial indexes; `BeginTransactionAsync` works on SQLite (no nesting); the OutboxDrainer already filters `ScheduledFor <= now` (deferred events need only new `ProcessEventAsync` branches); the opportunistic drain middleware stays (the BackgroundService loop is the idle backstop; both are safe via the dedup index).

---

## Task 1: LogisticsOrder entity + AddLogistics migration
**Files:** Create `Modules/Logistics/LogisticsOrderStatus.cs`, `LogisticsOrder.cs`; modify `AppDbContext.cs`; migration `<ts>_AddLogistics`; test `LogisticsEntityTests.cs`.

**Enum:** `public enum LogisticsOrderStatus { Pending=0, Packed=1, Accepted=2, Disputed=3 }`
**Entity** (`: BaseEntity`, NOT IArchivable):
```csharp
public class LogisticsOrder : BaseEntity {
    public int ClassId; public People.Class Class = null!;
    public int ModelId; public Curriculum.Model Model = null!;
    public int Quantity = 1;
    public LogisticsOrderStatus Status = LogisticsOrderStatus.Pending;
    public string? DisputeNotes;   // max 500; set on Disputed
    public DateTime? DeliveredAt;  // UTC; set on Accepted (NOT on Packed)
}
```
**Config:** `using Logistics = ...;`; DbSet; `Status.HasConversion<int>()`; `DisputeNotes` max 500; FKs ClassId→Classes + ModelId→Models, ALL `Restrict`; non-unique index `(ClassId, ModelId)`. NO query filter (NOT IArchivable).
- [ ] Step1: failing `LogisticsEntityTests` (round-trip; default Pending; FK enforcement for ClassId+ModelId). Step2 fail → Step3 entity+config → Step4 pass.
- [ ] Step5: `dotnet ef migrations add AddLogistics`. Confirm Up() creates LogisticsOrders (Restrict FKs, the index), no drops; snapshot staged; `has-pending-model-changes` clean.
- [ ] Step6: build+test green; commit (explicit paths) `feat(logistics): LogisticsOrder entity + migration`.

---

## Task 2: JobExecutionLog entity + AddJobExecutionLog migration
**Files:** Create `Shared/JobExecutionLog.cs` (+ `JobExecutionStatus` enum); modify `AppDbContext.cs`; migration `<ts>_AddJobExecutionLog`; test `JobExecutionLogTests.cs`.

**Entity** (NOT BaseEntity — infrastructure; audit interceptor must not touch it):
```csharp
public enum JobExecutionStatus { Started=0, Succeeded=1, Failed=2 }
public class JobExecutionLog {
    public int Id { get; set; }
    public string JobName { get; set; } = "";       // max 100
    public DateOnly ScheduledFor { get; set; }       // the Israel civil date the run is FOR
    public DateTime RanAt { get; set; }              // UTC
    public JobExecutionStatus Status { get; set; }
}
```
**Config:** DbSet; `Status.HasConversion<int>()`; `JobName` max 100; **unique index `(JobName, ScheduledFor)`** (the idempotency key). NO query filter.
- [ ] Step1: failing `JobExecutionLogTests` — insert a row; duplicate `(JobName, ScheduledFor)` → DbUpdateException; a DIFFERENT ScheduledFor allows a new row. (Proves the idempotency gate before the scheduler exists.) Step2 fail → Step3 → Step4 pass.
- [ ] Step5: `dotnet ef migrations add AddJobExecutionLog`. Confirm Up() creates JobExecutionLogs with the unique index; DateOnly→date(Npgsql); snapshot staged; has-pending clean.
- [ ] Step6: build+test green; commit `feat(jobs): JobExecutionLog (unique (JobName,ScheduledFor) idempotency) + migration`.

---

## Task 3: IDailyJobRunner + DailyJobService (job logic, timer-free)
**Files:** Create `Jobs/IDailyJobRunner.cs`, `Jobs/DailyJobService.cs`; register AddScoped; test `DailyJobServiceTests.cs`.

**Interface:**
```csharp
Task RunBirthdayCheckAsync(DateOnly todayIsrael, CancellationToken ct = default);
Task RunShiftGenerationAsync(DateOnly todayIsrael, CancellationToken ct = default);
```
**DailyJobService** (scoped; inject `AppDbContext` + `IShiftInstanceGenerator` + `ActionItemService`):
- **RunBirthdayCheckAsync:** for each active Client with Birthday set, if birthday month/day == todayIsrael → day-of; if == tomorrowIsrael → 24h-before. "Assigned instructor" = for each of the client's non-Dropped/non-Completed enrollments, the active `ShiftTemplate.DefaultInstructorId` for that class+current-year (query `ShiftTemplates.IgnoreQueryFilters()` to bypass the archival filter; filter Status=Active + current AcademicYearId). Create one ActionItem per (class/instructor) + one to Admin, via the Task-4 creators. (Day-of and 24h use distinct creators/keys.)
- **RunShiftGenerationAsync:** `await _generator.GenerateAllActiveAsync(30, ct);` (idempotent already).
- [ ] Step1: failing `DailyJobServiceTests` — seed clients with birthdays today + tomorrow (+ an active enrollment with a template+instructor); call RunBirthdayCheckAsync(today) → assert correct ActionItems (type Birthday, correct role/user, dedup keys); idempotent (2nd call → no dup); no-birthday client → none. RunShiftGenerationAsync → instances generated (delegates to the tested generator). Seed instructor users via UserManager.
- [ ] Step2 fail → Step3 implement + register → Step4 pass.
- [ ] Step5: commit `feat(jobs): IDailyJobRunner + DailyJobService (birthday + shift-gen logic, timer-free)`.
> NOTE: Task 3 depends on the Task-4 ActionItem creators for the birthday tickets. To keep TDD order, either (a) do Task 4 BEFORE Task 3, or (b) add the birthday creators in Task 3 and the rest in Task 4. RECOMMEND swapping: do the ActionItemService creators (current Task 4) FIRST, then DailyJobService. The implementer/controller may reorder Tasks 3↔4; the plan lists them logically.

---

## Task 4: ActionItemService new creators
**Files:** modify `Modules/ActionHub/ActionItemService.cs`; extend `ActionItemServiceTests.cs`.
Add (each follows the `EnsureGapActionItemAsync` pattern: dedup-check-open → build → Add → try SaveChanges catch DbUpdateException+Clear; load names for Hebrew description; dedup-invariant doc comment):
```csharp
Task EnsureDoubleAbsenceActionItemAsync(int clientId, int classId);   // role=CustomerService; key absence_double_{clientId}_{classId}; Type=Absence
Task EnsureMassDropoutActionItemAsync(int classId);                    // role=Admin; key dropout_mass_{classId}; Type=Absence (or a fitting type — use Absence per spec "drop-out")  -- NOTE: spec lists types Absence/Gap/Dispute/Task/Birthday/Tryout_Followup; mass-dropout has no dedicated type → use Absence with a distinct key, OR add nothing new. Use Type=Absence, key dropout_mass_{classId}.
Task EnsureDisputeActionItemAsync(int logisticsOrderId, int classId); // role=Admin; key dispute_{logisticsOrderId}; Type=Dispute
Task EnsureBirthdayDayOfActionItemAsync(int clientId, int? instructorId, DateOnly birthday);   // instructor item (user) + admin item (role); keys birthday_dayof_{clientId}_{birthday:yyyy-MM-dd}_user_{id} / _admin; Type=Birthday; DueDate=birthday
Task EnsureBirthday24hActionItemAsync(int clientId, int? instructorId, DateOnly birthday);      // keys birthday_24h_...; Type=Birthday
Task EnsureTryoutFollowupActionItemAsync(int clientId, int classId);  // role=CustomerService; key tryout_followup_{clientId}_{classId}; Type=TryoutFollowup
```
> The birthday creators: when instructorId is provided create the instructor (user-assigned) item; ALWAYS also create the admin (role-assigned) item. Keep exactly-one-of (role XOR user) per ActionItem. Provide overloads or a clear contract so the caller (DailyJobService) makes the instructor + admin calls.
- [ ] Step1: failing tests — one per creator: correct Type/role-or-user/dedup key/Hebrew description; idempotent (2nd call → no dup). (Dispute test seeds a LogisticsOrder.) Step2 fail → Step3 → Step4 pass.
- [ ] Step5: commit `feat(actionhub): Absence/Dispute/Birthday/TryoutFollowup ActionItem creators (dedup-keyed)`.

---

## Task 5: DailyJobScheduler BackgroundService
**Files:** Create `Jobs/DailyJobScheduler.cs`; modify `Program.cs` (`AddHostedService<DailyJobScheduler>()`); test (light) — see note.
**Design:** `BackgroundService` + `PeriodicTimer(TimeSpan.FromMinutes(5))`. Inject `IServiceScopeFactory`, `ILogger<DailyJobScheduler>`, `IHostEnvironment`.
```
ExecuteAsync(stoppingToken):
  if (env.IsEnvironment("Testing")) { await Task.Delay(Timeout.Infinite, stoppingToken); return; }
  await RunPassAsync(stoppingToken);                 // immediate catch-up on startup/wake
  while (await timer.WaitForNextTickAsync(stoppingToken)) await RunPassAsync(stoppingToken);
RunPassAsync:
  todayIsrael = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelClock.IsraelTz))
  await TryRunJobAsync("BirthdayCheck", todayIsrael, (r,d,ct)=>r.RunBirthdayCheckAsync(d,ct))
  await TryRunJobAsync("ShiftGeneration", todayIsrael, (r,d,ct)=>r.RunShiftGenerationAsync(d,ct))
  // drain the outbox each tick (deferred events fire when due)
  using var scope = _factory.CreateScope(); await scope.GetService<IOutboxDrainer>().DrainAsync(ct);
TryRunJobAsync(jobName, today, body):
  using scope; var db = scope.AppDbContext;
  if (await db.JobExecutionLogs.AnyAsync(j=>j.JobName==jobName && j.ScheduledFor==today)) return;   // fast-skip
  try { db.Add(new JobExecutionLog{JobName,ScheduledFor=today,RanAt=UtcNow,Status=Started}); await db.SaveChangesAsync(); }
  catch (DbUpdateException) { db.ChangeTracker.Clear(); return; }   // another instance won the race
  try { await body(scope.GetService<IDailyJobRunner>(), today, ct); /* set Status=Succeeded */ }
  catch (Exception ex) { log; /* set Status=Failed */ }  finally { await db.SaveChangesAsync(); }
```
Each job + the drain runs in its OWN scope (don't share AppDbContext across jobs). Wrap each pass in try/catch so one job's failure doesn't kill the loop.
- [ ] Step1: implement; register hosted service. The loop is Testing-gated, so there's no flaky timer in tests. Verify existing suite stays green (the hosted service registered but dormant). Add a light test if feasible (e.g. that the app still boots green) — the job LOGIC is already covered by Task 3 tests.
- [ ] Step2: build + full test green; commit `feat(jobs): DailyJobScheduler BackgroundService (catch-up-on-wake, JobExecutionLog idempotency, Testing-gated, drains outbox)`.

---

## Task 6: Event-driven attendance hooks (double-absence + tryout-followup)
**Files:** modify `Modules/Scheduling/SchedulingService.cs` (`RecordAttendanceAsync`); modify `Shared/OutboxDrainer.cs` (2 new branches); test `AttendanceAutomationTests.cs`.
**SchedulingService.RecordAttendanceAsync (CREATE/success path only — NOT the DbUpdateException idempotency-retry path):** resolve `classId` once (`_db.LessonLogs.IgnoreQueryFilters().Where(l=>l.Id==lessonLogId).Select(l=>l.ShiftInstance.Template.ClassId).FirstAsync`). If `status==Absent`: append OutboxEvent `{EventType="AttendanceAbsent", Payload={clientId,lessonLogId}, ScheduledFor=null}` + SaveChanges (append-after-commit; the attendance row is already saved — prefer a missed ticket over rolling back attendance). If `status==Present` AND the enrollment is tryout (`Status==Tryout || IsTryout` for (clientId,classId)): append OutboxEvent `{EventType="TryoutPresent", Payload={clientId,classId}, ScheduledFor=tomorrow 08:00 Israel (compute via IsraelClock: ConvertTimeFromUtc→date.AddDays(1).AddHours(8)→ConvertTimeToUtc)}` + SaveChanges.
**OutboxDrainer.ProcessEventAsync new branches:**
- `"AttendanceAbsent"`: deserialize {clientId,lessonLogId}; resolve classId+thisDate via the lessonLog's ShiftInstance (IgnoreQueryFilters); find the client's PREVIOUS attendance in that class (Attendances join LessonLog→ShiftInstance→Template where ClassId & ClientId & ShiftInstance.Date<thisDate, order date desc, take 1); if previous Status==Absent → `EnsureDoubleAbsenceActionItemAsync(clientId, classId)`.
- `"TryoutPresent"`: deserialize {clientId,classId}; re-verify still tryout; if so → `EnsureTryoutFollowupActionItemAsync(clientId, classId)`.
- [ ] Step1: failing `AttendanceAutomationTests` — two consecutive Absent attendances → drain → 1 Absence ActionItem (dedup); absent-then-present → none; tryout Present → OutboxEvent with future ScheduledFor → (set ScheduledFor to past, drain) → TryoutFollowup ActionItem. Existing `AttendanceTests`/`SchedulingServiceTests` stay green.
- [ ] Step2 fail → Step3 implement → Step4 pass.
- [ ] Step5: commit `feat(automations): double-absence + tryout-followup via same-tx outbox + drainer branches`.

---

## Task 7: Mass-dropout hook (ClientService)
**Files:** modify `Modules/People/ClientService.cs` (add `DeactivateAsync` + inject `ActionItemService`); modify `Program.cs` if needed (already AddScoped); test `ClientDeactivationTests.cs`.
**ClientService.DeactivateAsync(int clientId):** set `IsActive=false`, SaveChanges (audit interceptor stamps UpdatedAt). Then for each of the client's non-Dropped enrollment classes: count OTHER clients in that class who are `!IsActive` AND `Client.UpdatedAt >= now-7d` (and non-Dropped enrollment); if `>= 1` (current makes ≥2 total within 7d) → `EnsureMassDropoutActionItemAsync(classId)`. Document the "≥2 within 7 days" interpretation + the UpdatedAt-as-deactivation-timestamp approximation. Inject `ActionItemService` into ClientService's constructor (consistent with SchedulingService taking EnrollmentService).
- [ ] Step1: failing `ClientDeactivationTests` — class with 3 clients; deactivate 2 within 7d (set UpdatedAt) then the 3rd via DeactivateAsync → MassDropout ActionItem for the class; deactivating only 1 → none. Step2 fail → Step3 → Step4 pass.
- [ ] Step5: commit `feat(automations): mass-dropout ActionItem on ClientService.DeactivateAsync`.

---

## Task 8: SupplyPacingService + Logistics dispute loop
**Files:** Create `Modules/Logistics/SupplyPacingService.cs` (register AddScoped); test `LogisticsServiceTests.cs`.
**Methods:**
```csharp
Task<List<LogisticsOrder>> SeedOrdersForClassAsync(int classId);   // for each syllabus model where a LessonLog exists for (class,model) AND no non-Disputed order exists → create Pending qty 1. Use Class.SyllabusId → GetSyllabusAsync (ordered models). IgnoreQueryFilters when joining Class for names.
Task MarkPackedAsync(int orderId, int logisticsUserId);            // guard Status==Pending → Packed
Task MarkAcceptedAsync(int orderId, int instructorUserId);         // guard Status==Packed → Accepted; DeliveredAt=UtcNow
Task MarkDisputedAsync(int orderId, int instructorUserId, string disputeNotes);  // guard Status==Packed → Disputed; DisputeNotes set; in a TRANSACTION: set status+notes, SaveChanges, EnsureDisputeActionItemAsync(orderId, classId), Commit
Task<List<LogisticsOrder>> ListOrdersAsync(LogisticsOrderStatus? status, int? classId);  // include Class + Model
```
- [ ] Step1: failing `LogisticsServiceTests` — seed class+syllabus+model+lessonlog; SeedOrdersForClassAsync → 1 Pending; 2nd call → no dup; MarkPacked (Pending→Packed); MarkAccepted (Packed→Accepted, DeliveredAt set); MarkDisputed (Packed→Disputed, notes set, Dispute ActionItem created); transition guard (MarkPacked on non-Pending throws). Step2 fail → Step3 → Step4 pass.
- [ ] Step5: commit `feat(logistics): SupplyPacingService — seed orders + Packed/Accepted/Disputed dispute loop`.

---

## Task 9: Logistics UI pages
**Files:** `Pages/Logistics/...` per the design doc; test `LogisticsPagesTests.cs`.
**Build to:** `docs/design/slice-4-logistics-design.md` (Logistics subnav; orders list dense admin/Logistics with status chips + Mark-Packed HTMX row-swap; a `צור הזמנות` seed action for Admin/Logistics; instructor Accept/Dispute action with a dispute-notes form/modal; status chips for Pending/Packed/Accepted/Disputed — add a `.status-chip--packed` only if the design needs it).
- Authz: orders list + Mark-Packed + seed = `LogisticsOrAdmin` (Logistics/Admin); instructor Accept/Dispute = instructor (resource-scoped to their class) or `InstructorOrAdmin`. Match the design's exact authz + Hebrew copy. Status changes via HTMX row-swap returning a `_OrderRow` partial. No placeholder data. Numerals/dates in `<bdi class="num">`.
- [ ] Step1: failing `LogisticsPagesTests` — authz (anon 302; wrong role 403); Logistics marks Packed → row swaps; Instructor disputes → status Disputed + an Admin Dispute ActionItem exists; Admin seeds → orders appear. HtmlDecode Hebrew. Step2 fail → Step3 (per design) → Step4 pass.
- [ ] Step5: commit `feat(logistics): orders list + dispute-loop pages (HTMX) + seed action`.

---

## Task 10: Deploy + verify + handoff
- [ ] Step1: final `dotnet build -clp:ErrorsOnly && dotnet test` green; `dotnet ef migrations has-pending-model-changes` clean.
- [ ] Step2: whole-branch review (strongest model) — fix Critical/Important.
- [ ] Step3: merge to `master` (pre-authorized) → push → Render auto-deploys → boot applies `AddLogistics` + `AddJobExecutionLog`; the DailyJobScheduler starts (prod) and runs catch-up.
- [ ] Step4: verify live: `/health` ok; a Logistics route (e.g. `/Logistics/Orders`) → 302 login for anon (proves new routes + migrations booted). (The scheduler running is implicit in a successful boot.)
- [ ] Step5: update `docs/HANDOFF.md` + memory (Slice 4 live); commit.

---

## Self-Review (writing-plans checklist)
- **Spec coverage:** LogisticsOrder + dispute loop (§4/§5C) ✔(T1,T8,T9); JobExecutionLog + scheduler + catch-up (§6) ✔(T2,T3,T5); birthdays + shift-gen (§5E scheduled) ✔(T3,T4,T5); double-absence + mass-dropout (§5E event-driven) ✔(T4,T6,T7); tryout follow-up deferred outbox (§5D/§5E) ✔(T4,T6); SupplyPacingService (§5C) ✔(T8).
- **Archival:** NO filter on LogisticsOrder/JobExecutionLog ✔. Scheduler Testing-gated + scope-per-run ✔. Dedup-keyed creators + DbUpdateException-catch ✔.
- **Highest risk:** T5 (scheduler/catch-up/JobExecutionLog idempotency) + T6 (attendance hooks not double-firing on idempotency retry; IgnoreQueryFilters count) — extra coverage; job logic tested timer-free via T3.
- **Recommended task order:** T1, T2, T4 (creators), T3 (uses creators), T5, T6, T7, T8, T9, T10. (T4 before T3.)
- **Deferred (disclosed):** ResolveActionItemAsync + Action Hub polling UI + Admin bento + Logistics master packing list → Slice 5; SELECT-FOR-UPDATE drain locking → later; Google OAuth E2E → unchanged. NOT pulled forward.
