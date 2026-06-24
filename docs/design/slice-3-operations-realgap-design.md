# Orkabi — Slice 3 "Operations + Real-Gap" Liquid-Glass Design Spec

> **For:** the implementer subagents. **Status:** design spec, not production code. **Do not** treat the CSS blocks as final — they are paste-ready *proposals*; verify every token against the live `tokens.css` first.
> **Binding above this doc:** `docs/design/liquid-glass-design-system.md`, plus the **actual** `wwwroot/css/tokens.css` + `wwwroot/css/base.css` (read this session — all class/token names below were confirmed present unless flagged "NEW").
> **Restraint note:** This slice is **design-lighter** than Slices 1–2. It is standard forms + approval tables. It reuses the established vocabulary almost entirely. There are exactly **two** genuinely-new components (`.severity-chip`, `.action-card`) and one tiny chip variant (`.hours-chip`). Everything else is `.form-panel`/`.data-table`/`.status-chip`/`.segment`/`.empty-state`/`.shift-card`/`.model-chip` **as-is**.

---

## 0. Conventions that apply to every surface (carried from Slice 2 §0)

**Two scales, one shell.** Every page reuses the existing shell exactly as Slice-1/2 do:
```html
<div class="dash-shell">
  <header class="glass glass--nav dash-topbar"> … </header>   <!-- ONE blur layer, fixed/sticky -->
  <main class="dash-body"> … </main>
</div>
```
The topbar is the **only** `glass--nav` (blur) on screen for every Slice-3 surface. **No bottom sheet, no `.glass--lensed` anywhere this slice** — these are dense forms and approval lists; lensing is banned on dense surfaces (perf §10.3, legibility §5). The single hero/lensed surface remains the Slice-2 attendance sheet.

- **Instructor submission forms (Extra-Hours submit, Incident submit, Vacation submit)** = mobile-first **instructor scale**: base `--t-body` (17px), labels `--t-label` (15px), meta `--t-meta` (13px). They live in a centered `.form-panel` (which already sets `max-inline-size:560px; margin-inline:auto`), but **override the dense `--t-admin-*` inside the panel to instructor scale** via the per-surface tweaks below, so a phone-using instructor gets thumb-sized fields. (Slice-1 `.form-panel` defaults to admin scale; instructor forms bump three sizes — see NEW CSS `.form-panel--instructor`.)
- **Admin approval lists + Action-Items** = **dense admin scale**: base `--t-admin-body` (15px), `--t-admin-label` (13px), `--t-admin-meta` (12px). Reuse `.subnav`, `.data-table`, `.status-chip`, `.empty-state`, `.btn-ghost--accent` verbatim.

**Glass-tier discipline (legibility §5 / perf §10).** A page is **one glass panel** (`.glass .glass--tile` = `--lg-fill-strong` .80, the data-text-safe fill, with `--lg-tile-shadow: var(--lg-shadow)` set inline as every Slice-1/2 panel does) + the fixed topbar. Rows inside any scrolling list are **opaque tinted fills** (the `.data-row` / `.inst-row` recipe), never glass. Pills/chips/badges carry zero `backdrop-filter`. No `.glass--clear` under data text.

**RTL + numerals (§7).** Logical properties only; no `[dir=rtl]` file. Wrap every hours value, date, date-range, count in `<bdi class="num">…</bdi>`. **Days/months in Hebrew.** Date ranges wrap the **whole range in one `<bdi>`** so the en-dash/hyphen doesn't reorder: `<bdi class="num">24.06–28.06</bdi>`. Directional glyphs (breadcrumb chevron, `↗`) get `.icon-directional`; status dots, checkmarks, the severity dot **never** mirror.

**Motion (§8).** Animate **only confirmations**: an approve/reject/resolve row-swap fades the new fragment in (reuse the existing gated `lg-fade-up`), the status-chip color change *is* the confirmation, and a submit-success inline line fades. **No per-row entrance staggers** on these lists. Everything globally gated by `prefers-reduced-motion`; the one new keyframe usage reuses `lg-fade-up`.

**HTMX fragment map (what swaps, never a full reload).** Mirrors the Slice-2 substitution-row precedent exactly.

| Surface | Trigger | `hx-*` | Swapped fragment |
|---|---|---|---|
| §2 Extra-Hours approve | `אישור` button | `hx-post="?handler=Approve&id={id}"` `hx-target="#xh-row-{id}"` `hx-swap="outerHTML"` | `_ExtraHoursRow.cshtml` (row → Approved state) |
| §4 Vacation approve/reject | `אישור`/`דחייה` | `hx-post="?handler=Approve|Reject&id={id}"` `hx-target="#vac-row-{id}"` `hx-swap="outerHTML"` | `_VacationRow.cshtml` (row → Approved/Denied + approver meta) |
| §5 Action-Item resolve | `סמן כטופל` | `hx-post="?handler=Resolve&id={id}"` `hx-target="#action-{id}"` `hx-swap="outerHTML"` | `_ActionCard.cshtml` (card → Resolved state, or remove if list is "open only") |

> Precedent in repo: `People/Classes/_RosterRow.cshtml`, Slice-2 `_SubRow.cshtml`. Keep partials `_X.cshtml`, each a single swappable node with a stable `id`. Anti-forgery: global `hx-headers` on `<body>` (implementer note, not a design constraint). The submit **forms** (Extra-Hours, Incident, Vacation) are ordinary Razor Page posts (full nav + redirect-to-list with a success toast) — no HTMX needed; HTMX is only for the in-place approve/resolve swaps.

---

## 1. Operations section + subnav

**Routes:** `/Operations` (overview), `/Operations/ExtraHours`, `/Operations/Incidents`, `/Operations/Vacations`, `/Operations/ActionItems` (admin). Mirrors the People/Curriculum/Scheduling section pattern. Topbar title `תפעול`.

The subnav is **role-aware**: instructors see their *submit/my-requests* destinations; Admin/CS see the *approval* destinations. Reuse `.subnav` / `.subnav__item` exactly (it is the tinted rail, not glass, that scrolls with the body — confirmed in base.css).

```
┌─ glass glass--nav dash-topbar ─ עורקבי · תפעול · {שלום, רון | מנהל} ───────┐
  dash-body

  ┌ subnav (role-aware destinations) ───────────────────────────────────────┐
  │  [סקירה]  שעות נוספות   דיווחי אירוע   חופשות   ‹admin› אישורים           │
  └──────────────────────────────────────────────────────────────────────────┘
```

**Destinations & exact Hebrew labels**

| Audience | Subnav items (in order) |
|---|---|
| Instructor | `סקירה` · `שעות נוספות` · `דיווחי אירוע` · `חופשות` |
| Admin / CS | `סקירה` · `אישור שעות` · `דיווחי אירוע` · `אישור חופשות` · `משימות פתוחות` |

> The instructor's `שעות נוספות` / `חופשות` items land on a combined **submit-form + "my submissions" list** page (the instructor sees their own pending/approved rows beneath the form — same `.data-table`, read-only, no action column). The admin's `אישור שעות` / `אישור חופשות` land on the approval tables. `דיווחי אירוע` is shared route, role-gated content (instructor sees the submit form + their own reports; CS/Admin sees the org-wide incident list). `משימות פתוחות` (Action-Items, §5) is `AdminOnly`.

### 1a. Operations overview (`/Operations`)
Deliberately minimal — a `.hub-grid` of `.nav-card`s (the exact Slice-1 hub pattern), one card per destination, each with a `.nav-card__count` showing the live pending/open count where relevant. This seeds discoverability without inventing anything.

```
┌ glass glass--tile people-panel ──────────────────────────────────────────┐
│  ┌ nav-card ──────┐  ┌ nav-card ──────┐  ┌ nav-card ──────┐               │
│  │ ⏱               │  │ ⚠               │  │ 🏖              │               │
│  │ שעות נוספות     │  │ דיווחי אירוע    │  │ חופשות         │               │
│  │ דיווח ואישור…   │  │ תיעוד אירועים…  │  │ בקשה ואישור…   │               │
│  │ 3 ממתינות  →    │  │ 2 החודש    →    │  │ 1 ממתינה   →   │               │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘               │
│  ┌ nav-card (admin) ─┐                                                       │
│  │ ✅ משימות פתוחות   │  4 פתוחות  →                                          │
│  └────────────────────┘                                                      │
└────────────────────────────────────────────────────────────────────────────┘
```

| Element | Hebrew |
|---|---|
| Topbar title | `תפעול` |
| Card: extra-hours | title `שעות נוספות` · desc `דיווח ואישור שעות מעבר למשמרת` · count `<bdi class="num">3</bdi> ממתינות` |
| Card: incidents | title `דיווחי אירוע` · desc `תיעוד אירועים מהשטח` · count `<bdi class="num">2</bdi> החודש` |
| Card: vacations | title `חופשות` · desc `בקשה ואישור ימי חופשה` · count `<bdi class="num">1</bdi> ממתינה` |
| Card: action-items (admin) | title `משימות פתוחות` · desc `התראות מערכת הדורשות טיפול` · count `<bdi class="num">4</bdi> פתוחות` |

Markup is the verbatim Slice-1 `.hub-grid`/`.nav-card` — nothing new.

---

## 2. Extra-Hours (שעות נוספות)

**Data (spec §4):** `Extra_Hours` — `shift_instance_id`, `instructor_id`, `hours`, `reason`, `status` (Pending/Approved).

### 2a. Instructor submit form (`/Operations/ExtraHours`, role `InstructorOrAdmin`, instructor scale)

The form is tied to a **shift the instructor worked** — so the shift is chosen from the instructor's own recent shift context, not free text. The shift `<select>` is populated server-side with the instructor's recent `Shift_Instance`s (label = `{כיתה} · {יום} {date} · {time-range}`); this carries the `shift_instance_id`.

```
┌ glass glass--nav dash-topbar ─ עורקבי · תפעול · שעות נוספות ──────────────┐
  dash-body

  ┌ subnav ─ [שעות נוספות] · … ─────────────────────────────────────────────┐

  ┌ glass glass--tile form-panel form-panel--instructor ────────────────────┐
  │  דיווח שעות נוספות                                                       │
  │                                                                          │
  │  ── המשמרת ──                                                            │
  │  [ ג׳2 · יום שלישי 24 ביוני · 16:00–17:30        ▾ ]   ← shift select    │
  │                                                                          │
  │  ── מספר שעות ──                                                         │
  │  [  1.5            ] שעות                          ← number, step .5     │
  │                                                                          │
  │  ── סיבה ──                                                              │
  │  [ textarea: הכנת חומרים נוספת, הארכת מפגש…  ]                            │
  │                                                                          │
  │  ────────────────────────────────────────────────────────────────────  │
  │                                            [ ביטול ]   [ שליחת דיווח ]    │
  └──────────────────────────────────────────────────────────────────────────┘

  ── הדיווחים שלי ───────────────────────────  (section-label)

  ┌ glass glass--tile people-panel (read-only data-table, my submissions) ──┐
  │ משמרת              │ שעות   │ סיבה            │ סטטוס                     │
  │ ג׳2 · 24 ביוני     │ 1.5    │ הארכת מפגש      │ ●ממתין                    │
  │ ד׳1 · 18 ביוני     │ 2      │ הכנת חומרים     │ ●אושר                     │
  └──────────────────────────────────────────────────────────────────────────┘
```

**How it reads.** `.form-panel--instructor` (NEW — three size bumps only) so labels/fields are instructor-scale on a phone. The hours field is `type="number" step="0.5" min="0.5"` in a recessed well (reuse `.form-field input` recipe verbatim); the suffix `שעות` sits beside it via `.form-field--suffix` (NEW tiny helper — an inline flex row). The "my submissions" table reuses `.data-table` with **no actions column** for the instructor and the existing `.status-chip--pending` / `.status-chip--approved`. Hours render in `<bdi class="num">`.

**Exact Hebrew copy**

| Element | Hebrew |
|---|---|
| Topbar title | `שעות נוספות` |
| Form title | `דיווח שעות נוספות` |
| Shift label | `המשמרת *` |
| Shift placeholder | `בחרו משמרת…` |
| Shift option format | `{כיתה} · {יום} <bdi class="num">{D ב{חודש}}</bdi> · <bdi class="num">{16:00–17:30}</bdi>` |
| Hours label | `מספר שעות *` · suffix `שעות` |
| Hours hint | `אפשר חצאי שעות (לדוגמה <bdi class="num">1.5</bdi>)` |
| Reason label | `סיבה *` |
| Reason placeholder | `הכנת חומרים נוספת, הארכת מפגש…` |
| Submit / cancel | `שליחת דיווח` · `ביטול` |
| Validation: hours | `יש להזין מספר שעות (0.5 ומעלה)` |
| Validation: shift | `בחרו משמרת` |
| Validation: reason | `יש לפרט סיבה` |
| Submit success (toast) | `דיווח השעות נשלח לאישור` |
| My-submissions label | `הדיווחים שלי` |
| My-submissions columns | `משמרת` · `שעות` · `סיבה` · `סטטוס` |
| Status chips | `ממתין` (`--pending`) · `אושר` (`--approved`) |
| Empty (no submissions) | title `אין דיווחי שעות` · hint `דיווחים שתשלחו יופיעו כאן.` |

**Markup skeleton (instructor submit + my-list)**
```html
<form method="post" class="glass glass--tile form-panel form-panel--instructor"
      style="--lg-tile-shadow: var(--lg-shadow);">
  <h1 class="form-panel__title">דיווח שעות נוספות</h1>
  <div class="form-grid">
    <div class="form-field">
      <label class="form-field__label" for="ShiftInstanceId">המשמרת <span class="req">*</span></label>
      <select id="ShiftInstanceId" name="ShiftInstanceId" class="form-select">
        <option value="" disabled selected>בחרו משמרת…</option>
        <option value="12">ג׳2 · יום שלישי <bdi class="num">24 ביוני</bdi> · <bdi class="num">16:00–17:30</bdi></option>
      </select>
      <span class="form-field__error" data-valmsg-for="ShiftInstanceId"></span>
    </div>

    <div class="form-field">
      <label class="form-field__label" for="Hours">מספר שעות <span class="req">*</span></label>
      <div class="form-field--suffix">
        <input id="Hours" name="Hours" type="number" step="0.5" min="0.5" inputmode="decimal" class="num">
        <span class="form-field__suffix-text">שעות</span>
      </div>
      <span class="form-field__hint">אפשר חצאי שעות (לדוגמה <bdi class="num">1.5</bdi>)</span>
    </div>

    <div class="form-field form-field--full">
      <label class="form-field__label" for="Reason">סיבה <span class="req">*</span></label>
      <textarea id="Reason" name="Reason" placeholder="הכנת חומרים נוספת, הארכת מפגש…"></textarea>
    </div>
  </div>
  <div class="form-actions">
    <a class="btn-ghost" href="/Operations">ביטול</a>
    <button type="submit" class="btn-primary">שליחת דיווח</button>
  </div>
</form>

<div class="section-label">הדיווחים שלי</div>
<div class="glass glass--tile people-panel" style="--lg-tile-shadow: var(--lg-shadow);">
  <table class="data-table">
    <thead><tr><th>משמרת</th><th>שעות</th><th>סיבה</th><th>סטטוס</th></tr></thead>
    <tbody>
      <tr class="data-row">
        <td class="data-cell data-cell--primary">ג׳2 · <bdi class="num">24 ביוני</bdi></td>
        <td class="data-cell"><bdi class="num">1.5</bdi></td>
        <td class="data-cell">הארכת מפגש</td>
        <td class="data-cell"><span class="status-chip status-chip--pending">ממתין</span></td>
      </tr>
    </tbody>
  </table>
</div>
```

### 2b. Admin approval list (`/Operations/ExtraHours` admin view or `אישור שעות`, role `AdminOnly`, dense)

A dense `.data-table` of **Pending** rows; single `אישור` action per row → HTMX row-swap to Approved. This is structurally the Slice-2 substitution table minus the reject action.

```
┌ page-head ─ אישור שעות נוספות           [ הצג: ממתינות ▾ ] ───────────────┐
┌ glass glass--tile people-panel (data-table) ─────────────────────────────┐
│ מדריך/ה │ משמרת          │ שעות │ סיבה          │ סטטוס   │ פעולות           │
│ רון א׳  │ ג׳2 · 24 ביוני │ 1.5  │ הארכת מפגש    │ ●ממתין  │ [אישור]          │
│ מאיה ד׳ │ ד׳1 · 23 ביוני │ 2    │ הכנת חומרים   │ ●אושר   │ —                │  ← after swap
└────────────────────────────────────────────────────────────────────────────┘
```

After approval the row swaps to: hours value re-rendered with a small **`.hours-chip`** accent (NEW — a brand-tinted hours pill so the approved quantity is scannable), status `●אושר`, and an approver-meta line `אושר ע״י {שם} · {date}` in the סטטוס cell (the Slice-2 approved-meta pattern). The action cell becomes `—`.

**Exact Hebrew copy**

| Element | Hebrew |
|---|---|
| Page title | `אישור שעות נוספות` |
| Filter | `הצג:` · options `ממתינות` / `הכול` |
| Columns | `מדריך/ה` · `משמרת` · `שעות` · `סיבה` · `סטטוס` · `פעולות` |
| Status chips | `ממתין` (`--pending`) · `אושר` (`--approved`) |
| Approve action | `אישור` (`btn-ghost--accent`) |
| Approved meta (after) | `אושר ע״י {שם} · <bdi class="num">{D ב{חודש}}</bdi>` |
| No pending | title `אין שעות לאישור` · hint `דיווחים חדשים יופיעו כאן לאישור.` |

**Markup skeleton (approval row fragment `_ExtraHoursRow.cshtml`)**
```html
<tr class="data-row" id="xh-row-7">
  <td class="data-cell data-cell--primary">רון א׳</td>
  <td class="data-cell">ג׳2 · <bdi class="num">24 ביוני</bdi></td>
  <td class="data-cell"><span class="hours-chip"><bdi class="num">1.5</bdi> ש׳</span></td>
  <td class="data-cell">הארכת מפגש</td>
  <td class="data-cell"><span class="status-chip status-chip--pending">ממתין</span></td>
  <td class="data-cell data-cell--actions">
    <button class="btn-ghost btn-ghost--sm btn-ghost--accent"
            hx-post="?handler=Approve&id=7" hx-target="#xh-row-7" hx-swap="outerHTML">אישור</button>
  </td>
</tr>
```
After swap, the סטטוס cell becomes `<span class="status-chip status-chip--approved">אושר</span><span class="approve-meta">אושר ע״י דנה · <bdi class="num">24 ביוני</bdi></span>` and the actions cell `—`. (`.approve-meta` is NEW — one tiny muted-meta line; see shared additions.)

**Interaction / motion.** Approve → row swaps (`outerHTML`); the swapped row plays `lg-fade-up` (gated). The chip color change is the confirmation. Reduced-motion: instant swap. Single-action approval — no undo this slice (flagged in §7).

---

## 3. Incident-Report (דיווח אירוע)

**Data (spec §4):** `Incident_Report` — `shift_instance_id`, `instructor_id`, `severity`, `description`. **Submit-only — no approval workflow.** Severity is the one new visual primitive.

### 3a. Instructor submit form (`/Operations/Incidents`, role `InstructorOrAdmin`, instructor scale)

Severity is a **3-option `.segment`** (reuse `.segment` exactly — it already supports a radiogroup with the brand-fill selected state), low/medium/high in Hebrew. Description is a `textarea`. Tied to a shift via the same recent-shift `<select>` as §2.

```
┌ glass glass--tile form-panel form-panel--instructor ────────────────────┐
│  דיווח אירוע                                                            │
│                                                                          │
│  ── המשמרת ──                                                            │
│  [ ג׳2 · יום שלישי 24 ביוני · 16:00–17:30        ▾ ]                     │
│                                                                          │
│  ── חומרת האירוע ──                                                      │
│  [ נמוכה │ בינונית │ גבוהה ]   ← .segment (radiogroup, 3 opts)           │
│                                                                          │
│  ── תיאור האירוע ──                                                      │
│  [ textarea: מה קרה, מתי, מי מעורב, פעולות שננקטו…  ]                     │
│                                                                          │
│  ────────────────────────────────────────────────────────────────────  │
│                                            [ ביטול ]   [ שליחת דיווח ]    │
└──────────────────────────────────────────────────────────────────────────┘
```

> **Severity in the form = `.segment` (not the severity chip).** The chip is the *read* token (lists). The form uses the segment for input, consistent with how In_Progress/Completed and Active/Archived use `.segment` elsewhere. The selected segment option carries the brand fill (existing behavior); we do **not** color the segment by severity hue (that would fight the segment's single-fill design) — the *chip* in the list carries the hue.

### 3b. Admin / CS incident list (shared route, role `CsOrAdmin` content, dense)

A `.data-table` of incident rows: instructor, shift, **severity chip** (NEW `.severity-chip` with three hued variants), description (truncated with full text on the row), date. **No actions** — submit-only, read surface.

```
┌ page-head ─ דיווחי אירוע            [ חומרה: הכול ▾ ] ────────────────────┐
┌ glass glass--tile people-panel (data-table) ─────────────────────────────┐
│ מדריך/ה │ משמרת          │ חומרה        │ תיאור                  │ תאריך    │
│ רון א׳  │ ג׳2 · 24 ביוני │ ⬤ גבוהה      │ תלמיד נחבל קלות ביד…   │ 24 ביוני │
│ מאיה ד׳ │ ד׳1 · 23 ביוני │ ⬤ בינונית    │ ויכוח בין שני תלמידים… │ 23 ביוני │
│ נועה ב׳ │ ה׳3 · 22 ביוני │ ⬤ נמוכה      │ חוסר בחומרי יצירה…     │ 22 ביוני │
└────────────────────────────────────────────────────────────────────────────┘
```

**Exact Hebrew copy**

| Element | Hebrew |
|---|---|
| Topbar / page title | `דיווחי אירוע` |
| Form title | `דיווח אירוע` |
| Shift label / placeholder | `המשמרת *` · `בחרו משמרת…` |
| Severity label | `חומרת האירוע *` |
| Severity options (segment) | `נמוכה` · `בינונית` · `גבוהה` |
| Description label | `תיאור האירוע *` |
| Description placeholder | `מה קרה, מתי, מי מעורב, פעולות שננקטו…` |
| Submit / cancel | `שליחת דיווח` · `ביטול` |
| Submit success (toast) | `הדיווח נשלח` |
| Validation: severity | `בחרו את חומרת האירוע` |
| Validation: description | `יש לתאר את האירוע` |
| List filter | `חומרה:` · options `הכול` / `נמוכה` / `בינונית` / `גבוהה` |
| List columns | `מדריך/ה` · `משמרת` · `חומרה` · `תיאור` · `תאריך` |
| Severity chips (read) | `נמוכה` · `בינונית` · `גבוהה` |
| Empty (instructor) | title `אין דיווחים` · hint `דיווחים שתשלחו יופיעו כאן.` |
| Empty (admin/CS) | title `אין דיווחי אירוע` · hint `דיווחים מהמדריכים יופיעו כאן.` |

**Markup skeleton (submit segment + list row)**
```html
<!-- submit: severity as a 3-option segment -->
<div class="form-field form-field--full">
  <span class="form-field__label">חומרת האירוע <span class="req">*</span></span>
  <div class="segment" role="radiogroup" aria-label="חומרת האירוע">
    <label class="segment__opt"><input type="radio" name="Severity" value="Low"><span>נמוכה</span></label>
    <label class="segment__opt"><input type="radio" name="Severity" value="Medium"><span>בינונית</span></label>
    <label class="segment__opt"><input type="radio" name="Severity" value="High"><span>גבוהה</span></label>
  </div>
</div>
<div class="form-field form-field--full">
  <label class="form-field__label" for="Description">תיאור האירוע <span class="req">*</span></label>
  <textarea id="Description" name="Description" placeholder="מה קרה, מתי, מי מעורב, פעולות שננקטו…"></textarea>
</div>

<!-- list row: severity as a hued read-chip -->
<tr class="data-row">
  <td class="data-cell data-cell--primary">רון א׳</td>
  <td class="data-cell">ג׳2 · <bdi class="num">24 ביוני</bdi></td>
  <td class="data-cell"><span class="severity-chip severity-chip--high">גבוהה</span></td>
  <td class="data-cell">תלמיד נחבל קלות ביד במהלך פעילות</td>
  <td class="data-cell"><bdi class="num">24 ביוני</bdi></td>
</tr>
```

---

## 4. Vacation-Request (בקשת חופשה)

**Data (spec §4):** `Vacation_Request` — `instructor_id`, `start_date`, `end_date`, `status` (Pending/Approved/Denied). **Single-approval.** This is the exact analogue of the Slice-2 substitution approval (Pending → Approved/Denied, with approver+date after decision).

### 4a. Instructor submit form (`/Operations/Vacations`, role `InstructorOrAdmin`, instructor scale)

Two date inputs (`type="date"`, recessed well, `<bdi class="num">` rendering) + optional reason. The instructor's own request history sits below in a read-only `.data-table` (with the live status chip).

```
┌ glass glass--tile form-panel form-panel--instructor ────────────────────┐
│  בקשת חופשה                                                             │
│                                                                          │
│  ┌ form-grid--2col ────────────────────────────────────────────────┐    │
│  │ ── מתאריך ──            │ ── עד תאריך ──                          │    │
│  │ [ 24.06.2026   📅 ]     │ [ 28.06.2026   📅 ]                     │    │
│  └──────────────────────────────────────────────────────────────────┘    │
│  ── סיבה (לא חובה) ──                                                    │
│  [ textarea: אירוע משפחתי, מחלה…  ]                                       │
│                                                                          │
│  ────────────────────────────────────────────────────────────────────  │
│                                            [ ביטול ]   [ שליחת בקשה ]     │
└──────────────────────────────────────────────────────────────────────────┘

  ── הבקשות שלי ──
  ┌ data-table ─ טווח │ סיבה │ סטטוס ───────────────────────────────────────┐
  │ 24.06–28.06 │ אירוע משפחתי │ ●ממתין                                       │
  │ 10.05–12.05 │ —            │ ●אושר · אושר ע״י דנה · 02 במאי              │
  └──────────────────────────────────────────────────────────────────────────┘
```

> **Date inputs:** native `type="date"` in the recessed well (reuse `.form-field input` recipe). The value text renders LTR-isolated; wrap any *displayed* (non-input) date/range in `<bdi class="num">`. The 2-col grid (`.form-grid--2col`) already collapses to 1 column < 768px (confirmed in base.css responsive block). Validation: end ≥ start.

### 4b. Admin approval list (`/Operations/Vacations` admin or `אישור חופשות`, role `AdminOnly`, dense, single-approval)

The Slice-2 substitution table, retargeted. Columns: instructor, date-range, reason, status, actions. Pending rows get `אישור` (accent) / `דחייה` (ghost) → HTMX row-swap. After decision the row shows `●אושר`/`●נדחה` + the `אושר ע״י {שם} · {date}` / `נדחה ע״י {שם} · {date}` meta (the substitution precedent).

```
┌ page-head ─ אישור חופשות           [ הצג: ממתינות ▾ ] ────────────────────┐
┌ glass glass--tile people-panel (data-table) ─────────────────────────────┐
│ מדריך/ה │ טווח          │ ימים │ סיבה          │ סטטוס   │ פעולות           │
│ רון א׳  │ 24.06–28.06   │ 5    │ אירוע משפחתי  │ ●ממתין  │ [אישור][דחייה]   │
│ מאיה ד׳ │ 10.05–12.05   │ 3    │ —             │ ●אושר · אושר ע״י דנה · 02 במאי │
└────────────────────────────────────────────────────────────────────────────┘
```

**Exact Hebrew copy**

| Element | Hebrew |
|---|---|
| Topbar / page title | `חופשות` (instructor) · `אישור חופשות` (admin) |
| Form title | `בקשת חופשה` |
| Start / end labels | `מתאריך *` · `עד תאריך *` |
| Reason label / placeholder | `סיבה (לא חובה)` · `אירוע משפחתי, מחלה…` |
| Submit / cancel | `שליחת בקשה` · `ביטול` |
| Validation: dates | `תאריך הסיום חייב להיות אחרי תאריך ההתחלה` |
| Validation: required | `יש לבחור תאריכים` |
| Submit success (toast) | `בקשת החופשה נשלחה לאישור` |
| My-requests label | `הבקשות שלי` |
| Columns (instructor list) | `טווח` · `סיבה` · `סטטוס` |
| Columns (admin list) | `מדריך/ה` · `טווח` · `ימים` · `סיבה` · `סטטוס` · `פעולות` |
| Date range cell | `<bdi class="num">{DD.MM}–{DD.MM}</bdi>` (whole range in one `<bdi>`) |
| Days count | `<bdi class="num">{N}</bdi>` |
| Status chips | `ממתין` (`--pending`) · `אושר` (`--approved`) · `נדחה` (`--rejected`) |
| Approve / reject | `אישור` (accent) · `דחייה` (ghost) |
| Approved/denied meta (after) | `אושר ע״י {שם} · <bdi class="num">{D ב{חודש}}</bdi>` · `נדחה ע״י {שם} · <bdi class="num">{D ב{חודש}}</bdi>` |
| No pending | title `אין בקשות חופשה ממתינות` · hint `בקשות חדשות יופיעו כאן לאישור.` |
| Empty (instructor) | title `אין בקשות חופשה` · hint `בקשות שתשלחו יופיעו כאן.` |

**Markup skeleton (approval row fragment `_VacationRow.cshtml`)**
```html
<tr class="data-row vac-row" id="vac-row-3">
  <td class="data-cell data-cell--primary">רון א׳</td>
  <td class="data-cell"><bdi class="num">24.06–28.06</bdi></td>
  <td class="data-cell"><bdi class="num">5</bdi></td>
  <td class="data-cell">אירוע משפחתי</td>
  <td class="data-cell"><span class="status-chip status-chip--pending">ממתין</span></td>
  <td class="data-cell data-cell--actions">
    <button class="btn-ghost btn-ghost--sm btn-ghost--accent"
            hx-post="?handler=Approve&id=3" hx-target="#vac-row-3" hx-swap="outerHTML">אישור</button>
    <button class="btn-ghost btn-ghost--sm"
            hx-post="?handler=Reject&id=3" hx-target="#vac-row-3" hx-swap="outerHTML">דחייה</button>
  </td>
</tr>
```
After **approve** swap, סטטוס cell → `<span class="status-chip status-chip--approved">אושר</span><span class="approve-meta">אושר ע״י דנה · <bdi class="num">24 ביוני</bdi></span>`, actions → `—`. After **reject**, `status-chip--rejected` "נדחה" + `נדחה ע״י …` meta. (Reuses `.approve-meta` from §2b.)

**Interaction / motion.** Identical to the Slice-2 sub-row: row swaps, chip color is the confirmation, `lg-fade-up` gated, reduced-motion instant.

---

## 5. Minimal Action-Item / gaps view (Admin)

**Data (spec §4):** `Action_Item` — `type` (Absence/Gap/Dispute/Task/Birthday/Tryout_Followup), `status` (Open/Resolved), `assigned_to_role`/`assigned_to_user_id`, `related_entity_id`, `description`, `due_date`, `deduplication_key`. **Workflow §5A:** the Real-Gap monitor creates an Admin `Action_Item` (type=Gap) via the outbox→drainer path when a model overruns `expected+1`.

> **SCOPE — read this.** The **rich Action Hub** (live polling, all types as a unified inbox, ticketing/assignment, dispute loop, command-center bento) is **Slice 5** (per the build plan: "Slice 5 — Action Hub (polling first)"). **Slice 3 designs only a MINIMAL admin read** so the outbox→Action_Item path proven in this slice has somewhere to surface. Keep it deliberately simple. It is intentionally a stepping stone that the Slice-5 hub absorbs and replaces.

**Route:** `/Operations/ActionItems`, role `AdminOnly`, dense. A simple **list of cards** (`.action-card`, NEW) of **Open** items, each with a type badge, description, due date, status, and a single `סמן כטופל` (resolve) action that HTMX-swaps the card to Resolved (or removes it from an "open-only" list).

> **Card not table — why this earns its one new component.** Action-Items are heterogeneous (variable-length descriptions, a type badge, a due date, a resolve action) and read more like a short feed than tabular data — the existing dense `.data-table` doesn't fit a description-led item well, and the `.feed-item` (bento) is too compact/borderless for a standalone resolvable ticket. A lightweight bordered card is the right read. It is **opaque tinted** (the panel is the glass once), zero `backdrop-filter`.

```
┌ page-head ─ משימות פתוחות          [ הצג: פתוחות ▾ ] ─────────────────────┐
│  ⓘ זהו תצוגה מצומצמת — מרכז הפעולות המלא יגיע בהמשך.                      │  ← Slice-5 note (UI hint)
┌ glass glass--tile people-panel ──────────────────────────────────────────┐
│  ┌ action-card ─ type=Gap ─────────────────────────────────────────────┐ │
│  │ [⬤ חריגת קצב]                              יעד: 26 ביוני   ●פתוח      │ │
│  │ כיתה ג׳2 · דגם "קופסת אוצרות": בוצעו 9 שיעורים מתוך 8 צפויים.         │ │
│  │                                              [ סמן כטופל ]            │ │
│  └──────────────────────────────────────────────────────────────────────┘ │
│  ┌ action-card ─ type=Task ────────────────────────────────────────────┐ │
│  │ [⬤ משימה]                                  יעד: 28 ביוני   ●פתוח      │ │
│  │ לעדכן את רשימת החומרים לכיתה ד׳1.                                     │ │
│  │                                              [ סמן כטופל ]            │ │
│  └──────────────────────────────────────────────────────────────────────┘ │
│  ┌ action-card is-resolved ─ (after swap) ─────────────────────────────┐ │
│  │ [⬤ חריגת קצב]                              ✓ טופל · ע״י דנה          │ │
│  │ כיתה ה׳3 · דגם "מסגרת פסיפס"…                                         │ │
│  └──────────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────┘
```

**Type badge.** `.action-type` (NEW, a small pill) with a per-type Hebrew label + a dot color. For Slice 3, Gap is the only type the monitor produces, but the badge supports the full enum so Slice 5 inherits it. Type→Hebrew→accent:

| `type` | Hebrew | Dot/accent |
|---|---|---|
| Gap | `חריגת קצב` | `--warn` |
| Absence | `היעדרות` | `--absent` |
| Dispute | `מחלוקת` | `--absent` |
| Task | `משימה` | `--brand` |
| Birthday | `יום הולדת` | `--brand-tint` |
| Tryout_Followup | `מעקב ניסיון` | `--warn` |

**Status.** `status-chip--pending`-family won't read right semantically here (Open ≠ "awaiting approval"); use the **existing** `.status-chip--active` for `פתוח` (its `--ok` dot reads as "live/open") and `.status-chip--muted` (exists) for `טופל`/Resolved — **no new status chip needed**. After resolve the card gets `.action-card.is-resolved` (dims + a `✓ טופל · ע״י {שם}` meta replaces the action).

**Exact Hebrew copy**

| Element | Hebrew |
|---|---|
| Page title | `משימות פתוחות` |
| Slice-5 scope note (UI hint) | `זוהי תצוגה מצומצמת — מרכז הפעולות המלא יגיע בהמשך.` |
| Filter | `הצג:` · options `פתוחות` / `הכול` |
| Type badges | `חריגת קצב` · `היעדרות` · `מחלוקת` · `משימה` · `יום הולדת` · `מעקב ניסיון` |
| Due date | `יעד: <bdi class="num">{D ב{חודש}}</bdi>` |
| Status (open) | `פתוח` |
| Resolve action | `סמן כטופל` |
| Resolved meta (after) | `✓ טופל · ע״י {שם}` |
| Gap description (example, server-generated) | `כיתה {כיתה} · דגם "{דגם}": בוצעו <bdi class="num">{X}</bdi> שיעורים מתוך <bdi class="num">{N}</bdi> צפויים.` |
| Empty | title `אין משימות פתוחות` · hint `כשתיווצר התראה היא תופיע כאן.` |

**Markup skeleton (`_ActionCard.cshtml`)**
```html
<article class="action-card" id="action-15">
  <div class="action-card__top">
    <span class="action-type action-type--gap">
      <span class="action-type__dot" aria-hidden="true"></span>חריגת קצב
    </span>
    <span class="action-card__due">יעד: <bdi class="num">26 ביוני</bdi></span>
    <span class="status-chip status-chip--active">פתוח</span>
  </div>
  <p class="action-card__desc">
    כיתה ג׳2 · דגם "קופסת אוצרות": בוצעו <bdi class="num">9</bdi> שיעורים מתוך <bdi class="num">8</bdi> צפויים.
  </p>
  <div class="action-card__actions">
    <button class="btn-ghost btn-ghost--sm btn-ghost--accent"
            hx-post="?handler=Resolve&id=15" hx-target="#action-15" hx-swap="outerHTML">סמן כטופל</button>
  </div>
</article>
```
After resolve swap: `<article class="action-card is-resolved" id="action-15">` with the status chip → `.status-chip--muted` "טופל", and `.action-card__actions` replaced by `<span class="action-card__resolved-meta">✓ טופל · ע״י דנה</span>`.

**Interaction / motion.** Resolve → card swaps; in an "open-only" filter the server returns an empty fragment and the card animates out (reuse `lg-fade-up` reversed is overkill — just let it disappear; reduced-motion instant). In "הכול" it swaps to the dimmed `.is-resolved` state. The status/dim change is the confirmation. No polling this slice (Slice 5).

---

## 6. NEW CSS — shared additions to `base.css` (consolidated, paste-ready)

> Append after the Slice-2 blocks, **before** the `@supports not (...)` fallback (and add the noted line into that fallback). **No new tokens required** — implementer must verify each used token exists in the live `tokens.css` (`--warn`/`--warn-soft`, `--absent`/`--absent-soft`, `--ok`/`--ok-soft`, `--brand`/`--brand-rgb`/`--brand-tint`, `--lg-fill-tint`, `--radius-pill`/`--radius-chip`/`--radius-card`, `--t-body`/`--t-label`/`--t-meta`, `--t-admin-*`, `--sp-*`, `--dur-*`/`--ease-*`, `--fw-*`, `--ls-*`). If any is missing, STOP and flag — do not invent.
> This is **small** — five additions: an instructor-scale form modifier, a suffixed field row, the hours-chip, the approve-meta line, the severity-chip (3 variants), and the action-card + action-type. That is the entire CSS footprint of the slice.

```css
/* ============================================================
   SLICE 3 — Operations + Real-Gap (minimal)
   Reuses verbatim: .subnav, .page-head, .form-panel/.form-grid(+--2col)/
   .form-field/.form-select, .segment, .data-table(+.data-row/.data-cell/
   .data-cell--actions), .status-chip(+--pending/--approved/--rejected/
   --active/--muted), .btn-primary/.btn-ghost(+--sm/--accent), .empty-state,
   .nav-card/.hub-grid, .section-label, .num.
   Perf: topbar is the only blur; panels are glass once; rows/cards opaque.
   NO .glass--lensed, NO .glass--clear under data text.
   ============================================================ */

/* ---- Instructor-scale form modifier — bumps the dense .form-panel up to
        instructor sizes for the three submit forms (Extra-Hours, Incident,
        Vacation). Three size overrides only; everything else inherited. ---- */
.form-panel--instructor .form-panel__title { font-size: var(--t-title); }
.form-panel--instructor .form-field__label { font-size: var(--t-label); text-transform: none; letter-spacing: var(--ls-normal); }
.form-panel--instructor .form-field input,
.form-panel--instructor .form-field select,
.form-panel--instructor .form-field textarea,
.form-panel--instructor .form-select { font-size: var(--t-body); }
.form-panel--instructor .form-field__hint,
.form-panel--instructor .form-field__error { font-size: var(--t-meta); }
.form-panel--instructor .segment__opt span { font-size: var(--t-label); }

/* ---- Field with an inline trailing unit (e.g. hours · "שעות") ---- */
.form-field--suffix { display: flex; align-items: center; gap: var(--sp-3); }
.form-field--suffix input { inline-size: auto; flex: 1; min-inline-size: 0; }
.form-field__suffix-text {
  flex: 0 0 auto; font-size: var(--t-label); font-weight: var(--fw-medium);
  color: var(--brand-ink-muted);
}

/* ---- Hours chip — brand-tinted read pill for approved hours (scan aid).
        Flat tint, zero backdrop-filter (lives in scrolling tables). ---- */
.hours-chip {
  display: inline-flex; align-items: center; gap: var(--sp-1);
  padding-block: 2px; padding-inline: var(--sp-2);
  border-radius: var(--radius-pill);
  background: var(--lg-fill-tint); border: 1px solid rgba(var(--brand-rgb), .18);
  font-size: var(--t-admin-meta); font-weight: var(--fw-semibold); color: var(--brand);
}

/* ---- Approver/decider meta line — appears in a status cell after a decision
        (Extra-Hours approve, Vacation approve/reject). Slice-2 precedent. ---- */
.approve-meta {
  display: block; margin-block-start: 2px;
  font-size: var(--t-admin-meta); color: var(--brand-ink-muted);
}

/* ============================================================
   §3 Severity chip (READ token; the form uses .segment instead)
   Three hued variants. Hue is NOT the only signal — the Hebrew label
   carries meaning too (accessibility: not color-alone). Dot before label.
   ============================================================ */
.severity-chip {
  display: inline-flex; align-items: center; gap: var(--sp-1);
  font-size: var(--t-admin-meta); font-weight: var(--fw-semibold);
  padding-block: 2px; padding-inline: var(--sp-2); border-radius: var(--radius-pill);
}
.severity-chip::before { content: "⬤"; font-size: 8px; line-height: 1; }
.severity-chip--low    { background: var(--ok-soft);     color: var(--ok); }
.severity-chip--medium { background: var(--warn-soft);   color: var(--warn); }
.severity-chip--high   { background: var(--absent-soft); color: var(--absent); }

/* ============================================================
   §5 Action-Item minimal card (SLICE-3 stepping stone; Slice-5 hub
   replaces this). Opaque tinted card — the panel is the glass once.
   ============================================================ */
.action-card {
  display: flex; flex-direction: column; gap: var(--sp-3);
  padding-block: var(--sp-4); padding-inline: var(--sp-5);
  background: rgba(var(--brand-rgb), .04);
  border: 1px solid rgba(var(--brand-rgb), .10);
  border-radius: var(--radius-card);
  transition: opacity var(--dur-fast) var(--ease-glass),
              background var(--dur-fast) var(--ease-glass);
}
.action-card + .action-card { margin-block-start: var(--sp-3); }
.action-card__top {
  display: flex; align-items: center; gap: var(--sp-3); flex-wrap: wrap;
}
.action-card__due {
  margin-inline-start: auto;            /* push due + status to the trailing edge */
  font-size: var(--t-admin-meta); color: var(--brand-ink-muted);
}
.action-card__desc {
  margin: 0; font-size: var(--t-admin-body); color: var(--brand-ink);
  line-height: var(--lh-snug);
}
.action-card__actions { display: flex; justify-content: flex-end; }
.action-card__resolved-meta {
  font-size: var(--t-admin-meta); font-weight: var(--fw-semibold); color: var(--ok);
}
/* resolved state — dimmed, inert */
.action-card.is-resolved { opacity: .6; background: rgba(var(--brand-rgb), .03); }

/* ---- Action type badge (full enum supported; Slice 3 emits only Gap) ---- */
.action-type {
  display: inline-flex; align-items: center; gap: var(--sp-2); flex: 0 0 auto;
  padding-block: 2px; padding-inline: var(--sp-3); border-radius: var(--radius-pill);
  font-size: var(--t-admin-meta); font-weight: var(--fw-semibold);
  background: rgba(var(--brand-rgb), .08); color: var(--brand);
  border: 1px solid rgba(var(--brand-rgb), .14);
}
.action-type__dot { inline-size: 7px; block-size: 7px; border-radius: 50%;
  background: var(--brand); flex: 0 0 auto; }
.action-type--gap     { background: var(--warn-soft);   color: var(--warn);   border-color: rgba(200,134,27,.28); }
.action-type--gap     .action-type__dot { background: var(--warn); }
.action-type--absence,
.action-type--dispute { background: var(--absent-soft); color: var(--absent); border-color: rgba(178,58,72,.26); }
.action-type--absence .action-type__dot,
.action-type--dispute .action-type__dot { background: var(--absent); }
.action-type--tryout  { background: var(--warn-soft);   color: var(--warn);   border-color: rgba(200,134,27,.28); }
.action-type--tryout  .action-type__dot { background: var(--warn); }
.action-type--birthday .action-type__dot { background: var(--brand-tint); }
/* Task uses the .action-type base (brand) — no modifier needed */

/* ---- Scope-note hint line (the Slice-5 "minimal view" disclosure) ---- */
.scope-note {
  display: flex; align-items: center; gap: var(--sp-2);
  margin-block-end: var(--sp-4);
  font-size: var(--t-admin-meta); color: var(--brand-ink-muted);
}

/* ---- Responsive: instructor forms + action cards on mobile ---- */
@media (max-width: 767px) {
  .form-field--suffix { flex-wrap: nowrap; }
  .action-card__due { margin-inline-start: 0; inline-size: 100%; }
}
```

**Add into the existing `@supports not (...)` fallback block** (the action-card/severity-chip/hours-chip carry no `backdrop-filter`, so they need nothing; only confirm the instructor form fields inherit the existing fallback — they already do via the `.form-field input/select/textarea, .form-select` line already present, so **no new fallback lines are required**). Nothing to add — flagged for the implementer to confirm rather than blindly append.

---

## 7. Legibility & perf self-check (contract §5 / budget §10)

- **Blur budget:** every Slice-3 page = topbar (1 blur) + one `.glass--tile` panel (2). Lists/tables/cards scroll opaque; chips/badges zero `backdrop-filter`. **No `.glass--lensed`, no `.glass--clear` under data text.** ✔ §10.1/10.2/10.3, §5.1
- **Data text on safe fill:** all data tables/cards on `--lg-fill-strong` (.80) panels; instructor form fields on the recessed-well tinted fill (existing, AA-cleared in Slice 1). ✔ §5.1
- **Not color-alone:** severity chips carry the Hebrew label + dot (not hue only); status chips carry label + glyph; action-type carries label + dot. ✔ accessibility
- **RTL:** logical properties only; hours/days/dates/ranges in `<bdi class="num">` (ranges in one `<bdi>` so the dash doesn't reorder); Hebrew months/days; no glyph mirroring beyond `.icon-directional`. ✔ §7
- **Motion:** confirmations only (row/card swaps via gated `lg-fade-up`; chip color = confirmation); no entrance staggers; reduced-motion inherited. ✔ §8
- **Reuse vs invention:** 5 small additions total (`.form-panel--instructor`, `.form-field--suffix`, `.hours-chip`, `.approve-meta`, `.severity-chip`, `.action-card`+`.action-type`+`.scope-note`). Status chips, segment, data-table, form-panel, empty-state, nav-card, model-chip all reused verbatim. ✔ restraint

---

## 8. Deferred / out-of-scope (designer-flagged, per global rule §5)

These are explicit, not silent:

- **Rich Action Hub → Slice 5 (NOT deferred by me — it is *scoped* to Slice 5 by the build plan).** Slice 3 ships only the minimal Open-Action-Item card read (§5). Polling/live-refresh, the full type inbox, assignment/ticketing, the dispute loop, and the command-center bento are Slice 5. I designed the `.action-type` badge to support the **full enum** now so Slice 5 inherits it. Flagged so no one expects the hub this slice.
- **Extra-Hours / Vacation undo after a decision** — single-action approve (and approve/reject for vacation) with **no undo** this slice, matching the Slice-2 substitution approval (which also has no undo). If an undo/toast-undo is wanted (like the Slice-1 un-enroll toast), say so — it's a small addition, not built here.
- **Incident severity = enum mapping** — I specced 3 levels (`Low`/`Medium`/`High` → `נמוכה`/`בינונית`/`גבוהה`) because the entity stores a single `severity`. If the domain model uses a different scale (e.g. 4 levels), the `.segment` gains/loses an option and `.severity-chip` gains a variant — flag if the enum differs.
- **Shift `<select>` population for submit forms** — the design assumes the form is server-fed with the instructor's recent `Shift_Instance`s. The query (how far back, which statuses) is a server/architecture concern; I did not design it. The *form binding* (`ShiftInstanceId`) and option label format are fixed.
- **Vacation "days" count** — the admin table shows a `ימים` column; whether it's calendar days vs working days is a business-rule/server concern. UI just renders the server's `N`.
- **Operations overview live counts** — the `.nav-card__count`s (`3 ממתינות` etc.) are designed; the count queries are server-side and not designed here.
- **No HTMX on the submit forms** — by design (full Razor post + redirect + toast). Only approve/reject/resolve use HTMX row/card swaps. Flagged so the implementer doesn't HTMX-ify the forms unnecessarily.

**Everything else in the brief is specified. No other items deferred.**
