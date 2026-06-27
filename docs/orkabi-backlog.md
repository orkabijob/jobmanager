# Orkabi — Master Backlog & Checklist

_Synthesized 2026-06-27 from: the 5 persona gap reports, the codebase gap/authz review, the
docs+code deferred-items sweep, and `docs/HANDOFF.md`. Deduplicated; nothing dropped._
_Companion: `docs/personas-and-gaps.md` (persona detail). Tests are at **350/350** as of this writing._

> ⚠️ **Pending production migration (needs deploy sign-off):** `20260627160841_AddIncidentReportStatus` (F2) adds a non-null `int Status` column (default 0 = Open) to `IncidentReports`. Applied automatically by `MigrateAsync` at boot on the next `master` deploy; non-breaking (existing rows → Open). No other pending migrations.

**Legend:** `[ ]` open · `[x]` done · **✓v** = hand-verified against source · _src_ tags:
A=Admin persona, C=CS persona, L=Logistics persona, I=Instructor persona, G=gap-reviewer,
D=deferred-auditor, H=HANDOFF.

ID scheme: **B**=blocking · **F**=functional/important · **P**=polish/nice-to-have · **TD**=tech-debt/deferred.

---

## ✅ Done this session
- [x] **B1 — Admin user & role management** — `/Admin/Users` (list, create user, assign/revoke roles, enable/disable via Identity lockout, reset password; Admin-only; last-admin guard). _src A/G_
- [x] **Help center** — `/Help` (roles, user-management explainer, per-area cards, FAQ), reachable from every dashboard + AccessDenied.

---

## 🟥 Blocking — a core flow is non-functional

- [x] **B2 — Instructor substitution-request page.** ✅ `Pages/Scheduling/Substitutions/Create` `[InstructorOrAdmin]`: pick one of *my* future scheduled shifts + a proposed substitute → pending request; "my pending requests" list with per-row cancel. Handler enforces ownership / future-only / valid-different-instructor / no-duplicate-pending (the thin `RequestSubstitutionAsync` has no authz). Reachable via a "בקש החלפה" link on the instructor dashboard. 16 new tests (page authz + functional + service cancel coverage). _src I/G (SchedulingService.cs:221–276; Substitutions/Index only)_
- [x] **B3 — Academic-year management UI.** ✅ `Pages/Admin/AcademicYears` `[Authorize(Roles=Admin)]`: list (newest first), create (non-current; handler rejects end ≤ start), and set-current (transactional clear-before-set via existing `SetCurrentAsync`; respects the single-current partial index). Added `AcademicYearService.CreateAsync`. Reachable via a "שנות לימוד" link on the Admin dashboard. 11 new tests. _Scope note: Edit/Delete of years deliberately out of B3 scope (year deletion is FK-guarded work like F16)._ _src A/G (DailyJobService.cs:37)_

---

## 🟧 Functional / important — capability or correctness hole

- [x] **F1 — Extra-hours denial.** ✅ Added `ExtraHoursStatus.Denied` (int-mapped, no migration), `OperationsService.DenyExtraHoursAsync`, `OnPostDenyAsync` handler (Admin-only, like approve), and a "דחייה" button + "נדחה" chip on the row — mirroring the Vacations approve/deny. _Caught in review: the new enum value exposed a latent binary `Pending?else→Approved` render in the instructor's own-submissions list that would have shown a denied report as "אושר"; fixed to 3-state + test-locked._ No `AdminNote`/reason column added (would need a migration; Vacations passes null anyway). 5 new tests. _src A/G_
- [x] **F2 — Incident-report lifecycle.** ✅ **Both:** (A) a **High**-severity incident appends an `IncidentSevere` outbox event (same tx) → drainer → Admin action item (`EnsureSevereIncidentActionItemAsync`, dedup `severe_incident_{id}`); (B) `IncidentStatus` (Open/Closed/Escalated) on `IncidentReport` + Admin `OnPostClose`/`OnPostEscalate` (Admin-only) + status chips in both incident tables. Closing an incident resolves its severe action item (closes the loop). **⚠️ Migration `AddIncidentReportStatus` — runs on Neon at deploy (sign-off).** `OperationsService` now takes `ActionItemService`. _src G/D-O1_
- [x] **F3 — Logistics dispute response path.** ✅ `SupplyPacingService.RepackDisputedAsync` (Disputed→Pending, clears notes, resolves the dispute ticket — closes the loop) + `OnPostRepackAsync` (Logistics/Admin) + a "החזר לאריזה" button on disputed rows. Re-dispute recurrence holds (resolve nulls the dedup key). _src L/G_
- [x] **F4 — Dispute ticket assigned to Admin, invisible to Logistics hub.** ✅ `EnsureDisputeActionItemAsync` now assigns `AssignedToRole = Logistics` → shows in the Logistics dashboard queue + the Logistics Action Hub (and Logistics can resolve). Admin retains full visibility (the Admin hub uses `ListAllOpenAsync`; the bento by-type count + alerts feed still include disputes — only the Admin's *personal* focal queue excludes them, by design, locked with a test). _src L_
- [x] **F5 — Authz holes on Operations.** ✅ `/Operations` hub + `/Operations/Incidents` → `[Authorize(Roles = CsOrInstructorOrAdmin)]` (new constant; excludes Logistics, keeps CS for incident *reads*). `/Operations/ActionItems` stays all-roles. Logistics is now excluded from the hub/incidents but still reaches the Action Hub. _src C/G_
- [x] **F6 — Dead-end subnav links for CS.** ✅ Extracted a shared, role-gated `_OperationsSubnav` partial (the 5 inline copies' drift *was* F8) + gated the hub nav-cards; gated the Scheduling "החלפות" link+card to Admin across **all 6** Scheduling pages (Index, Templates Index/Create/Edit, Instances, Substitutions). CS sees zero 403 links. _src C_
- [x] **F7 — Incident submit form unusable for CS.** ✅ Form is now instructor-only in the view **and** the `OnPostAsync` handler guards `!IsInRole(Instructor) → Forbid()` (view/handler symmetry — caught in review). CS/Admin still see the incident *lists*. _src C_
- [x] **F8 — Operations subnav hides ActionItems from instructors.** ✅ Folded into the `_OperationsSubnav` partial — "משימות פתוחות" now shows for every role (incl. CS), and a Logistics user on the Action Hub sees *only* that link (no new dead-ends). _src I_
- [x] **F9 — Admin locked out of `/Dashboard/Logistics`.** ✅ `[Authorize(Roles = LogisticsOrAdmin)]`. _src A_

> _Deferred (latent, not live): `Pages/Shared/PageShellVm.cs` `SubnavFor(Operations/Scheduling)` still lists ExtraHours/Vacations/"החלפות" with `Roles=null` (everyone). No page renders those sections via `_PageShell` today (only Logistics does), so it's not a live dead-end — but the same role allow-lists must be added there before **TD13** migrates Operations/Scheduling pages onto `_PageShell`, or F6 silently regresses._
- [ ] **F10 — Self-service password reset + profile page.** No forgot-password flow, no `/Account/Profile`; `FullName` never set at registration → greetings fall back to email. Add `ForgotPassword`/`ResetPassword` + `/Account/Profile` (name + change-password). _src I/G (Register.cshtml.cs:25; AppUser.cs:7)_
- [x] **F11 — Instructor cancel own pending vacation.** ✅ `OperationsService.CancelVacationAsync` (guards ownership + Pending → `Cancelled`) + `OnPostCancelAsync` + a "ביטול" button on pending rows. Added `VacationStatus.Cancelled` (int enum, no migration) and the "בוטל" render in the instructor list (blast-radius-checked: counts filter Pending; `_VacationRow` only renders the admin pending list). 5 new tests. _src G_
- [ ] **F12 — Client profile / enrollment overview.** No `/People/Clients/{id}`; CS can't answer "what class is my kid in?" without opening every roster. Add a read-only detail page (enrollments, payment flags, tryout). _src C_
- [ ] **F13 — Attendance history view.** Attendance is Instructor/Admin + today-only; CS has zero visibility. Add `/Attendance/History` `[CsOrAdmin]` (LessonLog summaries → per-student rows). _src C/I/G_
- [ ] **F14 — Substitution-approval notifications.** `ApproveSubstitutionAsync` silently swaps `ActualInstructorId`; affected instructors aren't told. Create user-assigned action items for sub + original on approve. _src G/I (SchedulingService.cs:236–252)_
- [ ] **F15 — `EnrollmentStatus.Completed` is unreachable.** No `CompleteAsync`, no UI transition. Define semantics (manual graduate vs. auto on syllabus completion) + add the transition. _src G (EnrollmentService.cs)_
- [ ] **F16 — No delete for Curriculum Models / Schools.** Create+Update only; bad rows accumulate. Add FK-guarded `DeleteModelAsync`/`DeleteSchoolAsync` + edit-page buttons. _src G (CurriculumService.cs:12–31; SchoolService.cs:11–33)_
- [ ] **F17 — Instructor week/month schedule view.** Dashboard shows today only; `/Scheduling/Instances` is CS/Admin-gated. Add a read-only "my schedule" tab (7/30-day) filtered to the user. _src I_
- [ ] **F18 — Instructor proactive absence report.** No "I can't make it" path (vacation needs future range; incident needs an existing shift). Add "הודע על היעדרות" on the shift card → action item + optional sub-request. _src I_
- [x] **F19 — Instructor dashboard nav to Operations.** ✅ A "פעולות מהירות" quick-links card-grid under the shift cards: בקשת החלפה, שעות נוספות, חופשות, דיווח אירוע, ההזמנות שלי, משימות פתוחות. _src I_
- [x] **F20 — "First incomplete model" resolver.** ✅ New `SchedulingService.ResolveCurrentModelForClassAsync` picks the first syllabus model (by OrderIndex) whose count of **Completed** LessonLogs for the class is still below its `ExpectedLessonsToComplete` (last model if all complete). Both callers now route through it — `ResolveLessonLogForAttendanceAsync` (attendance) and the dashboard "דגם:" chip (the now-unused `CurriculumService` dep was dropped). Progression no longer frozen at model #1. **✓v** _src I/G/D-S1_

---

## 🟩 Polish / nice-to-have

- [ ] **P1 — Pagination** on Clients/Schools/Classes/Incidents/ActionItems/ExtraHours lists (Action Items grow unbounded). _src G_
- [ ] **P2 — Birthday action items auto-close.** `DueDate` set but never used; stale items pile up. Nightly auto-resolve `Birthday` items past due, or an "overdue" badge. _src G (ActionItemService.cs:296,351)_
- [ ] **P3 — Attendance→Log link.** `/Attendance/{id}/Log` exists but isn't linked from the attendance sheet. Add a "פתח יומן שיעור" button. _src I_
- [ ] **P4 — Data export / reporting.** CSV on list pages + attendance history + extra-hours payroll summary; optional `/Reports` hub. _src A_
- [ ] **P5 — Audit-log UI.** Surface existing `Created/UpdatedBy*` columns in tables + optional `/Admin/AuditLog` feed. _src A (BaseEntity.cs)_
- [ ] **P6 — Today's-sessions date filter** on `/Scheduling/Instances` (default to today). _src C_
- [ ] **P7 — Class-coverage flags.** "חסר סילבוס" column on `/People/Classes`; instructor-empty flag on `/Scheduling/Templates`. _src C_
- [ ] **P8 — Client class-transfer shortcut** (one-step move between classes vs. drop+add). _src C_
- [ ] **P9 — Cross-class payment report** ("who hasn't paid materials/monthly?") — filter on Clients or `/People/Clients/Unpaid`. _src C_
- [ ] **P10 — Parent communication** (call-log / WhatsApp deep-link on the future client profile). _src C_
- [ ] **P11 — Instructor read-only syllabus view** (Curriculum is CS/Admin-gated). _src I_
- [ ] **P12 — Instructor standalone "my roster"** (roster only reachable inside an open attendance page today). _src I_
- [ ] **P13 — MyOrders visibility for substitutes.** Filtered by `DefaultInstructorId` only; an approved substitute can't see the class's package. Include approved subs. _src I (MyOrders/Index.cshtml.cs:115)_
- [ ] **P14 — Logistics: next-session/urgency column** on the packing list (join `ShiftInstance.Date`). _src L (SupplyPacingService.cs:143)_
- [ ] **P15 — Logistics: per-class upcoming material requirements** view (forward visibility before lessons are logged). _src L_
- [ ] **P16 — Logistics: inventory/stock tracking** (`StockLevel` per Model: on-hand + reorder threshold). _src L_
- [ ] **P17 — Logistics: `PackedAt`/packer-identity audit** (`MarkPackedAsync` discards `logisticsUserId`). _src L (SupplyPacingService.cs:86)_
- [ ] **P18 — Logistics: bulk-pack** (multi-select / "mark all packed"). _src L_
- [ ] **P19 — Logistics: shipment tracking** (carrier, tracking #, in-transit). _src L_
- [ ] **P20 — Logistics: reorder/procurement** (supplier + purchase-order flow). _src L_
- [ ] **P21 — Disputed re-order discoverability.** After resolving a dispute, the "Generate Orders" re-create step is silent. Add "Resolve & Re-order" or in-UI guidance. _src G_

---

## 🛠 Tech-debt / deferred (from HANDOFF + docs/code sweep)

- [ ] **TD1 — Hebrew `IdentityErrorDescriber`** — Identity errors render in English (now user-facing on `/Admin/Users` create/edit). _D-A1/H_
- [ ] **TD2 — Gate open self-registration** — `/Account/Register` is anonymous; decide admin-only invite vs. keep. _D-A2/H_
- [ ] **TD3 — Wire Google OAuth** — needs client + `Authentication__Google__*` in Render + redirect URI. _D-A3/H_
- [ ] **TD4 — Multi-role hub scoping** — a user with two non-Admin roles sees only the first role's hub items. _D-O2/H_
- [ ] **TD5 — Israel-date vs UTC-date window mismatch** — `ShiftInstanceGenerator` (Israel) vs Scheduling/Instances filter (UTC). _D-S2/H_
- [ ] **TD6 — `TimeOnly` stored as text** — EF9-SQLite parity; only matters if time-range SQL is needed. _D-S3/H_
- [ ] **TD7 — Drag-to-reorder syllabus models** (up/down buttons ship today). _D-S4_
- [ ] **TD8 — Shift-instances calendar/grid view** (grouped list ships today). _D-S5_
- [ ] **TD9 — Stale-gap auto-resolve** — open Gap action item isn't auto-cleared when the model is later Completed. _D-S7_
- [ ] **TD10 — `DeactivateAsync` hook bypass risk** — a future `IsActive=false` via `UpdateAsync` would skip the mass-dropout hook (no such caller today). _D-L1 (ClientService.cs:68)_
- [ ] **TD11 — Dashboard live-refresh** — CS/Logistics/Instructor dashboards don't poll (one `hx-trigger` add). _D-D1_
- [ ] **TD12 — Metric count-up animation** on the Admin bento. _D-D2_
- [ ] **TD13 — `_PageShell` migration** — 20 Slice-0..4 pages still inline topbar+subnav. _D-U1/H_
- [ ] **TD14 — People-pages static "שלום" greeting** + repeated shell (no `_PeopleShell`). _D-U2/H_
- [ ] **TD15 — Save-success toasts** on People/Curriculum/Scheduling CRUD. _D-U3_
- [ ] **TD16 — Full mobile table reflow** (card-stack; only a `thead{display:none}` stub today). _D-U4_
- [ ] **TD17 — Bulk/multi-select enroll** on the class roster. _D-U5_
- [ ] **TD18 — Test: replace indirect assertions** with targeted outcome checks. _D-T1/H_
- [ ] **TD19 — Test: naive-UTC month-boundary** in one test (service is Israel-TZ-correct). _D-T2/H_
- [ ] **TD20 — Test: Class-name partial-index enforcement.** _D-T3/H_
- [ ] **TD21 — Test: AuditInterceptor update-path.** _D-T4/H_
- [ ] **TD22 — Test: `RoleRoutingTests` use `LocalPath`** not full-string compare. _D-T5/H_
- [ ] **TD23 — Free-tier keep-warm** — UptimeRobot `/health` ping or ~$5 always-on host. _D-I1/H_
- [ ] **TD24 — Nightly `pg_dump` backups** to object storage (spec §8; verify if built). _D-I2_
- [ ] **TD25 — ICU `he-IL` collation** at DB provisioning (spec §8; verify at DB level). _D-I3_
- [ ] **TD26 — Full-text search** (ILIKE baseline today; phased). _D-I4_
- [ ] **TD27 — SELECT-FOR-UPDATE outbox drain** (dedup index is the current race guard). _D-I5_
- [ ] **TD28 — Dedicated/queryable audit table** (base audit fields live; table phased). _D-I6_
- [ ] **TD29 — SignalR real-time Action Hub** (25s poll today; spec Phase 2). _D-X1/H_

### Noted, not action items
- **Attendance offline queue / service-worker** — _deliberately out of scope_ (resilience = idempotency key + DOM-retained marks). _D-S6_

---

## Suggested build order
1. **B2, B3** (blocking; service layer already exists — page-only). Then **F1** (mirrors Vacations).
2. **Cheap correctness/authz wins:** F5, F6, F7, F8, F9 (mostly attribute/markup edits).
3. **F2, F3, F4** (close the incident + dispute loops).
4. **F10–F20** (profile/password, cancels, history, deletes, instructor schedule/absence, model resolver).
5. **P/TD tiers** as capacity allows; knock out the test-debt (TD18–22) alongside related features.

> **Deploy gate:** pushing to `master` triggers a production deploy on Render and requires explicit
> user sign-off. Keep the suite green (currently 275) and match the Liquid-Glass RTL design system.
