# Next-session kickoff prompt

_Paste the block below into a fresh Claude Code session opened in the Orkabi repo._

---

You are continuing work on **Orkabi** — a Hebrew RTL, role-based ASP.NET Core 8 Razor Pages
modular monolith (Apple "Liquid Glass" design system, Neon Postgres in prod, SQLite for tests).
Your job this session is to **work the backlog in `docs/orkabi-backlog.md` as a checklist**, using
specialist agent teams, until I tell you to stop.

## Read first (in this order)
1. `docs/orkabi-backlog.md` — the authoritative to-do list (IDs: **B**locking, **F**unctional, **P**olish, **TD** tech-debt). This is your checklist.
2. `docs/personas-and-gaps.md` — per-persona detail and "where it should live" for each gap.
3. `docs/HANDOFF.md` — project status, conventions, infra, the build/deploy process.

## How to work (per item)
For each backlog item you pick up:
1. **Plan** — dispatch a code-architect/explore agent to confirm the exact files, the existing pattern to mirror, and the authorization scope. Verify every claim against source (do not trust the backlog blindly — it's a map, not gospel).
2. **TDD** — write failing tests FIRST (RED), then minimal code (GREEN). Mirror the existing test harness (`OrkabiAppFactory`, `SqliteFixture`, `TestLogin`) and the per-page authz + service-logic test split already used in the repo.
3. **Review** — dispatch a code-reviewer agent (spec adherence + bugs + design-system fidelity) before considering the item done; fix what it finds.
4. **Verify** — run `dotnet build` and `dotnet test`; confirm the whole suite is green (it was **275/275**) and the new tests pass. Never claim done without the test output.
5. **Record** — check the box in `docs/orkabi-backlog.md`, and append the item to `docs/HANDOFF.md`'s status when it's a user-facing feature.

## Agent-team strategy
- **Parallelize independent items.** The cheap authz/markup fixes (F5, F6, F7, F8, F9) and several P/TD items are independent — fan them out to parallel implementer agents (use git worktrees if they touch overlapping files).
- **Sequence dependent work.** Anything sharing a service/migration goes in one agent's lane.
- Use a **designer pass** (Liquid-Glass RTL) for any new page: reuse `base.css`/`tokens.css` classes (`dash-shell`, `page-head`, `glass--tile people-panel`, `data-table`, `nav-card`, `subnav`, `form-*`, `toggle-pill`, `status-chip`) before inventing CSS; honor the §5 legibility contract (body text on `--lg-fill-strong`).
- After each item (or small batch), report progress to me at PM altitude: what shipped, test count, what's next.

## Suggested order
Start with **B2** (instructor substitution-request page) and **B3** (`/Admin/AcademicYears`) — both have the service layer already built and just need a page. Then **F1** (extra-hours deny, mirrors Vacations), then the cheap authz/markup batch (F5–F9), then the loops (F2/F3/F4), then F10–F20, then P/TD as capacity allows. Pair related test-debt (TD18–TD22) with the features they touch.

## Conventions & guardrails
- **TDD is mandatory** (the repo practices RED-first). No production code without a failing test first.
- **Match the codebase:** services in `Modules/<Area>/`, pages in `Pages/`, admin-only pages under `Pages/Admin/` with `[Authorize(Roles = AppRoles.Admin)]`, new migrations via `dotnet ef migrations add <Name> --project src/Orkabi.Web` (design-time factory uses a fake Npgsql string; offline).
- **Autonomy:** build autonomously via the specialist agent team; route design/implementation questions to agents, not to me. I oversee at PM altitude. The one decision that IS mine: anything that gates scope or product direction (e.g. whether to gate self-registration **TD2**, what `EnrollmentStatus.Completed` should mean **F15**) — surface those, don't guess silently.
- **DEPLOY GATE (hard):** pushing to `master` triggers a **production deploy** on Render and requires my **explicit sign-off**. Do not push without it. Commit to a working/feature branch; ask before merging to `master`.
- Keep the test suite green at every step. If a migration is needed, note it explicitly (it must run on real Neon at deploy).

Begin by reading the three docs, confirming the current `dotnet test` count is green, then start on **B2**. Tell me your plan for B2 before you write code.
