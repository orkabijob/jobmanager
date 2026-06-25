# Slice 5 — Action Hub + Real Dashboards Implementation Plan (FINAL slice)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development — fresh implementer per task, task-review gate after each, whole-branch review before merge. Steps use `- [ ]`.
> **Design reference (binding for UI tasks):** `docs/design/slice-5-action-hub-dashboards-design.md`. **Spec:** `docs/superpowers/specs/2026-06-23-orkabi-design.md` §3 (Action Hub polling-first), §4 (Action_Item resolved fields), §5/§7 (dashboards).

**Goal:** Close the loop — the **Action Hub** (role-aware, polling, with a **Resolve** action that frees the dedup slot) surfaces every automation's Action_Items; replace the Admin **placeholder bento** with real metrics + build real CS/Logistics dashboards; the Logistics **packing list**; syllabus→class assignment; and carryover polish (shared shell partial for new pages, save-success toasts, dynamic greeting).

**Architecture:** Extend the Slice-3 Action-Item kernel with a resolve flow (the dedup-key-null-on-resolve is the lynchpin). Generalize the existing Admin-only `/Operations/ActionItems` page into the role-aware hub (no second page). New read-only `DashboardMetricsService` (cross-cutting consumer, reads DbSets directly). Reuse all Slice-0..4 patterns (BaseEntity/audit, archival filter on aggregate roots only, thin services, additive Npgsql migration at boot, SQLite EnsureCreated tests, HTMX row/fragment swaps + antiforgery, the `!Testing` env gate, the Liquid-Glass vocabulary).

**Tech stack:** ASP.NET Core 8 Razor Pages + HTMX, EF Core 9 (Npgsql/Sqlite), ASP.NET Identity (int keys), xUnit + WebApplicationFactory.

## Global Constraints
- **Branch:** `slice-5-action-hub-dashboards`; merge to `master` only at the deploy task (deploys pre-authorized — proceed after whole-branch review + green).
- **Build/test gate:** `dotnet build` clean; `dotnet test` green at every task boundary (suite is currently 201). After the migration, ensure `AppDbContextModelSnapshot.cs` is staged AND run `dotnet ef migrations has-pending-model-changes` (must be clean) before committing.
- **Implementers COMMIT with explicit paths** (never `git add -A`).
- **Hebrew-only RTL;** logical CSS; numerals/counts/dates in `<bdi class="num">` (NOT inside `<option>`); HTMX partials return one fragment.
- **DEDUP-KEY LYNCHPIN:** `ResolveActionItemAsync` MUST set `DeduplicationKey = null` so the next automation cycle can re-create a ticket for the same entity (the partial unique index `HasFilter("DeduplicationKey IS NOT NULL")` only covers non-null keys; a Resolved row keeping its key would suppress recurrences FOREVER). This MUST have a dedicated test (resolve → a fresh `Ensure*` creates a new Open ticket).
- **Authz handler-guards (Slice-3/4 lesson):** every privileged POST handler guards IN the handler (first lines), not only via `[Authorize]`. Resolve authz: role-assigned item → resolvable by anyone in that role OR Admin; user-assigned item → only that user OR Admin; else `Forbid()`.
- **Connection string:** PROD uses the **DIRECT** (non-pooled) Neon endpoint (the pooled string was dropped — paste issue). Polling at ~25-30s for a handful of staff is negligible on direct. Implementers MUST NOT touch connection strings or the pooler.
- **Do NOT refactor the 20 live Slice-0..4 pages** to a shared shell (risk). The `_PageShell`/`_SectionShell` partial is for NEW Slice-5 pages only; existing-page migration is an explicitly-deferred separate effort.
- **VERIFIED facts:** EF9-SQLite enforces partial unique + FK; `IgnoreQueryFilters()` needed to read archived-Class data; `IsraelClock.IsraelTz` for "today"; the OutboxDrainer/scheduler unchanged this slice.

---

## Task 1: ActionItem resolve flow (entity + migration + service)
**Files:** modify `Modules/ActionHub/ActionItem.cs`, `Data/AppDbContext.cs`, `Modules/ActionHub/ActionItemService.cs`; migration `<ts>_AddActionItemResolvedFields`; extend `tests/Orkabi.Web.Tests/ActionItemServiceTests.cs`.

**FIRST verify** `ActionItem.cs` does NOT already have ResolvedByUserId/ResolvedAt (architect says it doesn't). Add:
```csharp
public int? ResolvedByUserId { get; set; }
public DateTime? ResolvedAt { get; set; }
```
**AppDbContext config:** optional FK `ResolvedByUserId → AppUser` `OnDelete(SetNull)` (`.HasOne<AppUser>().WithMany().HasForeignKey(a => a.ResolvedByUserId).OnDelete(DeleteBehavior.SetNull)`); ResolvedAt as timestamptz. No query filter change.

**ActionItemService — add:**
```csharp
// Resolve: set Resolved + resolver/at + NULL the dedup key (lynchpin). Idempotent no-op if already Resolved.
public async Task ResolveActionItemAsync(int actionItemId, int resolvedByUserId) {
    var item = await _db.ActionItems.FirstOrDefaultAsync(a => a.Id == actionItemId);
    if (item is null || item.Status == ActionItemStatus.Resolved) return;   // double-resolve no-op
    item.Status = ActionItemStatus.Resolved;
    item.ResolvedByUserId = resolvedByUserId;
    item.ResolvedAt = DateTime.UtcNow;
    item.DeduplicationKey = null;                                            // LYNCHPIN — frees the slot
    await _db.SaveChangesAsync();
}
// Hub query: a user's open queue = role-assigned to their role OR user-assigned to them.
public Task<List<ActionItem>> ListOpenForUserAndRoleAsync(int userId, string role) =>
    _db.ActionItems.Where(a => a.Status == ActionItemStatus.Open && (a.AssignedToRole == role || a.AssignedToUserId == userId))
        .OrderBy(a => a.CreatedAt).ToListAsync();
// Admin "everything open".
public Task<List<ActionItem>> ListAllOpenAsync() =>
    _db.ActionItems.Where(a => a.Status == ActionItemStatus.Open).OrderBy(a => a.CreatedAt).ToListAsync();
// (Optional) a user's user-assigned-only open items (for dashboard badge).
public Task<List<ActionItem>> ListOpenForUserAsync(int userId) =>
    _db.ActionItems.Where(a => a.Status == ActionItemStatus.Open && a.AssignedToUserId == userId)
        .OrderBy(a => a.CreatedAt).ToListAsync();
```
- [ ] Step1: failing tests — Resolve sets Status=Resolved + ResolvedByUserId + ResolvedAt + **DeduplicationKey null**; **THE LYNCHPIN TEST**: create a gap ticket → resolve it → call `EnsureGapActionItemAsync` for the SAME (classId,modelId) → a NEW Open ticket is created (proves the freed slot); double-resolve is a no-op (no throw, no second write); ListOpenForUserAndRoleAsync returns role-assigned + user-assigned, excludes Resolved + other users' user-assigned; ListAllOpen returns all Open. Step2 RED → Step3 implement → Step4 GREEN.
- [ ] Step5: `dotnet ef migrations add AddActionItemResolvedFields`. Confirm Up() adds the 2 nullable columns + the SetNull FK, no drops; snapshot staged; `has-pending-model-changes` clean.
- [ ] Step6: build+test green; commit (explicit paths) `feat(actionhub): ActionItem resolve flow (clears dedup key) + role/user queries + migration`.

---

## Task 2: Action Hub page (role-aware, resolve, polling)
**Files:** modify `Pages/Operations/ActionItems/Index.cshtml(.cs)`; create `Pages/Operations/ActionItems/_ActionItemList.cshtml`; test `tests/Orkabi.Web.Tests/ActionHubPageTests.cs`.
**Build to the design doc** (Action Hub section): role-aware open-items list as `.action-card`s with the `.action-type` badge + description + due date `<bdi class="num">` + a **Resolve** button (`סמן כטופל`); polling refresh; empty state; the Hebrew copy + classes exactly.
- Generalize the page: change `[Authorize(Roles = Admin)]` → `[Authorize]` (any authenticated). OnGet: parse userId (`User.FindFirstValue(ClaimTypes.NameIdentifier)`), determine effective role (Admin→`ListAllOpenAsync`; else `ListOpenForUserAndRoleAsync(userId, role)`). Remove the Slice-3 `scope-note` placeholder.
- `OnPostResolveAsync(int id)`: **authz in handler** — load item; if role-assigned: require `User.IsInRole(item.AssignedToRole)` OR Admin; if user-assigned: require `userId == item.AssignedToUserId` OR Admin; else `Forbid()`. Then `ResolveActionItemAsync(id, userId)`. Return the swap that removes the card (e.g. `Content("")` with `hx-target` the card + `hx-swap="outerHTML"`, or a brief resolved-state per the design). 
- `OnGetListAsync()`: returns `Partial("_ActionItemList", items)` — the polling fragment. Wrap the list root with `hx-get="?handler=List" hx-trigger="every 25s" hx-swap="innerHTML"` (or the design's interval/value). Keep it one cheap query.
- The `/Operations/ActionItems` topbar/subnav link becomes visible to all roles (per the design). Numerals/dates in `<bdi class="num">`.
- [ ] Step1: failing `ActionHubPageTests` — anon → 302; an Instructor sees ONLY their role/user items (not another role's); resolve via the handler removes/closes the item AND DB Status=Resolved + key null; **an Instructor cannot resolve an Admin-only/another-user's item (handler Forbid + item stays Open)** (the Slice-3/4 authz-leak test); the polling `?handler=List` fragment returns the open list; empty state renders. HtmlDecode Hebrew. Step2 RED → Step3 (per design) → Step4 GREEN.
- [ ] Step5: commit `feat(actionhub): role-aware Action Hub with resolve + polling (generalize the minimal page)`.

---

## Task 3: DashboardMetricsService + Admin bento real data
**Files:** create `Modules/Dashboard/DashboardMetricsService.cs` (register AddScoped); modify `Pages/Dashboard/Admin.cshtml(.cs)`; test `tests/Orkabi.Web.Tests/DashboardMetricsTests.cs`.
**Build to the design doc** (Admin bento section): replace EVERY placeholder value with the real metric; keep the premium bento layout; each tile links to its source surface.
- `DashboardMetricsService` (inject `AppDbContext` + `ActionItemService`): `GetAdminMetricsAsync()` returning a DTO/record with: ActiveClientsCount (`Clients.Count(IsActive)`), NewClientsThisMonth (`Clients` IsActive + CreatedAt>=firstOfMonthIsrael), OpenActionItemsByType (`ActionItems` Open GroupBy Type → counts), SessionsToday (`ShiftInstances` Date==todayIsrael), PendingVacations (`VacationRequests` Status==Pending), OpenDisputedOrders (`LogisticsOrders` Status==Disputed), ActiveClassesCount, and the top-5 recent Open items (across all roles) for the feed tile + the top-5 Open Admin items for the task tile. Use `IsraelClock` for today/month boundaries. Keep to a bounded set of cheap COUNT/aggregate queries.
- Rewrite `Admin.cshtml.cs` to load the metrics; `Admin.cshtml` to render `@Model.*` (NO hardcoded values). Map ActionItemType→feed-dot/severity per the design. Numerals in `<bdi class="num">`.
- [ ] Step1: failing `DashboardMetricsTests` — seed clients/classes/shifts/vacations/orders/action-items; assert each Admin metric equals the seeded truth (e.g. ActiveClientsCount counts only IsActive; OpenDisputedOrders counts only Disputed; SessionsToday only today-Israel). Step2 RED → Step3 → Step4 GREEN. (Page-render assertion optional: Admin GET 200 + a real number present, no placeholder string.)
- [ ] Step5: commit `feat(dashboard): DashboardMetricsService + real Admin bento metrics`.

---

## Task 4: CS + Logistics dashboards (real) + dynamic greeting
**Files:** modify `Pages/Dashboard/Cs.cshtml(.cs)`, `Pages/Dashboard/Logistics.cshtml(.cs)` (+ `Admin.cshtml.cs` greeting); extend `DashboardMetricsService` (GetCsMetricsAsync, GetLogisticsMetricsAsync); test (extend DashboardMetricsTests / add page tests).
**Build to the design doc** (CS + Logistics dashboard sections): real surfaces — each role's metrics + its Action-Hub queue preview + quick links; reuse bento/card vocabulary.
- `GetCsMetricsAsync()`: CS open tickets (`ListOpenForRoleAsync(CustomerService)`), tryout pipeline (active `Enrollment` IsTryout count, optionally by status), follow-up-needed (`Enrollment` !PaidMaterials || !PaidMonthly count). `GetLogisticsMetricsAsync()`: Logistics open tickets (`ListOpenForRoleAsync(Logistics)`), OrdersByStatus (`LogisticsOrders` GroupBy Status), PendingOrders count.
- Rewrite Cs/Logistics dashboards from `dash-stub` placeholders to real surfaces. **Dynamic greeting:** inject `UserManager<AppUser>`, resolve `me.FullName`, render `שלום, @Model.Greeting` (apply to Admin/CS/Logistics — mirror the Instructor dashboard's existing pattern).
- [ ] Step1: failing tests — CS metrics (tryout count, follow-up count) + Logistics metrics (orders-by-status, pending count) equal seeded truth; CS/Logistics GET 200 with the greeting name + real numbers (no `dash-stub`/placeholder). Step2 RED → Step3 → Step4 GREEN.
- [ ] Step5: commit `feat(dashboard): real CS + Logistics dashboards + dynamic greeting`.

---

## Task 5: Logistics packing list
**Files:** modify `Modules/Logistics/SupplyPacingService.cs` (add GetPackingListAsync); create `Pages/Logistics/PackingList/Index.cshtml(.cs)`; add the subnav item to the Logistics pages; modify `wwwroot/css/base.css` (print + any packing classes per design); test `tests/Orkabi.Web.Tests/PackingListPageTests.cs`.
**Build to the design doc** (packing list section): the consolidated master list grouped by class (school→class), models + quantities `<bdi class="num">` + status + a Pack action; print-friendly (`@media print` collapses chrome).
- `GetPackingListAsync()`: `LogisticsOrders` where Status==Pending||Packed, `IgnoreQueryFilters()`, `.Include(o=>o.Class).ThenInclude(c=>c.School).Include(o=>o.Model)`, order school→class→status. Add to SupplyPacingService (read on the same aggregate).
- Page `/Logistics/PackingList`, `[Authorize(Roles = AppRoles.LogisticsOrAdmin)]`. Group in-memory by class; the Pack action reuses the existing `MarkPackedAsync` HTMX pattern (handler-guard Logistics/Admin). Add `רשימת אריזה` to the Logistics subnav (Orders + MyOrders + PackingList).
- [ ] Step1: failing `PackingListPageTests` — anon 302; non-Logistics/non-Admin (Instructor) → 403; Logistics GET 200 lists Pending+Packed grouped by class with quantities; (optional) Pack action transitions + handler-guard. HtmlDecode Hebrew. Step2 RED → Step3 → Step4 GREEN.
- [ ] Step5: commit `feat(logistics): master packing list (grouped, print-friendly)`.

---

## Task 6: Syllabus → class assignment
**Files:** modify `Pages/People/Classes/Edit.cshtml(.cs)`, `Pages/Curriculum/Syllabi/Index.cshtml(.cs)`; test (extend the Classes / Curriculum page tests).
**Build to the design doc** (syllabus section, if present) + Slice-2 vocabulary.
- Classes/Edit: add a `SyllabusId` `<select>` (nullable; `— ללא —` option) populated from `CurriculumService.ListSyllabiAsync()`; bind into the Edit InputModel; `ClassService.UpdateAsync` already persists `Class.SyllabusId` (verify — it persists the entity). No service change expected.
- Syllabi/Index: add a `כיתות מקושרות` count column — a `ClassCounts` dict (`Classes` GroupBy SyllabusId, IgnoreQueryFilters as needed) like the existing `ModelCounts`. Numerals in `<bdi class="num">`.
- [ ] Step1: failing tests — editing a class to set SyllabusId persists it (GET shows the selected syllabus; POST updates); Syllabi/Index shows the linked-class count. Step2 RED → Step3 → Step4 GREEN.
- [ ] Step5: commit `feat(curriculum): assign syllabus to class + linked-class count on syllabi list`.

---

## Task 7: Carryover polish — toasts + shared shell (new pages) 
**Files:** modify `Pages/Shared/_Layout.cshtml` (toast container + minimal JS); create `Pages/Shared/_PageShell.cshtml` (topbar+subnav partial for NEW pages); apply to the Slice-5 new pages (PackingList, rebuilt CS/Logistics dashboards) where low-risk; modify `wwwroot/css/base.css` only if the design adds toast/shell CSS (the `.toast` base already exists). Test: extend a page test to assert the toast trigger / shell render.
**Build to the design doc** (toast + `_PageShell`/`_SectionShell` sections).
- Toast: add `<div id="toast-container" aria-live="polite">` to `_Layout` + ~15 lines of JS listening for an `HX-Trigger: showToast` response header (or a custom event) to append an auto-dismiss toast. Wire it on Resolve (`הפריט סומן כטופל`) and Pack. Reuse the existing `.toast` CSS; add only what the design specifies.
- `_PageShell.cshtml`: a partial rendering topbar (wordmark + title + `שלום, {name}` + logout) + subnav (items + active key), per the design's contract. Apply ONLY to NEW Slice-5 pages — do NOT touch the 20 live pages.
- [ ] Step1: failing test(s) — a Resolve/Pack response carries the toast trigger; a new page renders via `_PageShell` (title + active subnav). Keep ALL existing page tests green. Step2 RED → Step3 → Step4 GREEN.
- [ ] Step5: commit `feat(ui): save-success toasts + shared page-shell partial (new pages)`.
> **DEFERRED (disclosed):** migrating the 20 existing Slice-0..4 pages onto `_PageShell` is a separate low-value/medium-risk effort — NOT in this slice (the live pages stay as-is). Flag in the handoff.

---

## Task 8: Deploy + verify + final handoff
- [ ] Step1: final `dotnet build -clp:ErrorsOnly && dotnet test` green; `dotnet ef migrations has-pending-model-changes` clean.
- [ ] Step2: whole-branch review (strongest model) — triage accumulated minors (this plan's deferred shell-migration; any per-task minors) + verify the dedup-key-null lynchpin, the hub authz (no leak), the polling cost, dashboards cheap, migration additive/Neon-safe. Fix Critical/Important.
- [ ] Step3: merge to `master` (pre-authorized) → push → Render auto-deploys → boot applies `AddActionItemResolvedFields`.
- [ ] Step4: verify live: `/health` ok; `/Operations/ActionItems` → 302 for anon (hub reachable); `/Logistics/PackingList` → 302 (new route + migration booted).
- [ ] Step5: update `docs/HANDOFF.md` + memory — **Slices 0–5 ALL LIVE, project feature-complete**; record the deferred shell-migration + SignalR-upgrade. Commit.

---

## Self-Review (writing-plans checklist)
- **Spec coverage:** Action Hub resolve + polling (§3) ✔(T1,T2); Action_Item resolved fields (§4) ✔(T1); real dashboards (§5/§7) ✔(T3,T4); packing list ✔(T5); syllabus mgmt ✔(T6); polish ✔(T7).
- **Lynchpin:** dedup-key-null-on-resolve has a dedicated test (T1) ✔. **Authz:** resolve handler-guard + no-leak test (T2); packing handler-guard (T5) ✔. **No live-page refactor** (T7 new-pages-only) ✔.
- **Migration:** one (AddActionItemResolvedFields), additive, snapshot-in-sync gate ✔. Prod = DIRECT Neon endpoint; don't touch conn strings ✔.
- **Highest risk:** T1 (the dedup-key lynchpin — silent recurrence-suppression if wrong) + T2 (hub authz leak) — both get explicit tests.
- **Deferred (disclosed):** existing-page `_PageShell` migration → post-Slice-5; SignalR real-time → later phase (polling is the deliverable); manual Task-creation UI → not in scope; ResolveLessonLog "first incomplete model" refinement → later. NONE pulled forward.
- **Recommended order:** T1 (core) → T2 (hub) → T3 (admin metrics) → T4 (CS/Logistics) → T5 (packing) → T6 (syllabus) → T7 (polish) → T8 (deploy).
