# Orkabi — Business Management System: Design Spec

**Status:** Draft for review · **Date:** 2026-06-23 · **Author:** Claude (Opus 4.8) with a 3-agent review team (Architect, Designer, FullStack)

---

## 1. Context — why this, why now

Orkabi is an educational crafting business in Israel that runs workshops in schools. It needs one role-based web app to manage operations, logistics, instructor shifts, and curriculum/pacing — replacing scattered manual tracking. The app is **100% Hebrew, RTL-native**, online-only, with all alerts living inside an in-app "Action Hub" (no SMS/email). Cost must be minimal.

The owner already has a mature ASP.NET Core 8 platform, **ShiftManager** (`C:\Users\katzi\Downloads\ShiftManager`, ~488K LOC), which independently solved several of Orkabi's hardest problems: the shift **Template→Instance** pattern, production-grade Hebrew/RTL, cookie auth, approval workflows, and background jobs. Rather than rebuild those from scratch (or fork the whole workforce/military-domain codebase), Orkabi is a **fresh, lean app that recycles ShiftManager's proven *patterns*** and builds the education domain + a distinct premium visual identity new. The intended outcome: a sleek, reliable, near-free tool the owner can run for a single business and grow later.

This spec was reviewed by three independent expert agents; all three returned **APPROVE/GO-WITH-CHANGES**. Their corrections are folded in below.

---

## 2. Locked decisions

| Decision | Choice | Notes |
|---|---|---|
| Backend | **ASP.NET Core 8 (LTS), Razor Pages + thin REST API** | Aligns with the owner's existing Razor/Figma tooling |
| Data access | **EF Core 9 + Npgsql** | int-backed enums; DateOnly/TimeOnly |
| Database | **PostgreSQL — Neon free tier** | Serverless: auto-resumes on connect in ~seconds (no manual unpause), so usage gaps/holidays need no keep-warm. We use EF Core + ASP.NET Identity, so Supabase's bundled auth/storage add no value here. Use Neon's pooler endpoint |
| App host | **Free wake-on-request host** (Render free / Fly) | No keep-warm baseline; sleeps after ~15 min idle, auto-wakes on next request (~30-50s cold start accepted). An optional UptimeRobot `/health` ping is a trivial later upgrade if snappier mornings are wanted |
| Scheduling | **In-process `BackgroundService` + `PeriodicTimer` (`Asia/Jerusalem`) + catch-up-on-wake** | Daily jobs run on first app wake each day via `JobExecutionLog` catch-up (no external cron, no keep-warm). Absence/drop-out are event-driven (no daily run) |
| Language | **Hebrew-only**, culture fixed `he-IL`, `dir="rtl"` global | Drop ShiftManager's bilingual/culture-switching machinery |
| UI | **Fresh "Apple-glass" design system** | Must not feel like a ShiftManager copy |
| Auth | **ASP.NET Core Identity + cookie + Google OAuth** | Email/password AND Google SSO (per PRD) |
| RBAC | **4 fixed roles via policies + resource-based handlers** | NOT ShiftManager's 135-grant ABAC engine |
| Background jobs | **`BackgroundService`/handler + `JobExecutionLog`** | Hangfire dropped |
| Enrollment | **Many-to-many `Enrollment` join table** | Future-proof; tryout/payment status moves to enrollment level |
| Build approach | **Full system specced upfront, built in vertical slices** | Walking skeleton deployed first |

**Cost:** ~$0/mo (Neon free + free app host + free external cron). Accepted tradeoff: morning cold start, and the discipline of treating the free tiers' sleep as a first-class design constraint.

---

## 3. Architecture — lean modular monolith

Single ASP.NET Core 8 app. Razor Pages render views; a **thin REST API** serves the snappy instructor interactions. Organized by bounded context, with boundaries enforced through **service interfaces**, not just folders (no cross-module `DbSet` reaching).

```
Orkabi/
├─ Modules/
│  ├─ Identity/     Users, 4 roles, Identity + Google SSO, cookie auth, resource-based authz
│  ├─ People/       Clients(Students), Schools, Classes, Enrollment (join)
│  ├─ Curriculum/   Models, Syllabus, Syllabus_Models (ordered), AcademicYear
│  ├─ Scheduling/   Shift_Template→Instance, ShiftInstanceGenerator, Substitution_Request, Lesson_Log, Attendance
│  ├─ Operations/   Extra_Hours, Incident_Report, Vacation_Request
│  ├─ Logistics/    Logistics_Order, SupplyPacingService, dispute loop
│  └─ ActionHub/    Action_Item (shared kernel — subscribes to domain events, never queries other modules)
├─ Shared/          RTL infra, design system, archival query-filter + scope service, Israel-TZ constant, Outbox, JobLog
├─ Jobs/            DailyAutomations handler + outbox drainer (triggered by /internal/jobs/tick)
└─ Data/            AppDbContext (global filters, audit interceptor), migrations, seed
```

**ActionHub is a shared kernel, not a peer module** (Architect): every module raises domain events; ActionHub subscribes and creates tickets. It never reads `Lesson_Log`/`Attendance` directly.

**Rendering strategy** (Architect + Designer reconciled):
- **Razor Pages** for Admin dashboards, CS workflows, Logistics lists, syllabus management (dense, form-per-action, server-rendered).
- **HTMX** for server-driven partial updates (lesson-log "3 of 8" indicator, simple row updates) — zero custom JS where possible.
- **Thin REST API + a small slice of Motion One JS** for the instructor **attendance** screen: optimistic client-side swipe/fill animation, persisted async via the API with an **idempotency key** (double-tap safety on flaky mobile).
- **Action Hub real-time: polling first** (20–30s); SignalR is a deliberate phase-2 upgrade, not a silent skip.

---

## 4. Data model

Adopts the PRD schema with the reviewers' refinements. **Every entity** gets base audit fields in the first migration: `created_at`, `created_by_user_id`, `updated_at`, `updated_by_user_id` (via a SaveChanges interceptor). Enums are int-backed.

### Identity & People
- **User** — managed by ASP.NET Identity; `name`, `phone`, `role` (Admin/CustomerService/Logistics/Instructor), `is_active`. Role string representation standardized everywhere (claims == `Action_Item.assigned_to_role`).
- **School** — `name`, `city`, `external_website_url`.
- **Class** — `school_id`, `syllabus_id`, `name`, `status` (Active/Archived), `academic_year_id`.
- **Client (Student)** — `name`, `parent_phone`, `age`, `address`, `birthday` (DateOnly), `is_active`. **Tryout/payment status removed from here** → moved to Enrollment.
- **Enrollment (join)** — `client_id`, `class_id`, `status`, `is_tryout`, `paid_materials`, `paid_monthly`, `enrolled_at`. A student may hold multiple active enrollments. Rosters/attendance read through this.

### Curriculum
- **AcademicYear** (new lookup) — `id`, `label`, `start_date`, `end_date`, `is_current`. Replaces the fragile string `academic_year` tag; enforces one current year + range queries.
- **Model** — `name`, `expected_lessons_to_complete`, `material_link`, `video_link`.
- **Syllabus** — `name`, `start_date`, `end_date`, `status`.
- **Syllabus_Models (junction)** — `syllabus_id`, `model_id`, `order_index`.

### Scheduling
- **Shift_Template** — `class_id`, **`default_instructor_id`**, `day_of_week`, `start_time`, `end_time`, `academic_year_id`, `status`.
- **Shift_Instance** — `template_id`, **`actual_instructor_id`**, `date` (DateOnly), `status`.
- **Substitution_Request** (new) — `shift_instance_id`, `requesting_instructor_id`, `substitute_instructor_id`, `status`, `approved_by_user_id`, `approved_at`. The audit trail; mutating `actual_instructor_id` alone is insufficient.
- **Lesson_Log** — `shift_instance_id`, `model_id`, `status` (In_Progress/Completed), `instructor_notes`, **`expected_lessons_snapshot`** (captures the model's expected lessons at log time, so later syllabus edits don't corrupt historical gap math).
- **Attendance** — `lesson_log_id`, `client_id`, `status` (Present/Absent). Submitted with a client-supplied idempotency key.

### Operations
- **Extra_Hours** — `shift_instance_id`, `instructor_id`, `hours`, `reason`, `status` (Pending/Approved).
- **Incident_Report** — `shift_instance_id`, `instructor_id`, `severity`, `description`.
- **Vacation_Request** — `instructor_id`, `start_date`, `end_date`, `status` (Pending/Approved/Denied). Single-approval.

### Logistics
- **Logistics_Order** — `class_id`, `model_id`, `quantity`, `status` (Pending/Packed/Accepted/Disputed), `dispute_notes`, `delivered_at`.

### Action Hub & infrastructure
- **Action_Item** — `type` (Absence/Gap/Dispute/Task/Birthday/Tryout_Followup), `status` (Open/Resolved), `assigned_to_role` **or** `assigned_to_user_id`, `related_entity_id`, `description`, `due_date`, **`deduplication_key`** (unique — prevents duplicate tickets on any retry/restart).
- **OutboxEvent** — `id`, `event_type`, `payload jsonb`, `scheduled_for`, `created_at`, `processed_at`. Written in the same transaction as the triggering change; drained opportunistically + by the cron backstop.
- **JobExecutionLog** — `job_name`, `scheduled_for` (date), `ran_at`, `status`; unique `(job_name, scheduled_for)`. Idempotency + restart-resilience for daily jobs.

### Archival (cross-cutting) — handled as the highest-risk area
- **Global EF query filter on aggregate roots only** (`Class`, `Shift_Template`, `Syllabus`) — **not** on referenced lookups (`Model`, `AcademicYear`) to avoid silent null navigations. Filter scope is declared explicitly in `OnModelCreating` with a comment naming each filtered entity.
- **`IgnoreQueryFilters()` escape hatch** documented for cross-year admin reports (e.g. "total lessons by student" spanning years).
- **Partial unique indexes** (`WHERE status = Active`) so archiving doesn't unblock truly duplicate live records.
- **`is_active` vs archived are mutually exclusive invariants**: a mid-year dropout is `is_active = false` but stays in current-year history (never hidden by the filter). Enforced/commented in the archival service.
- Current⇄Historical UI toggle switches the active `AcademicYear` scope.

---

## 5. Core workflows

- **A. Real-Gap monitor (pacing).** On `Lesson_Log` save, in the same transaction, write an `OutboxEvent`. The drainer sums lessons spent on that `model_id`; if `> expected+1` and not complete → create an Admin `Action_Item` (`dedup_key = gap_<class>_<model>`). Live pacing recalculates against the *current* syllabus (PRD §6C); the per-log snapshot preserves historical correctness.
- **B. Substitution & access.** Admin approves a `Substitution_Request` → sets `Shift_Instance.actual_instructor_id`. Authorization is **resource-based and date-scoped**: an instructor may open a shift's roster/materials only when `actual_instructor_id == me AND date == today`, checked against the DB (not cached claims), so access opens/closes correctly by date.
- **C. Logistics dispute loop.** `SupplyPacingService` seeds `Pending` orders from `Syllabus_Models.order_index` + `Lesson_Log` progress → Logistics marks `Packed` → Instructor marks `Accepted`/`Disputed` → `Disputed` writes an urgent Logistics `Action_Item`, visible to Admin.
- **D. Tryout flow.** CS creates an `Enrollment` with `is_tryout = true` (no logistics; instructors use spare kits). The student is pinned at the roster bottom with a TRYOUT badge. On `Attendance = Present` for a tryout, an `OutboxEvent` with `scheduled_for = tomorrow 08:00 Asia/Jerusalem` is written → fires a CS follow-up `Action_Item` at the right time (fixes the "two mornings late" hole).
- **E. Automations — scheduled vs event-driven.** Two are genuinely *time-scheduled* (nothing but the calendar triggers them), run by the in-process scheduler, idempotent via `JobExecutionLog`: **birthdays** (24h before + day-of, to assigned instructor + Admin) and **shift-instance generation** (rolling horizon). **Tryout follow-up** is a time-*deferred* event (outbox `scheduled_for = tomorrow 08:00`, fired by the scheduler). The remaining two are **event-driven at write time — no daily run**: **double consecutive absence** (when an `Absent` Attendance is saved, check the client's previous lesson; if also Absent → CS Action_Item) and **mass drop-out** (when a client is set `is_active = false`, check the same class for ≥2 inactivations within 7 days → Admin Action_Item). All ticket creation uses `deduplication_key`.

**ShiftInstanceGenerator strategy:** rolling **~30-day horizon** generated by the daily tick, plus **on-demand regeneration** when a template changes (already-detached/edited instances preserved). Avoids materializing a whole year up front while keeping instances available.

---

## 6. Running jobs on free tiers — no keep-warm needed

The app is used daily during term, and both tiers auto-wake on demand, so **no keep-warm infrastructure is required** in the baseline:

- **DB (Neon):** scales to zero when idle and **auto-resumes on the next connection** in ~seconds — no manual unpause, even after a multi-week school break. This is the decisive reason Neon is chosen over Supabase (whose free tier pauses after 7 days and needs a manual dashboard restore).
- **App host:** sleeps after ~15 min idle and **auto-wakes on the next HTTP request** (~30-50s cold start, accepted). Spinning up restarts the process → the hosted services start → catch-up runs.
- **Jobs:** the **in-process `BackgroundService` + `PeriodicTimer` (`Asia/Jerusalem`, DST-correct)** runs scheduled jobs (birthdays, shift-instance generation, due tryout-followup outbox events). On every startup/wake it performs **catch-up**: for each daily job, if `JobExecutionLog` has no row for today (`unique(job_name, scheduled_for)`, `ON CONFLICT DO NOTHING`), it runs it now and records it. So the day's jobs fire the first time any user opens the app each morning — reliably and exactly once. The **Outbox** is drained by the same loop and opportunistically at the end of relevant requests (gap tickets appear promptly during active use). Event-driven automations (absence, drop-out) run inline at write time and need no scheduler at all.

**Accepted tradeoff:** ~30-50s cold start on the first request after an idle gap, and daily jobs fire at first-open rather than precisely at 08:00 (fine for these alerts). **Optional upgrade, no rewrite:** a single UptimeRobot `/health` ping keeps the app warm (eliminating cold starts) — add it later only if mornings feel slow. If the host's monthly free-hour budget ever gets tight, Fly.io's small always-on machine or a ~$5 VPS are drop-in alternatives.

---

## 7. Auth

- **ASP.NET Core Identity** for the user store, password hashing, and reset-token generation. Since there's no email, **password reset is an in-app Admin/CS-triggered flow** (fits "in-app only").
- **Cookie auth** `HttpOnly` + `SameSite=Lax` + `Secure`; port ShiftManager's `OnRedirectToLogin` returning **401 JSON for `/api/*`** (not a 302).
- **Google OAuth** via `Microsoft.AspNetCore.Authentication.Google` (`.AddGoogle()` + `.AddCookie()`).
- **4 role policies** (`AdminOnly`, `CSOrAdmin`, `LogisticsOrAdmin`, `InstructorOrAdmin`) + **resource-based handlers** for date-scoped shift access and Action_Item role-or-user visibility.
- **Anti-forgery enforced on the cookie-authed API** (token issued to page, sent via `RequestVerificationToken` header) — the API shares the cookie, so it shares the exposure.
- Decide the Identity **PK type up front** and keep it consistent across all FKs.

---

## 8. Postgres specifics

- **ICU `he-IL` collation set at DB provisioning** (one-time, hard to change later) for correct Hebrew sorting; ICU index on name columns. `ILIKE` for search at this scale; full-text deferred until needed.
- **Int-backed enums** (`.HasConversion<int>()`), not native PG enum types (which make migrations painful).
- **Store instants as `timestamptz` UTC; convert to `Asia/Jerusalem` only at the presentation edge and in the scheduler.** Pure dates use `DateOnly` (no TZ). `Asia/Jerusalem` is a first-class app constant; all job fire times derive from it (DST-correct).
- **Migrations** committed to source, applied at boot via `MigrateAsync()` **with a pre-migration `pg_dump` backup**; never `EnsureCreated`. Nightly `pg_dump` to cheap object storage.

---

## 9. Design system — "Apple-glass" (built fresh)

Authored **logical-property-native** (`*-inline-*`, `text-align: start/end`); `dir="rtl"` flips everything for free — explicitly **not** ShiftManager's ~1,200-line `[dir="rtl"]` override file.

- **Type:** self-hosted **Assistant** (UI, OFL) + **Heebo tabular-nums** for phone numbers/quantities/metrics. Drop Ploni (commercial). Subset to Hebrew+Latin; weights 400/500/600/700. No Hebrew italics; wrap phones/times/Latin in `<bdi dir="ltr">`. Type scales defined for mobile-instructor (base 17px) and dense-admin (base 15px).
- **Glass, three tiers** (restraint + layering = premium, not uniform blur): nav `saturate(180%) blur(24px)`, panels `blur(16px)`, inline `blur(8px)`. Panel fills `rgba(255,255,255,0.55)`, **strong 0.72 under data text**. Hairline light borders; layered ambient+contact+inner-highlight shadow stacks; radii 12/20/28/32. Blue-family **mesh-gradient backdrop** (no rainbow orbs). `-webkit-` prefix + `@supports` opaque fallback. **Blur only on fixed/static elements; scrolling lists stay opaque** (mobile perf); max 2 blur layers in viewport.
- **Legibility contract:** data text on the 0.72 fill; the **"Take Attendance" CTA is solid Blue Jay `#2B547E`** (white text ~6.8:1, AA) — the one opaque anchor among glass. Verify every text/glass pair at the lightest backdrop point (AA 4.5:1 body / 3:1 large).
- **Signature moves:** the solid Blue-Jay attendance **monolith**; **swipe-or-tap** attendance (fill animates in reading direction); **Bento** admin command-center (asymmetric tiles, not a 3-col grid); a reusable brand-tinted **lesson-model chip**; continuous depth (everything floats on the mesh).
- **Motion: Motion One** (~5KB, WAAPI/off-main-thread), all additive motion behind `@media (prefers-reduced-motion: no-preference)`; instructor motion fast/functional (≤200ms). GSAP only if Admin later needs timeline choreography.
- **RTL details:** selective icon mirroring (directional glyphs only), `<bdi>` for numbers/phones/times, logical scroll, progress/stepper fills start→end (right→left), validation/caret to inline-end.

---

## 10. Build order — vertical slices

Each slice = DB → service → API → Razor page → **deployed**. Not "all DBs, then all services."

- **Slice 0 — Walking skeleton (deploy day one).** Free host + Neon + migrations-on-boot + Identity (email/password **and** Google) + 4 roles + **archival global query filter** + **RTL glass base layout** + the **`/internal/jobs/tick`** endpoint wired to external cron + one authed page per role. De-risks the integration seams (Google callback, Neon pooler, RTL-vs-glass, migrations on the host) before they multiply.
- **Slice 1 — People** (Clients, Schools, Classes, Enrollment).
- **Slice 2 — Curriculum + Scheduling** (incl. `ShiftInstanceGenerator`, `Substitution_Request`).
- **Slice 3 — Operations + Lesson_Log + inline Real-Gap event** (proves the outbox→Action_Item path).
- **Slice 4 — Logistics + daily automations** (`SupplyPacingService`, dispute loop, crons).
- **Slice 5 — Action Hub** (polling first), Admin command-center, syllabus management module.

---

## 11. Testing & verification

- **xUnit** (mirrors ShiftManager). Unit tests for the workflow/automation logic (gap thresholds, double-absence window, dedup keys, TZ/DST fire times, archival filter scoping). Integration tests for auth (both login paths), the resource-based substitution check, and outbox idempotency.
- **End-to-end verification** per slice: deploy to the real free host, exercise the slice's primary path in Hebrew RTL on a mobile viewport (Playwright available), confirm the daily tick fires a job idempotently, and confirm archived data does not leak into current-year views.
- The `qa-expert` skill is available for a full P0–P4 test strategy when we reach hardening.

---

## 12. Phasing & open items (transparency)

**Deliberately phased (not silent skips):**
- Action Hub **real-time = polling first**; SignalR later if latency bothers users.
- **Full-text search** deferred (ILIKE suffices at current scale).
- A dedicated **audit table** later; base audit timestamps are in from migration one.

**Decided contrary to a reviewer (recorded):**
- **Hosting $0** chosen over the agents' unanimous ~$5/mo recommendation; mitigated via the external-cron architecture (§6). Residual accepted risk: **morning cold start**. Revisit if reliability disappoints — moving to a ~$5 always-on VPS is a config change, not a rewrite.
- **Google SSO kept** (PRD requirement) over the Architect's "drop it" suggestion; cheap via Identity.

**No items deferred without disclosure.**
