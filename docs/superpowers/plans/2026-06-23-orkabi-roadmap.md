# Orkabi — Implementation Roadmap (ordered plan index)

> Source spec: `docs/superpowers/specs/2026-06-23-orkabi-design.md`. Each slice below is a self-contained sub-project that produces working, testable, deployable software. Build strictly in order — each slice depends on the patterns established by the previous one. Each slice gets its own detailed bite-sized plan written just-in-time when we reach it.

## Build order

| # | Slice | Goal (working software at the end) | Depends on | Detailed plan |
|---|---|---|---|---|
| **0** | **Walking Skeleton** | A deployed ASP.NET Core 8 app on Render+Neon: email/password **and** Google login, 4 roles, role-routed dashboards, the archival global-query-filter base, the RTL Apple-glass base layout, and a `/health` endpoint. Proves every integration seam end-to-end. | — | `2026-06-23-orkabi-slice-0-walking-skeleton.md` ✅ |
| **1** | **People** | **`AcademicYear` (lookup + seed current year)** first, then Schools, Classes (with `AcademicYearId` + `status`), Clients(Students), and the `Enrollment` join (tryout/payment status). CS can build rosters. | Slice 0 | _just-in-time_ |
| **2** | **Curriculum + Scheduling** | Models, Syllabus (+ ordered models), Shift_Template→Instance with `ShiftInstanceGenerator`, `Substitution_Request` + the date-scoped resource authorization, **Lesson_Log (incl. `ExpectedLessonsAtLogTime` snapshot)**, Attendance (HTMX instructor mobile view + swipe/tap). | Slices 0-1 | _just-in-time_ |
| **3** | **Operations + Real-Gap** | Extra_Hours, Incident_Report, Vacation_Request (single-approval), and the Outbox-backed Real-Gap pacing monitor (proves the domain-event→Action_Item path). | Slices 0-2 | _just-in-time_ |
| **4** | **Logistics + Automations** | Logistics_Order + dispute loop, `SupplyPacingService`, and the in-process scheduler with catch-up-on-wake (birthdays, shift-gen) + event-driven absence/drop-out + deferred tryout follow-up. | Slices 0-3 | _just-in-time_ |
| **5** | **Action Hub + Dashboards** | The Action_Item ticketing hub (polling-first), the Admin bento command-center, Logistics master packing list, CS surfaces, and the syllabus-management module. | Slices 0-4 | _just-in-time_ |

## Cross-cutting threads (built into Slice 0, extended every slice)
- **Archival** (`status` enum + `AcademicYear` FK + global query filter on aggregate roots) — scaffolded in Slice 0, every new aggregate root opts in.
- **Audit fields** (`created_at/by`, `updated_at/by` via SaveChanges interceptor) — Slice 0, applied to every entity.
- **Hebrew RTL + Apple-glass design system** — base layout + tokens in Slice 0, every new page inherits.
- **Israel-TZ rule** (store UTC, display/schedule `Asia/Jerusalem`) — `IsraelClock` stub (the `TimeZoneInfo` constant) created in **Slice 0 Task 10**; time-of-day logic consumes it from Slice 2.
- **HTMX** — `htmx.min.js` added to `_Layout` in **Slice 2**; instructor attendance + lesson-log pages use HTMX partials (never full-page POSTs) from Slice 2 onward.
- **Pacing snapshot** — `Lesson_Log.ExpectedLessonsAtLogTime` (set from `Syllabus_Models.ExpectedLessonsToComplete` at save time) is a **Slice 2** deliverable, so Slice 3's Real-Gap monitor has stable history across syllabus edits.
- **`is_active` vs `Archived` invariant** — documented on `IArchivable.cs` from Slice 0: `Archived` is set only by the academic-year batch job; `is_active = false` means inactive within the current year and must NOT trigger the archival filter.
- **Testing** — xUnit + `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory) + `Testcontainers.PostgreSql` (real Postgres in Docker), harness in Slice 0. Note: Testcontainers uses a **direct** Postgres, so it cannot catch Neon-pooler issues — the day-one real-Neon deploy must explicitly verify boot-migration.

## Phased (per spec §12, not silent skips)
- Action Hub real-time = **polling first** (Slice 5); SignalR later.
- Full-text search deferred (ILIKE suffices).
- Dedicated audit *table* later; base audit *fields* from Slice 0.
