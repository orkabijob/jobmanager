# Orkabi — Session Handoff & Status

_Last updated: 2026-06-24. Hand this to the next session/team to continue._

Orkabi (Hebrew: **עורקבי**) is a role-based, 100%-Hebrew RTL web app for an educational crafting business — managing operations, logistics, instructor shifts, and curriculum. Built as a **modular monolith** in ASP.NET Core 8, in **vertical slices**.

---

## ✅ Status: Slice 0 (walking skeleton) is COMPLETE, deployed, and LIVE

- **Live:** https://orkabi.onrender.com (login works; admin lands on `/Dashboard/Admin`)
- **Repo:** https://github.com/orkabijob/jobmanager (branch `master`)
- **Tests:** 14/14 green (`dotnet test`). Build green under `TreatWarningsAsErrors`.

### What Slice 0 delivers
- **Auth:** ASP.NET Core Identity (int keys), **email/password** login + **Google OAuth** plumbing (Google not yet configured → its button auto-hides). Cookie auth (HttpOnly, env-branched Secure, `/api/*` → 401-not-302). Password policy relaxed to **8+ chars, no complexity**. OAuth `email_verified` security gate.
- **RBAC:** 4 fixed roles (Admin / CustomerService / Logistics / Instructor) via policies; a role-router home (`/`) + 4 role-gated dashboards.
- **Cross-cutting foundations** every later slice builds on: the **archival** `IArchivable` + global-query-filter pattern (on a throwaway `Probe` entity), `BaseEntity` + **audit interceptor** (sync+async), the **dual-provider** data story (SQLite `EnsureCreated` for fast offline tests / **Npgsql migrations for prod**), `IsraelClock` (Asia/Jerusalem) stub.
- **Design:** an "Apple **Liquid Glass**" RTL design system — **a photo backdrop** (`wwwroot/img/bg-liquid.jpg`, water/granite) that the glass **refracts**, recessed-well inputs, **text-immune** lensing (refraction lives on a `.glass__lens` background layer, never on text), `he-IL` culture, self-hosted Assistant + Heebo variable fonts. An Admin **bento command-center** dashboard (⚠️ with **placeholder** data — 847 clients, sample tasks/alerts — NOT real yet).
- **Ops:** `/health`, the `/api/*` 401 seam, Dockerfile, `render.yaml`, env-var admin seed, `docs/DEPLOY.md`.

---

## Live infrastructure (where things run)

| Piece | Detail |
|---|---|
| **Host** | Render (free, Docker, **auto-deploys on push to `master`**). Free tier **sleeps after ~15 min** → first hit cold-starts ~30–60s and can throw a one-off transient error that clears on refresh. |
| **DB** | Neon (free) — project `orkabi` (`cold-math-14190037`), org `org-wild-hall-90364045`, database `neondb`. **Both** `ConnectionStrings__Default` and `__Migrations` use the **direct** (non-pooled) endpoint — we dropped the pooler because the long pooled string (with `Max Auto Prepare`) kept getting a line-break mangled on paste, and direct is fine at this scale. |
| **Secrets** | All live secrets are **Render env vars** (NOT in the repo): the two `ConnectionStrings__*`, `SEED_ADMIN_EMAIL`/`SEED_ADMIN_PASSWORD`, and (when wired) `Authentication__Google__ClientId`/`Secret`. Get them from the Render dashboard or the user. |
| **Admin login** | The seeded admin = `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` (e.g. `orkabijob@gmail.com`). Seeded on boot, idempotent. Remove `SEED_ADMIN_PASSWORD` from Render after first boot. |
| **Migrations** | Already applied to Neon. App boot re-runs `MigrateAsync` (no-op if none pending) then seeds roles + admin. |

---

## 🚧 What's left — the roadmap (vertical slices, build in order)

See `docs/superpowers/plans/2026-06-23-orkabi-roadmap.md`. Each slice gets its own just-in-time detailed plan.

- **Slice 1 — People.** FIRST: **remove the `Probe` scaffolding entity + its migrations**, and the **unused Bootstrap/jQuery/site.css/`_ValidationScriptsPartial`** template cruft. Then: `AcademicYear` (lookup + seed current year) → Schools → Classes (with `AcademicYearId` + `status`) → Clients → the **`Enrollment` join** (`is_tryout`/`paid_materials`/`paid_monthly` move from Client → Enrollment).
- **Slice 2 — Curriculum + Scheduling.** Models, Syllabus (+ ordered models), Shift_Template→Instance with `ShiftInstanceGenerator`, `Substitution_Request` + date-scoped resource auth, `Lesson_Log` (incl. `ExpectedLessonsAtLogTime` snapshot), Attendance (HTMX instructor mobile view + swipe/tap). Add `htmx.min.js` to `_Layout`.
- **Slice 3 — Operations + Real-Gap.** Extra_Hours, Incident_Report, Vacation_Request (single-approval), the Outbox-backed Real-Gap pacing monitor.
- **Slice 4 — Logistics + Automations.** Logistics_Order + dispute loop, `SupplyPacingService`, the **in-process** `BackgroundService` scheduler with catch-up-on-wake + `JobExecutionLog` (birthdays, shift-gen), event-driven absence/drop-out, deferred tryout follow-up.
- **Slice 5 — Action Hub + real dashboards.** `Action_Item` ticketing (polling-first) + replace the **placeholder** Admin bento data with real metrics/tasks/alerts; build the Logistics master packing list, CS surfaces, syllabus-management module.

---

## ⚠️ Known issues / tech debt (deferred, non-blocking)

- **`Probe` test entity + its migrations** ship in prod — remove at the start of Slice 1.
- **Unused template cruft** (Bootstrap/jQuery/`wwwroot/css/site.css`/`js/site.js`/`_Layout.cshtml.css`/`_ValidationScriptsPartial`) — sweep it (no longer referenced by `_Layout`).
- **Test isolation:** `GoogleSchemeTests.Google_challenge_redirects_to_accounts_google_com` **fails when run in isolation** but **passes in the full suite** (14/14). It's not self-contained — make it standalone.
- **Hebrew Identity errors:** Identity validation messages render in **English** in a Hebrew app — add a Hebrew `IdentityErrorDescriber`.
- **Open self-registration:** anyone can hit `/Account/Register` (they get no role → AccessDenied). Decide if it should be admin-only.
- **Google OAuth not wired:** to enable, create a Google OAuth client, set `Authentication__Google__ClientId`/`Secret` in Render, and add the redirect URI `https://orkabi.onrender.com/signin-google` (NOT the page path). The login button then appears automatically.
- **Dashboard data is placeholder** (mock) until Slices 1–5 fill it.
- **Free-tier sleep:** Render + Neon both sleep → ~30–60s cold start + rare transient first-hit error. Optional later: a keep-warm ping or a ~$5 always-on VPS.
- Minor: `RoleRoutingTests` uses an exact-string `Location` compare (prefer `LocalPath`).

---

## How this project is built (process + conventions)

- **Subagent-driven development:** per task → implementer subagent → reviewer-gate → fix; a final whole-branch review (strongest model). Tests are xUnit. SQLite (`EnsureCreated`) for the fast offline inner loop; Npgsql/Neon for prod (the **real-Neon deploy is the sole Postgres-fidelity gate**).
- **Commands:** `dotnet test` (14/14), `dotnet build`, `dotnet run` (local — note `launchSettings.json` picks the port; use `--no-launch-profile` to honor `ASPNETCORE_URLS`). Apply migrations to Neon: `ORKABI_MIGRATIONS_CONNSTRING="<direct neon string>" dotnet ef database update --project src/Orkabi.Web`.
- **Deploy:** push to `master` → Render auto-deploys (~3–5 min).
- **User working preferences (important):** consult a **specialist agent team** (Architect / Full-stack / Reviewer / a dedicated **Apple Liquid-Glass designer**) for decisions instead of asking the user; work autonomously; reviewer-gate each step; for design, route through the Liquid-Glass designer (target = true Apple Liquid Glass). The user oversees at a PM altitude.

## Key documents
- Spec: `docs/superpowers/specs/2026-06-23-orkabi-design.md`
- Roadmap: `docs/superpowers/plans/2026-06-23-orkabi-roadmap.md`
- Slice 0 plan: `docs/superpowers/plans/2026-06-23-orkabi-slice-0-walking-skeleton.md`
- Design system: `docs/design/liquid-glass-design-system.md`
- Deploy guide: `docs/DEPLOY.md`

## Suggested first moves for the next session
1. Confirm the live site + current design look with the user (design was still being iterated — inputs/glass/background were being tuned).
2. Wire Google OAuth if the user wants it (creds + redirect URI above).
3. Start **Slice 1 (People)**: remove `Probe` + template cruft, write the just-in-time Slice 1 plan, build it via the subagent-driven process.
