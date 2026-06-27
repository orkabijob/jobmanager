# Orkabi — User Personas & Gap Map

_Produced 2026-06-27. Method: six parallel review agents (one persona per role + a codebase
gap/authz reviewer + a docs/code deferred-items sweep), each grounded in the real route +
`[Authorize]` map and verified against source. The highest-impact "blocking" findings were
re-verified by hand (noted **✓ verified**)._

**Guiding lens for every persona:** _"What do I want to be able to do, and where do I do it?"_ —
any job whose answer is **nowhere** is a gap.

> **Already closed this session:** the #1 gap every persona hit — **no way to add users or
> assign roles** — is now fixed by **`/Admin/Users`** (create user, assign/revoke roles,
> enable/disable, reset password; Admin-only; last-admin guard). See `docs/HANDOFF.md`.

---

## Roles at a glance

| Role | Lands on | Owns |
|---|---|---|
| **מנהל / Admin** | `/Dashboard/Admin` | Everything; approvals; (now) user management |
| **שירות לקוחות / CS** | `/Dashboard/Cs` | Schools, classes, clients, enrollments, curriculum, scheduling setup |
| **לוגיסטיקה / Logistics** | `/Dashboard/Logistics` | Material orders, packing list, disputes |
| **מדריך / Instructor** | `/Dashboard/Instructor` | Today's shifts, attendance, lesson logs, own requests |
| **(no role) / new user** | `/Account/AccessDenied` | Nothing until an Admin assigns a role |

---

## Persona 1 — מנהל / Admin

**The business owner/operator.** Lands on the bento, scans for fires (disputes, approvals, open
action items), drills into Operations/Logistics to unblock staff, and keeps the org running —
above all, making sure new hires can actually log in.

**Can do (✅):** real-time dashboard; approve extra-hours; approve/deny vacations; approve/reject
substitutions; view & resolve all action items; view all incidents; full People / Curriculum /
Scheduling / Logistics management; take attendance for any class; **(new)** manage users & roles.

**Gaps (what I want, but there's no "where"):**
1. ~~Onboard a staff member / assign a role / disable / reset password~~ — **✅ now at `/Admin/Users`.**
2. **Deny an extra-hours request** — approve-only; no `DenyExtraHoursAsync`, no reject handler (Vacations has both). **✓ verified**
3. **Set/change the current academic year** — `AcademicYearService.SetCurrentAsync` exists but has no page; year rollover (Sept 2026) needs DB surgery, and the daily birthday job keys off `IsCurrent`. **✓ verified (no `AcademicYears` pages)**
4. **Export data / reports** — no CSV, no attendance history, no extra-hours payroll summary.
5. **Audit trail UI** — `BaseEntity` records `CreatedBy/UpdatedBy` on every row, but nothing surfaces it.
6. **Step into the Logistics dashboard** — `/Dashboard/Logistics` is `[Authorize(Roles=Logistics)]`, hard-excluding Admin (inconsistent with the `LogisticsOrAdmin` Logistics pages). One-line fix.

---

## Persona 2 — שירות לקוחות / Customer Service

**The front office.** Owns the people-data lifecycle (schools → classes → clients → enrollments →
tryouts → payments) and the curriculum catalogue, and fields every parent question.

**Can do (✅):** dashboard + CS action-item tray; client search/CRUD + deactivate; school/class
CRUD + archive; roster builder (enroll/drop/re-enroll/tryout/payment pills); syllabus→class
assignment; curriculum models/syllabi; shift templates + generate instances; tryout-followup &
double-absence alerts; read all incidents.

**Gaps:**
1. **Client profile / "what class is my kid in?"** — no `/People/Clients/{id}` detail; CS must open each class roster to find a student's enrollments. _(Most common inbound question.)_
2. **Dead-end nav links that 403 CS** — the Operations subnav shows "שעות נוספות"/"חופשות" and every Scheduling sub-page shows "החלפות", but all three are role-gated away from CS → silent Forbidden.
3. **Incident form rendered but unusable for CS** — the page is `[Authorize]` (no role), so CS sees the submit form, but `RecentShifts` is always empty → submit fails validation. Should be hidden for CS.
4. **Cross-class "who hasn't paid" report** — payment flags live only per-enrollment on the roster.
5. **"Today's sessions" quick view** — `/Scheduling/Instances` is a flat list, no date filter.
6. **Attendance history** — Attendance is Instructor/Admin only; CS can't answer "was my child marked present last week?"
7. **Class-coverage overview** — no flag for classes missing a syllabus or a template instructor.
8. **Parent communication** — phones are visible, but no compose/log/send anywhere (future slice).

---

## Persona 3 — לוגיסטיקה / Logistics

**The supply-chain executor.** Receives the signal that a class needs a kit, packs it, ships it,
and owns the loop when an instructor disputes a package.

**Can do (✅):** dashboard counts; orders list + status/class filters; mark Packed (orders & packing
sheet); print master packing list; generate orders from lesson logs.

**Gaps:**
1. **No dispute response path** — `_OrderRow` renders "—" for any non-Pending status; `SupplyPacingService` has no transition out of `Disputed`. Disputes stall until Admin intervenes. **✓ verified (no re-pack method/handler)**
2. **Dispute tickets invisible to the Logistics hub** — `EnsureDisputeActionItemAsync` assigns to **Admin**, but the Logistics dashboard lists `ListOpenForRoleAsync(Logistics)` → Logistics must poll the orders list manually.
3. **No urgency/date context on packing** — the packing list has no "class's next session" column; can't prioritize by who has a session tomorrow.
4. **No forward material requirements** — orders are seeded reactively (after a lesson is logged); curriculum is CS/Admin-gated, so Logistics can't pre-stage.
5. **No inventory / stock tracking** — no stock entity at all.
6. **No `PackedAt` / packer-identity audit** — `MarkPackedAsync` accepts `logisticsUserId` but discards it; no dispatch/tracking fields.
7. **No bulk-pack** — every order is an individual click.

---

## Persona 4 — מדריך / Instructor

**The field user, usually on a phone.** Shows up to a shift, takes attendance, logs the lesson,
receives the class's materials, and occasionally needs a sub / extra hours / vacation / to report
an incident.

**Can do (✅):** today's shifts (mobile-first); tap-to-mark attendance (idempotent); receive/dispute
material package; report incident; submit extra-hours; request vacation; see own request statuses.

**Gaps:**
1. **Request a substitute** — `/Scheduling/Substitutions` is **Admin-only**; the entity + `RequestSubstitutionAsync` exist but there is **no instructor create UI**, so the Admin approval queue is always empty. The most urgent field use-case. **✓ verified (no `Create` page, zero UI callers)**
2. **See my week / month schedule** — the dashboard shows **today only**; `/Scheduling/Instances` is CS/Admin-gated.
3. **Proactively report "I can't make it"** — no absence form (vacation needs future date-range; incident needs an existing shift).
4. **Navigate from my dashboard to my actions** — the instructor dashboard has no links to ExtraHours/Vacations/Incidents/MyOrders; they're URL-only.
5. **Profile / change password** — no `/Account/Profile`, no self-service reset; `FullName` is never set so the greeting falls back to the email.
6. **See substitutions affecting me** — if an Admin assigns a sub to my shift (or me as sub), I get no notification.
7. **"Current model" is wrong** — the dashboard chip always shows syllabus model #1, not the next-incomplete one (known refinement). **✓ verified (`OrderBy(OrderIndex).First`)**
8. **Lesson-log discoverability** — `/Attendance/{id}/Log` exists but isn't linked from the attendance sheet.
9. Operations subnav hides "משימות פתוחות" from non-Admins, though the page serves them.

---

## Persona 5 — (no role) / new user

**Just registered, no role.** Wants to start working; lands on `/Account/AccessDenied`.

- **Before this session:** stuck forever — no in-app actor could grant a role.
- **Now:** an Admin adds them (or assigns a role to their self-registered account) at **`/Admin/Users`**; their next login routes them to the right dashboard. The Help page and AccessDenied page point them to "ask an admin."
- **Still open:** self-service password reset; and `/Account/Register` remains open to anonymous sign-up (a product decision — gate it or keep it).

---

## Consolidated gap backlog (deduplicated, ranked)

### 🟥 Blocking — a core flow is non-functional
| # | Gap | Where it should live | Status |
|---|---|---|---|
| B1 | Add users / assign roles / disable / reset password | `/Admin/Users` | **✅ done this session** |
| B2 | Instructors cannot **create** substitution requests (service exists, no UI) | `/Scheduling/Substitutions/Create` `[InstructorOrAdmin]` | open ✓verified |
| B3 | No **academic-year** management; rollover needs DB surgery; birthday job depends on `IsCurrent` | `/Admin/AcademicYears` | open ✓verified |

### 🟧 Important — capability or correctness hole
| # | Gap | Suggested home |
|---|---|---|
| I1 | Extra-hours **approve-only** (no deny) | `DenyExtraHoursAsync` + `OnPostDeny` (mirror Vacations) ✓verified |
| I2 | Incident reports have **no lifecycle / resolution / action item** | `Status` enum + Admin close/escalate, or action item on severe |
| I3 | Logistics **dispute loop** broken: ticket → Admin not Logistics; no re-pack; `Disputed` dead-ends | reassign ticket to Logistics + `RepackDisputedAsync` |
| I4 | **Authz dead-ends**: `/Operations` hub & `/Operations/Incidents` are role-free (Logistics can reach); CS sees subnav links that 403; instructor subnav hides ActionItems | tighten `[Authorize]`, role-gate subnav links |
| I5 | Admin can't open `/Dashboard/Logistics` (`Roles=Logistics`) | change to `LogisticsOrAdmin` (1 line) |
| I6 | No **self-service password reset** / no profile page; `FullName` never set | `/Account/Profile` + forgot-password flow |
| I7 | Instructor can't **cancel** their own pending vacation | `CancelVacationAsync` + handler |
| I8 | No **client profile / enrollment overview** for CS | `/People/Clients/{id}` |
| I9 | No **attendance history** view (CS has none) | `/Attendance/History` `[CsOrAdmin]` |
| I10 | Substitution approval **notifies nobody** affected | user-assigned action items on approve |
| I11 | `EnrollmentStatus.Completed` unreachable; no **delete** for Models/Schools | add transitions + FK-guarded deletes |
| I12 | Instructor dashboard: no nav to Operations, no week/month view, no proactive-absence | quick-links strip + "my schedule" tab |
| I13 | "First incomplete model" resolver shows model #1 always | join LessonLog completion ✓verified |

### 🟩 Nice-to-have — polish, scale, ops
Pagination on growing lists · birthday action items never auto-close · Attendance→Log link ·
data export/reporting + audit-log UI · inventory/stock, bulk-pack, `PackedAt`, shipment tracking ·
cross-class payment report · client class-transfer shortcut · class-coverage (missing
syllabus/instructor) flags · parent communication · Hebrew `IdentityErrorDescriber` · gate
self-registration · wire Google OAuth · `_PageShell` migration (20 pages) · static "שלום" on People
pages · save-success toasts on People/Curriculum/Scheduling · full mobile table reflow ·
date-window (Israel vs UTC) alignment · `TimeOnly`-as-text · drag-to-reorder syllabus · calendar
grid · SignalR push for Action Hub · dashboard live-refresh · metric count-up · nightly `pg_dump`
backups · ICU `he-IL` collation · SELECT-FOR-UPDATE drain · full-text search · test-quality items
(indirect asserts, UTC month boundary, partial-index test, audit-interceptor test, `RoleRoutingTests`
LocalPath).

---

## Deferred-items inventory (from docs + code sweep)

Grouped as the auditor returned them; all open unless noted.

- **Auth/Users:** Hebrew Identity errors (A1); open self-registration decision (A2); Google OAuth not wired (A3); spec §7 admin/CS password reset — _partially addressed: admin reset now exists; self-service still missing_ (A4).
- **Operations:** IncidentReport creates no action item (O1); multi-role hub scoping shows only first role's items (O2).
- **Scheduling:** "first incomplete" model resolver (S1); Israel-vs-UTC date window (S2); `TimeOnly` as text (S3); drag-reorder syllabus (S4); calendar grid (S5); offline attendance queue — _out of scope by design_ (S6); stale-gap auto-resolve (S7).
- **Logistics:** `DeactivateAsync` hook bypass if a future path sets `IsActive=false` via `UpdateAsync` (L1).
- **Dashboards:** live-refresh polling (D1); metric count-up (D2).
- **Design/UX:** `_PageShell` migration of 20 pages (U1); static People greeting + repeated shell (U2); missing save toasts on People/Curriculum/Scheduling (U3); full mobile table reflow (U4); bulk-select roster enroll (U5).
- **Testing:** indirect assertions (T1); naive-UTC month boundary (T2); Class-name partial-index test (T3); AuditInterceptor update-path test (T4); `RoleRoutingTests` exact-string compare (T5).
- **Ops/Infra:** free-tier cold start / keep-warm (I1); nightly `pg_dump` backups — _verify_ (I2); ICU `he-IL` collation — _verify_ (I3); full-text search — _phased_ (I4); SELECT-FOR-UPDATE drain (I5); dedicated audit table — _phased_ (I6).
- **Other:** SignalR real-time Action Hub — _phased_ (X1).

---

## What changed this session
- **`/Help`** — in-app guide (roles, user-management explainer, per-area cards, FAQ), reachable from every dashboard + AccessDenied.
- **`/Admin/Users`** — full user & role management (closes B1), with a last-admin safety guard. 18 new tests; suite at 275/275.
