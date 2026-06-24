# Orkabi — Session Handoff & Status

_Last updated: 2026-06-24. Hand this to the next session/team to continue._

Orkabi (Hebrew: **עורקבי**) is a role-based, 100%-Hebrew RTL web app for an educational crafting business — managing operations, logistics, instructor shifts, and curriculum. Built as a **modular monolith** in ASP.NET Core 8, in **vertical slices**.

---

## ✅ Status: Slices 0 + 1 + 2 are COMPLETE, deployed, and LIVE. Slice 3 in progress.

- **Live:** https://orkabi.onrender.com (login works; admin lands on `/Dashboard/Admin`; CS/Admin manage People at `/People`, Curriculum at `/Curriculum`, Scheduling at `/Scheduling`; instructors take attendance from `/Dashboard/Instructor`)
- **Repo:** https://github.com/orkabijob/jobmanager (branch `master`; Slice-2 merge `7148f39`)
- **Tests:** 90/90 green (`dotnet test`). Build clean.
- **Slice 2 deploy verified:** anonymous `/Scheduling/Templates` + `/Curriculum` → 302 → login, `/health` → ok. Boot `MigrateAsync` applied `AddCurriculum`/`AddScheduling`/`AddClassSyllabusId` on real Neon (the spec's Postgres-fidelity gate — passed).

### What Slice 0 delivers
- **Auth:** ASP.NET Core Identity (int keys), email/password + Google OAuth plumbing (Google not yet configured → button auto-hides). Cookie auth (HttpOnly, env-branched Secure, `/api/*`→401). Password policy 8+ chars, no complexity. OAuth `email_verified` gate.
- **RBAC:** 4 fixed roles (Admin / CustomerService / Logistics / Instructor); role-router home (`/`) + 4 role-gated dashboards.
- **Cross-cutting foundations:** `IArchivable` + global-query-filter pattern, `BaseEntity` + audit interceptor (sync+async), dual-provider data (SQLite `EnsureCreated` for tests / **Npgsql migrations for prod**), `IsraelClock` (Asia/Jerusalem) constant.
- **Design:** Apple **Liquid Glass** RTL system — photo backdrop (`wwwroot/img/bg-liquid.jpg`) the glass refracts, recessed-well inputs, text-immune `.glass__lens` layer, `he-IL` culture, self-hosted Assistant + Heebo fonts. Admin **bento** dashboard (⚠️ still **placeholder** data).
- **Ops:** `/health`, the `/api/*` 401 seam, Dockerfile, `render.yaml`, env-var admin seed, `docs/DEPLOY.md`.

### What Slice 1 (People) delivers
- **Entities (`Modules/People/`):** `AcademicYear` (lookup; single-current enforced by partial unique index; seeded **תשפ"ו / 5786**, 2025-09-01→2026-06-30), `School`, `Class` (the **only** archival aggregate — `EntityStatus` + global query filter; partial unique index on school+year+name WHERE Active), `Client` (uses `IsActive`, orthogonal to archival — dropouts stay visible), `Enrollment` join (`EnrollmentStatus` Active/Tryout/Dropped/Completed; `IsTryout`/`PaidMaterials`/`PaidMonthly`; partial unique index on (client,class) WHERE not Dropped → re-enroll after drop allowed). All FKs `Restrict`. `Class.SyllabusId` deliberately deferred to Slice 2.
- **Service layer:** `AcademicYearService` (incl. transactional `SetCurrentAsync`), `SchoolService`, `ClassService` (archived view via `IgnoreQueryFilters`), `ClientService` (active-only vs all), `EnrollmentService` (app-level dup guard + DB safety-net index, drop, tryout/payment toggle).
- **Pages (`Pages/People/`, all `[Authorize(Roles = AppRoles.CsOrAdmin)]`):** People hub, Schools CRUD, Classes CRUD (+ archived filter), Clients CRUD (+ active filter), and the **signature Roster builder** (`/People/Classes/Roster/{id}`) — two-pane enroll/drop, three per-enrollment toggle pills (חומרים/חודשי/ניסיון), tryout tray + TRYOUT badge, friendly Hebrew duplicate-enroll error.
- **Design:** new People component vocabulary in `base.css` (subnav, page-head, data-table, recessed-well forms, segmented control, toggle pill, status chips, empty states, roster two-pane, tryout tray) — all from `docs/design/slice-1-people-design.md`.
- **Cleanup:** removed the `Probe` scaffold + all Bootstrap/jQuery/`site.css`/`_ValidationScriptsPartial`/`_Layout.cshtml.css` template cruft; fixed the `GoogleSchemeTests` file-lock flake.

---

## Live infrastructure (where things run)

| Piece | Detail |
|---|---|
| **Host** | Render (free, Docker, **auto-deploys on push to `master`**). Free tier sleeps after ~15 min → ~30–60s cold start; if a boot migration ever fails its health check, Render keeps the previous deploy serving (no outage). |
| **DB** | Neon (free) — project `orkabi` (`cold-math-14190037`), org `org-wild-hall-90364045`, db `neondb`. Both `ConnectionStrings__Default` and `__Migrations` use the **direct** (non-pooled) endpoint. |
| **Secrets** | All live secrets are **Render env vars**: the two `ConnectionStrings__*`, `SEED_ADMIN_EMAIL`/`SEED_ADMIN_PASSWORD`, and (when wired) `Authentication__Google__ClientId`/`Secret`. Not in the repo. |
| **Admin login** | Seeded admin = `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD`. Idempotent on boot. |
| **Migrations** | Applied at boot via `MigrateAsync` (idempotent). To apply manually: `ORKABI_MIGRATIONS_CONNSTRING="<direct neon string>" dotnet ef database update --project src/Orkabi.Web`. |

---

## 🚧 Roadmap (vertical slices, build in order)

See `docs/superpowers/plans/2026-06-23-orkabi-roadmap.md`.

- **Slice 0 — Walking skeleton.** ✅ LIVE
- **Slice 1 — People.** ✅ LIVE
- **Slice 2 — Curriculum + Scheduling.** ✅ LIVE. Models, Syllabus (+ ordered Syllabus_Models), `Class.SyllabusId` FK, Shift_Template→Instance with `ShiftInstanceGenerator`, `Substitution_Request` + **date-scoped resource authorization** (service guard `CanAccessShiftAsync`), `Lesson_Log` (+ `expected_lessons_snapshot`), Attendance via optimistic `/api/attendance` (idempotency key) + lesson-log HTMX pacing; HTMX self-hosted + antiforgery-wired in `_Layout`; the signature instructor attendance surface (Blue-Jay monolith + tap-to-mark). `TimeOnly` shift times stored as `text` (EF9-SQLite value-converter parity — tech-debt only if time-range SQL is needed later).
- **Slice 3 — Operations + Real-Gap.** 🔄 IN PROGRESS (branch `slice-3-operations-realgap`). Extra_Hours, Incident_Report, Vacation_Request (single-approval), the **Outbox + Action_Item kernel**, and the Real-Gap pacing monitor (Lesson_Log save → OutboxEvent → drain → Admin Action_Item with dedup key).
- **Slice 4 — Logistics + Automations.** Logistics_Order + dispute loop, `SupplyPacingService`, the in-process `BackgroundService` scheduler with catch-up-on-wake + `JobExecutionLog`.
- **Slice 5 — Action Hub + real dashboards.** `Action_Item` ticketing + replace the **placeholder** Admin bento data with real metrics; Logistics packing list, CS surfaces, syllabus-management.

---

## ⚠️ Known issues / tech debt (deferred, non-blocking)

- **Admin dashboard data is placeholder** (mock) until Slice 5.
- **Hebrew Identity errors:** Identity validation messages render in English — add a Hebrew `IdentityErrorDescriber`.
- **Open self-registration:** anyone can hit `/Account/Register` (gets no role → AccessDenied). Decide if it should be admin-only.
- **Google OAuth not wired:** create a Google OAuth client, set `Authentication__Google__ClientId`/`Secret` in Render, add redirect URI `https://orkabi.onrender.com/signin-google`. Button then appears automatically.
- **Slice-1 polish (deferred to Slice 2):** People topbar greeting is static "שלום" (no user name) and the topbar/subnav shell is repeated per page (extract a shared `_PeopleShell`/layout partial); no save-success toasts. Minor test-coverage gaps (Class-name partial-index enforcement test; AuditInterceptor update-path test).
- **Free-tier sleep:** ~30–60s cold start after idle. Optional later: UptimeRobot `/health` ping or a ~$5 always-on host.
- Minor: `RoleRoutingTests` uses an exact-string `Location` compare (prefer `LocalPath`).

---

## How this project is built (process + conventions)

- **Subagent-driven development:** per task → fresh implementer subagent → task-review gate (spec + quality) → fix; a final whole-branch review (strongest model) before merge; deploy + verify. Tests are xUnit; SQLite `EnsureCreated` for the fast inner loop, Npgsql/Neon for prod (the real-Neon deploy is the Postgres-fidelity gate). Progress ledger at `.superpowers/sdd/progress.md` (gitignored).
- **Commands:** `dotnet test`, `dotnet build`, `dotnet run`. New migration: `dotnet ef migrations add <Name> --project src/Orkabi.Web` (offline; design-time factory uses a fake Npgsql string).
- **Branch/deploy:** each slice on its own branch (`slice-N-...`); merge to `master` only when complete + reviewed + green → Render auto-deploys → verify `/health` + a slice route. **Pushing to `master` triggers a production deploy and requires explicit user sign-off** (harness-gated).
- **User working preferences (important):** FULL autonomous mandate through ALL slices — do not pause between slices; route every question/decision to a **specialist agent** (Architect / Full-stack / Reviewer / Apple Liquid-Glass designer / researcher), never to the user. The user oversees at PM altitude. The only confirmed exception: the production deploy push to `master`.

## Key documents
- Spec: `docs/superpowers/specs/2026-06-23-orkabi-design.md`
- Roadmap: `docs/superpowers/plans/2026-06-23-orkabi-roadmap.md`
- Slice 0 plan: `docs/superpowers/plans/2026-06-23-orkabi-slice-0-walking-skeleton.md`
- Slice 1 plan: `docs/superpowers/plans/2026-06-24-orkabi-slice-1-people.md`
- Design system: `docs/design/liquid-glass-design-system.md`
- Slice 1 design: `docs/design/slice-1-people-design.md`
- Deploy guide: `docs/DEPLOY.md`

## Suggested next moves
1. Finish **Slice 2 (Curriculum + Scheduling)** via the subagent-driven process (architect + designer blueprints already in flight; write the just-in-time plan, execute task-by-task, deploy + verify).
2. Optionally wire Google OAuth (needs creds from the user).
