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
- [ ] **R4 — Attendance history can't answer "was my child present?"** _(CS #1, the CS persona's #1 daily question)_. F13 is aggregate-only: the `classId` filter exists in the service but the page hard-codes `null`, and there's no per-student view. Wire the filter + add a per-lesson per-student drill-down.

## 🟠 Discovery / routing holes (small, high-leverage)

- [ ] **R5 — Pending substitution requests are invisible to the Admin** _(Admin #2)_. `RequestSubstitutionAsync` raises no signal (unlike F18 absence); the approvals tile lists only extra-hours + vacations. Add a dedup-keyed Admin action item on request (mirror F18), or a pending-subs count on the tile.
- [ ] **R6 — Mass-dropout action item routes to Admin, not CS** _(CS #3, **DECISION**)_. CS owns the dropout lifecycle (double-absence + tryout-followup already go to CS); mass-dropout going to Admin is inconsistent. But it's a serious oversight signal an admin may want to own. _Route to CS (Admin keeps it via `ListAllOpenAsync`) vs keep on Admin — needs a call._
- [ ] **R7 — Client-related tickets don't deep-link to the F12 profile** _(CS #5)_. Tryout-followup / double-absence set `RelatedEntityId = clientId` but the card renders text only. Link the card to `/People/Clients/Details/{RelatedEntityId}`.
- [ ] **R8 — Non-High incidents have no dashboard signal; "escalate" is a no-op** _(Admin #3)_. Only High emits an action item; a Medium incident produces zero proactive signal, and `EscalateIncidentAsync` only relabels. Add an open-incident bento count and make escalate raise/route an item.

## 🟠 Capability gaps (bigger)

- [ ] **R9 — Academic-year management can't edit/rollover + accepts duplicates** _(Admin #4, **DECISION** on rollover semantics)_. No Edit, no overlap/duplicate-label validation; `SetCurrent` doesn't regenerate classes/templates for the new year — the Sept-2026 rollover (biggest looming admin task) has no surface. Add Edit (with guards) + a documented rollover path.
- [ ] **R10 — Approved substitutes can't see/receive/dispute the kit** _(Instructor #4 / P13)_. `MyOrders` scopes to `DefaultInstructorId`; a sub set as `ActualInstructorId` sees zero orders. Include classes where the user is the actual instructor on an upcoming instance.
- [ ] **R11 — Same-day-only shift access compounds the lesson-log gap** _(Instructor #2)_. `CanAccessShiftAsync` gates to `Date == todayIsrael`; a late-night lesson or next-morning fix is `Forbid()` (Admin-only). Widen the instructor window to ±1 day Israel time.
- [ ] **R12 — Mobile: five instructor tables render as label-less squished rows** _(Instructor #3 / TD16)_. Only `thead{display:none}` today. Card-stack reflow with `data-label` under 767px — highest-leverage UX fix for a phone-first persona.

## 🟢 Polish (persona-confirmed, mostly already P-tier)

- [ ] **R13 — Greeting inconsistency** _(Admin #7 / Instructor #8)_: bento resolves `FullName`, every sub-page hard-codes the email prefix → name flickers between pages. Shared topbar/greeting helper.
- [ ] **R14 — Admin focal queue is oldest-first + birthday items never auto-close** _(Admin #5 / P2)_: stale birthday tickets bury same-day urgent items. Order by urgency + auto-close past-due birthdays.
- [ ] **R15 — Dispute ticket shows no reason / no order link** _(Logistics #2)_: `DisputeNotes` omitted from the description; no deep-link. Append notes + link.
- [ ] **R16 — "Generate Orders" while a dispute is open forks a duplicate Pending order** _(Logistics #3)_: the seed guard excludes `Disputed`. Treat a live dispute as "exists".
- [ ] **R17 — Attendance two-half tap has no legend** _(Instructor #5)_; **R18 — phone is plain text (no `tel:`/WhatsApp)** _(CS #7 / P10)_; **R19 — absence button always shows success + no per-row state** _(Instructor #6)_.

## 🧪 Test-debt (QA agent — one-sided coverage that hides regressions)

- [ ] **QA1 — `DeleteModelAsync` LessonLog-only + LogisticsOrder-only guards** (only SyllabusModel tested; incl. an archived-template LessonLog to lock `IgnoreQueryFilters`).
- [ ] **QA2 — `ReportAbsenceAsync` own-past-shift (throws) + own-today-shift (succeeds)** boundary.
- [ ] **QA3 — Re-dispute recurrence**: repack → pack → dispute again creates a fresh ticket.
- [ ] **QA4 — Schools delete page handler** (success + in-use re-render) + archived-class block.
- [ ] **QA5 — `ResolveCurrentModelForClassAsync` `(null,null)`** for no-syllabus + empty-syllabus.
- [ ] **QA6** — `RepackDisputed` page forbidden-role; **QA7** — `ListByClientAsync` archived-class; **QA8** — `ListLessonHistoryAsync` archived + null-classId; **QA9** — `CloseIncidentAsync` from Escalated; **QA10** — `ToggleAsync` tryout-on-Dropped.

---

## Decisions needed from the product owner
1. **R6 — mass-dropout routing:** CS-owned vs Admin-owned (or dual)?
2. **R9 — academic-year rollover:** what should "set current" do — flag only (today), or regenerate classes/templates for the new year?
3. **TD2 — self-registration:** keep `/Account/Register` open, or admin-only invite?
