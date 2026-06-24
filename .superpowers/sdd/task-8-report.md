## Task 8 Report — SupplyPacingService + Logistics Dispute Loop

### Files Changed
- `src/Orkabi.Web/Modules/Logistics/SupplyPacingService.cs` — new file (service implementation)
- `src/Orkabi.Web/Program.cs` — added `AddScoped<SupplyPacingService>()`
- `tests/Orkabi.Web.Tests/LogisticsServiceTests.cs` — new file (13 tests)

---

### 5 Methods Implemented

**1. SeedOrdersForClassAsync(int classId)**
- Loads class via `IgnoreQueryFilters()` (handles archived classes).
- If `SyllabusId` is null → returns empty list immediately.
- Loads syllabus with `IgnoreQueryFilters()` + `Include(SyllabusModels.OrderBy(OrderIndex)).ThenInclude(Model)`.
- For each SyllabusModel: checks LessonLog existence via `LessonLogs.AnyAsync(l => l.ModelId == sm.ModelId && l.ShiftInstance.Template.ClassId == classId)` (the only path from LessonLog to ClassId — through ShiftInstance → ShiftTemplate → ClassId).
- If no LessonLog → skip. If non-Disputed order already exists → skip (idempotent gate).
- Creates `LogisticsOrder { ClassId, ModelId, Quantity=1, Status=Pending }`.
- Single `SaveChangesAsync` for the batch. Returns only the newly created orders.

**2. MarkPackedAsync(int orderId, int logisticsUserId)**
- Loads order; throws `InvalidOperationException` if `Status != Pending`.
- Sets `Status = Packed`; `SaveChangesAsync`.

**3. MarkAcceptedAsync(int orderId, int instructorUserId)**
- Loads order; throws `InvalidOperationException` if `Status != Packed`.
- Sets `Status = Accepted`, `DeliveredAt = DateTime.UtcNow`; `SaveChangesAsync`.

**4. MarkDisputedAsync(int orderId, int instructorUserId, string disputeNotes)**
- Loads order; throws `InvalidOperationException` if `Status != Packed`.
- Opens a non-nested transaction via `BeginTransactionAsync`.
- Sets `Status = Disputed`, `DisputeNotes = disputeNotes`; `SaveChangesAsync`.
- Calls `_actionHub.EnsureDisputeActionItemAsync(orderId, order.ClassId)` — EnsureDispute does its own SaveChanges + DbUpdateException-catch inside the same transaction (SQLite supports this pattern; EnsureDispute does not open its own nested transaction, only does EF SaveChanges).
- `CommitAsync`.

**5. ListOrdersAsync(LogisticsOrderStatus? status, int? classId)**
- Filters by optional `status` and optional `classId`.
- `.Include(o => o.Class).Include(o => o.Model)`.
- Orders by `Status` then `ClassId`.

---

### Seeding Rule

A `LogisticsOrder` (Pending, qty=1) is created for `(classId, modelId)` only when:
1. The class has a `SyllabusId` (non-null).
2. The syllabus includes the model (as a `SyllabusModel`).
3. A `LessonLog` exists linking `(classId, modelId)` via `LessonLog.ShiftInstance.Template.ClassId == classId && LessonLog.ModelId == modelId`.
4. No existing non-Disputed order for `(classId, modelId)` already exists.

A second call creates no new orders (idempotent).

---

### Dispute Transaction

```
BeginTransactionAsync
  → order.Status = Disputed; order.DisputeNotes = notes
  → SaveChangesAsync
  → EnsureDisputeActionItemAsync(orderId, classId)   ← dedup key: dispute_{orderId}
      (EnsureDispute does its own SaveChanges; DbUpdateException-catch for concurrent insert race)
CommitAsync
```

The `ActionItemService.EnsureDisputeActionItemAsync` pattern (SaveChanges + DbUpdateException catch) is safe inside a transaction on SQLite — EF SaveChanges writes to the DB but the outer transaction controls commit/rollback atomicity.

---

### RED → GREEN

**RED:** Build failed with 13 CS0246 errors (`SupplyPacingService` not found) — confirmed by `dotnet build`.

**GREEN:** After implementing `SupplyPacingService` and registering in `Program.cs`:
- `dotnet test` → **187 passed, 0 failed** (174 pre-existing + 13 new)

---

### Self-Review

- `IgnoreQueryFilters` applied to `Classes` and `Syllabi` lookups (both are archivable aggregate roots with global query filters). `LessonLogs`, `LogisticsOrders` are NOT archivable — no filter needed.
- Idempotency gate checks `Status != Disputed` to allow re-seeding after a dispute (per spec).
- Transaction in `MarkDisputedAsync` is non-nested; `BeginTransactionAsync` is called only once per request.
- `FindAsync` with `new object[] { orderId }` used to avoid ambiguity in EF Core 9.
- `ListOrdersAsync` uses `.Where` before `.Include` for EF efficiency.

---

### Concerns

- None. All 13 tests cover the specified scenarios: seeding, idempotency, missing lesson log, no-syllabus, all 3 state transitions, all 3 guard failures, dispute action item creation, and list filtering/navigation properties.

---

## Fix — IgnoreQueryFilters on Archived ShiftTemplate / Class (post-review patch)

### Root Cause

`ShiftTemplate` carries a global query filter (`Status == EntityStatus.Active`). Two call sites in `SupplyPacingService` navigated through `ShiftTemplate` (or `Class`) without calling `IgnoreQueryFilters()`, causing silent data loss:

1. **`SeedOrdersForClassAsync` — lesson-log check** (`_db.LessonLogs.AnyAsync(...)`): the predicate navigates `l.ShiftInstance.Template.ClassId`. Because `ShiftTemplate` has the global filter, EF Core applies it to the join, so a `LessonLog` whose `ShiftTemplate` was later archived is excluded → zero orders seeded for that model.

2. **`ListOrdersAsync`** (`_db.LogisticsOrders ... .Include(o => o.Class)`): `Class` also has a global filter. When the class is archived, the `Include` silently nulls the navigation → `o.Class` is null in the returned list, breaking display.

### Additions

**`src/Orkabi.Web/Modules/Logistics/SupplyPacingService.cs`**

- `SeedOrdersForClassAsync`: Added `.IgnoreQueryFilters()` immediately after `_db.LessonLogs` on the "has lesson log" `AnyAsync` call. Predicate is otherwise identical.
- `ListOrdersAsync`: Added `.IgnoreQueryFilters()` immediately after `_db.LogisticsOrders`. Status/classId filters, Includes, and ordering are otherwise identical.

**`tests/Orkabi.Web.Tests/LogisticsServiceTests.cs`**

New test `SeedOrders_seeds_even_when_shift_template_archived`:
- Seeds class + syllabus + model + ShiftTemplate (Active) + ShiftInstance + LessonLog via existing helpers.
- Archives the ShiftTemplate (`template.Status = EntityStatus.Archived` + `SaveChangesAsync` + `ChangeTracker.Clear()`).
- Calls `SeedOrdersForClassAsync(cls.Id)`.
- Asserts exactly one Pending order is created — proving the archived template no longer hides the lesson log.
- RED without the fix (zero orders seeded); GREEN with it (one order seeded).

### Suite Result

`dotnet build` → **0 errors, 0 warnings**.  
`dotnet test` → **188 passed, 0 failed** (187 pre-existing + 1 new).
