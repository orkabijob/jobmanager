# Post-deploy review findings (5-persona + QA audit)

_Run after the Functional tier (B1–B3, F1–F20) shipped to production (commit `903d0da`).
Four role-persona agents + one QA agent audited the codebase against each role's real
workflows. This is the prioritized residue — what the freshly-shipped tier still gets wrong._

Suite at capture: **392/392**. Legend: `[x]` fixed this session · `[ ]` open.

---

## 🔴 Correctness bugs in shipped features (a feature is secretly non-functional)

- [x] **R1 — Lesson-log loop was dead → curriculum progression frozen** _(Instructor #1 / P3)_. Nothing linked to `/Attendance/{id}/Log` (the only place a lesson is marked `Completed`); attendance-save redirects to the dashboard. F20's resolver counts only `Completed` logs → "current model" + pacing froze on model #1 forever. **Fixed:** added a "יומן שיעור וסיום מודל" link on the attendance sheet (`2977e88`). _Further: optionally redirect to the Log page after attendance save._
- [x] **R2 — Resolving a dispute ticket stranded the kit** _(Logistics #1)_. Hub "סמן כטופל" did a generic resolve without re-packing → order stuck `Disputed` forever. **Fixed:** dispute-resolve now routes through `RepackDisputedAsync` (`e668254`).
- [x] **R3 — Disabling a user didn't cut their session** _(Admin #1, security)_. `SetEnabledAsync` set `LockoutEnd` but not the security stamp → 7-day window. **Fixed:** rotates the security stamp on disable (`2977e88`).
- [x] **R4 — Attendance history can't answer "was my child present?"** _(CS #1)_. **Fixed:** `/Attendance/History` gains a class filter + each row drills into `/Attendance/Lesson/{id}` (per-student present/absent). `5708373`.

## 🟠 Discovery / routing holes (small, high-leverage)

- [x] **R5 — Pending substitution requests invisible to the Admin** _(Admin #2)_. **Fixed:** `RequestSubstitutionAsync` now raises a dedup-keyed Admin action item (`sub_request_{id}`), resolved on approve/deny/cancel — the pending queue is now discoverable in the Admin hub.
- [x] **R6 — Mass-dropout routing** _(CS #3)_. **Decided: both.** Admin keeps its oversight item; a new CS item (`dropout_mass_cs_{classId}`) gives CS the follow-up. `79c88d2`.
- [x] **R7 — Client tickets deep-link to the F12 profile** _(CS #5)_. **Fixed:** Birthday + TryoutFollowup cards (RelatedEntityId is a clientId) now show a "פרופיל תלמיד" link to `/People/Clients/Details/{id}`, gated to CS/Admin viewers (no 403 dead-end for others).
- [x] **R8 — incident signal** _(Admin #3)_. **Fixed (both halves):** `EscalateIncidentAsync` raises a dedup-keyed Admin item (resolved on close), AND the Admin bento now shows an **open-incident count** (`AdminMetrics.OpenIncidents`, Open+Escalated) linking to `/Operations/Incidents` — so a Medium incident (no action item) has a passive signal too.

## 🟠 Capability gaps (bigger)

- [~] **R9 — Academic-year rollover** _(Admin #4)_. **Rollover built:** `AcademicYearService.RollOverAsync(from,to)` clones Active classes + their Active shift-templates (carries SyllabusId; skips enrollments/instances/logs/orders); idempotent (partial-index-aware Active-name skip); transactional. Explicit from→into form on `/Admin/AcademicYears` (chosen over auto-on-set-current for accidental-double-run safety — **flagged for veto**). No migration. **Still open:** year **Edit** page + overlap/duplicate-label validation on Create (the other half of Admin #4).
- [x] **R10 — Approved substitutes see the kit** _(Instructor #4 / P13)_. **Fixed:** `MyOrders` (list + Accept/Dispute authz via the consolidated `GetInstructorClassIdsAsync`) now includes classes where the user is the `ActualInstructor` on a shift instance, not just the template default — the sub who actually teaches can receive/dispute the package.
- [x] **R11 — Same-day-only shift access** _(Instructor #2)_. **Fixed:** `CanAccessShiftAsync` now allows a backward grace window (yesterday + today, Israel time) so a late-night lesson / next-morning correction / UTC-boundary slip isn't locked out. Future shifts stay forbidden (no early marking).
- [ ] **R12 — Mobile: five instructor tables render as label-less squished rows** _(Instructor #3 / TD16)_. Only `thead{display:none}` today. Card-stack reflow with `data-label` under 767px — highest-leverage UX fix for a phone-first persona.

## 🟢 Polish (persona-confirmed, mostly already P-tier)

- [ ] **R13 — Greeting inconsistency** _(Admin #7 / Instructor #8)_: bento resolves `FullName`, every sub-page hard-codes the email prefix → name flickers between pages. Shared topbar/greeting helper.
- [x] **R14 — Birthday items auto-close** _(Admin #5 / P2)_. **Fixed:** the daily job now calls `AutoResolveStaleBirthdayItemsAsync` to resolve past-due (DueDate<today) Birthday items before creating today's, so stale ones stop accumulating at the front of the focal queue. (Focal-queue re-ordering by urgency left as a further tweak — with the clutter gone at source it's lower-value.)
- [x] **R15 — Dispute ticket now shows the reason** _(Logistics #2)_: the instructor's `DisputeNotes` (truncated) is appended to the dispute ticket description, so Logistics can triage from the hub. (The separate order deep-link is moot now that hub-resolve re-packs directly — Logistics #1.)
- [x] **R16 — Generate no longer duplicates a disputed order** _(Logistics #3)_: `SeedOrdersForClassAsync` now treats ANY existing order (incl. Disputed) as "exists"; a live dispute is re-queued via re-pack, not by forking a second Pending order.
- [ ] **R17 — Attendance two-half tap has no legend** _(Instructor #5)_; **R18 — phone is plain text (no `tel:`/WhatsApp)** _(CS #7 / P10)_; **R19 — absence button always shows success + no per-row state** _(Instructor #6)_.

## 🧪 Test-debt (QA agent — one-sided coverage that hides regressions)

- [ ] **QA1 — `DeleteModelAsync` LessonLog-only + LogisticsOrder-only guards** (only SyllabusModel tested; incl. an archived-template LessonLog to lock `IgnoreQueryFilters`).
- [x] **QA2 — `ReportAbsenceAsync` own-past-shift (throws) + own-today-shift (succeeds)** boundary. Added.
- [ ] **QA3 — Re-dispute recurrence**: repack → pack → dispute again creates a fresh ticket.
- [ ] **QA4 — Schools delete page handler** (success + in-use re-render) + archived-class block.
- [x] **QA5 — `ResolveCurrentModelForClassAsync` `(null,null)`** for no-syllabus. Added (empty-syllabus case still uncovered).
- [ ] **QA6** — `RepackDisputed` page forbidden-role; **QA7** — `ListByClientAsync` archived-class; **QA8** — `ListLessonHistoryAsync` archived + null-classId; **QA9** — `CloseIncidentAsync` from Escalated; **QA10** — `ToggleAsync` tryout-on-Dropped.

---

## Decisions (resolved by the product owner)
1. **R6 — mass-dropout routing:** ✅ **Both** (CS follow-up + Admin oversight) — built (`79c88d2`).
2. **R9 — academic-year rollover:** ✅ **Full rollover** — "set current" should regenerate classes/shift-templates for the new year. _Not yet built — a larger feature; the rollover engine needs design (an architect pass) before implementation._
3. **TD2 — self-registration:** ✅ **Keep open** — no change; `/Account/Register` stays anonymous. Resolved (won't-change).
