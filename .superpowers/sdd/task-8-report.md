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

Inserted `§5 Scheduling` block (~30 lines) before the `@supports not backdrop-filter` fallback block (was line 1262). Added:
- `.date-group-head`, `.inst-list`, `.inst-row`, `.inst-row__class/__time/__who`
- `.status-chip--pending`, `.status-chip--approved`, `.status-chip--rejected`
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
