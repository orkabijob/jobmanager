# Task 8 Report — Scheduling Admin Pages

## Pages + HTMX handlers built

| Page | Route | Auth | HTMX |
|------|-------|------|------|
| Scheduling/Index | GET /Scheduling | CsOrAdmin | — |
| Scheduling/Templates/Index | GET /Scheduling/Templates | CsOrAdmin | — |
| Scheduling/Templates/Create | GET+POST /Scheduling/Templates/Create | CsOrAdmin | — |
| Scheduling/Templates/Edit | GET+POST /Scheduling/Templates/Edit/{id} | CsOrAdmin | — |
| Scheduling/Instances/Index | GET /Scheduling/Instances | CsOrAdmin | OnPostGenerateAsync → partial _InstanceList |
| Scheduling/Substitutions/Index | GET /Scheduling/Substitutions | Admin | OnPostApproveAsync + OnPostRejectAsync → partial _SubRow |

Partials: `_InstanceList.cshtml` (full panel swap), `_SubRow.cshtml` (row-level swap via `hx-target="#sub-row-{id}"`).

## How instructor selects are sourced

`UserManager<AppUser>.GetUsersInRoleAsync(AppRoles.Instructor)` — injected via constructor into Create/Edit page models. Renders as `SelectListItem(FullName ?? Email ?? UserName, id)`.

## RED → GREEN test summary

| Test | RED reason | GREEN fix |
|------|-----------|-----------|
| Anonymous_redirected_from_scheduling | 404 (no page) | Created Index.cshtml with [Authorize] |
| Cs_user_can_open_scheduling_templates | 404 (no page) | Created Templates/Index.cshtml |
| Instructor_forbidden_from_substitutions | 404 (no page) | Created Substitutions/Index with [Authorize(Roles=Admin)] |
| Creating_template_generates_instances | 404 on Templates/Create | Created Create page; template creation triggers generator |
| Approving_substitution_swaps_row_and_sets_actual_instructor | AntiForgery token not found in HTML | Added hidden `<form id="csrf-anchor">@Html.AntiForgeryToken()</form>` to Substitutions/Index |

One compile error during implementation: `IndexModel.DayName` is static — referenced as `@Model.DayName(...)` in cshtml which the compiler rejected. Fixed to use fully-qualified `IndexModel.DayName(...)`.

## base.css additions

Inserted `§5 Scheduling` block before the `@supports not backdrop-filter` fallback block (was line 1262). Added:
- `.date-group-head`, `.inst-list`, `.inst-row`, `.inst-row__class/__time/__who`
- `.status-chip--pending`, `.status-chip--approved`, `.status-chip--rejected`
- `.status-chip--muted` (Detached "נערך ידנית" chip; muted bg + hollow `◦` bullet, matching the existing `--archived/--off` muted style) — added during review after finding the chip class referenced in `_InstanceList.cshtml` was undefined.
- Mobile `@media` wrap rule for `.inst-row`

## Files changed

```
src/Orkabi.Web/Pages/Scheduling/Index.cshtml
src/Orkabi.Web/Pages/Scheduling/Index.cshtml.cs
src/Orkabi.Web/Pages/Scheduling/Templates/Index.cshtml
src/Orkabi.Web/Pages/Scheduling/Templates/Index.cshtml.cs
src/Orkabi.Web/Pages/Scheduling/Templates/Create.cshtml
src/Orkabi.Web/Pages/Scheduling/Templates/Create.cshtml.cs
src/Orkabi.Web/Pages/Scheduling/Templates/Edit.cshtml
src/Orkabi.Web/Pages/Scheduling/Templates/Edit.cshtml.cs
src/Orkabi.Web/Pages/Scheduling/Instances/Index.cshtml
src/Orkabi.Web/Pages/Scheduling/Instances/Index.cshtml.cs
src/Orkabi.Web/Pages/Scheduling/Instances/_InstanceList.cshtml
src/Orkabi.Web/Pages/Scheduling/Substitutions/Index.cshtml
src/Orkabi.Web/Pages/Scheduling/Substitutions/Index.cshtml.cs
src/Orkabi.Web/Pages/Scheduling/Substitutions/_SubRow.cshtml
src/Orkabi.Web/wwwroot/css/base.css
tests/Orkabi.Web.Tests/SchedulingPagesTests.cs
.superpowers/sdd/task-8-report.md
```

## Self-review

- All service methods called verified against `SchedulingService.cs` before use — no invented methods.
- `Edit.cshtml.cs` loads template via `_db.ShiftTemplates.IgnoreQueryFilters()` so archived templates remain editable.
- After approve/reject the handler re-loads via `LoadRequestAsync` with `IgnoreQueryFilters()` so the approved request (no longer "pending") is found and the correct status chip is returned.
- `TimeOnly` model binding from `<input type="time">` works natively in .NET 8 — no string workaround needed.
- Final test run: 78/78 passed (73 original + 5 new).

## Concerns

- Hidden `<form id="csrf-anchor">` on the Substitutions page exists to expose the antiforgery token for the `AntiForgery.Extract` test helper. In production HTMX reads from the `<meta name="htmx-csrf">` tag injected by `_Layout.cshtml`. The form is harmless.
- `_InstanceList.cshtml` compares `inst.ActualInstructorId != inst.Template.DefaultInstructorId` (nullable int vs int) — works correctly; null produces false (no substitution indicator).

---

## Review Fix Pass — 2026-06-24

### Finding 1 (Important): Approved-substitution audit meta — `_SubRow.cshtml`
- Added `@using Orkabi.Web.Shared` to bring `IsraelClock` into scope.
- In the `else if (Approved)` branch of the actions cell, replaced the plain "אושר" text span with the design §5 pattern: `אושר ע״י {name} · <bdi class="num">{dd.MM.yyyy}</bdi>`.
- `ApprovedAt` (UTC `DateTime?`) is converted via `TimeZoneInfo.ConvertTimeFromUtc(..., IsraelClock.IsraelTz)` before formatting.
- `ApprovedByUser?.FullName` falls back to `"—"` if nav not loaded; `ApprovedAt` null also falls back to `"—"`.
- Denied/rejected row shows a plain `נדחה` text span (no change needed — status chip in the Status column already shows it).

### Finding 2 (Important): Date-group header RTL numerals — `_InstanceList.cshtml`
- Replaced the single `ToString("dddd, d MMMM yyyy", he-IL)` call with two calls: `"dddd"` for the Hebrew weekday name, and `"d MMMM"` for the day+month.
- Only the day+month portion is wrapped in `<bdi class="num">` per design §0/§5.
- Year is omitted from the group header (spec says weekday + day+month only).
- Variable declarations placed inline inside the `@foreach` block (no nested `@{}` — Razor already in code context).

### Finding 3 (Minor): Page title — `Instances/Index.cshtml`
- Changed both `ViewData["Title"]` and `<h1>` from `מופעי משמרות` to `מופעי משמרת` (singular, per spec).

### Finding 4 (Minor): Empty-state copy alignment
- `_InstanceList.cshtml`: title `אין מופעים בטווח`, hint `צרו מופעים מהתבניות הקיימות.`
- `Substitutions/Index.cshtml`: hint changed to `בקשות חדשות יופיעו כאן לאישור.` (title unchanged: `אין בקשות החלפה ממתינות`).

### Test results
- `dotnet build` — 0 warnings, 0 errors.
- `dotnet test --filter SchedulingPagesTests` — Passed: 5/5.
- `dotnet test` (full suite) — Passed: 78/78.
