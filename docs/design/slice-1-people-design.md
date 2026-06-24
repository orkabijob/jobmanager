# Orkabi — Slice 1 "People" Liquid-Glass Design Spec

> **For:** the implementer subagents. **Status:** design spec, not production code.
> **Binding above this doc:** `docs/design/liquid-glass-design-system.md`, plus the *actual*
> `wwwroot/css/tokens.css` + `wwwroot/css/base.css` (this spec uses the **real** token names,
> which differ slightly from the canonical doc — e.g. `--radius-card` is **22px**,
> `--lg-fill-strong` is **.80**, the backdrop is the **photo** `bg-liquid.jpg`+scrim, and
> `--lg-shadow-tile`/`--lg-fill-deep`/`--brand-ink-muted`/`--brand-ink-faint`/`--warn-soft`/
> `--ok-soft`/`--info`/`--ls-wide`/`--ls-widest` exist).
> **IMPLEMENTER RULE:** before using any token below, confirm it exists in the real `tokens.css`.
> If a token named here is missing, STOP and flag it — do not invent a value.
> **Goal of the slice:** "CS can build rosters." Five surfaces below; the **Roster builder** is the signature.

## 0. Conventions that apply to every surface

**Shell.** Every People page reuses the existing shell exactly as `Admin.cshtml` does:
```html
<div class="dash-shell">
  <header class="glass glass--nav dash-topbar"> … </header>   <!-- ONE blur layer, fixed -->
  <main class="dash-body"> … </main>
</div>
```
The topbar is the **only** `glass--nav` (blur) on screen. Everything in `.dash-body` is either a single static glass panel or opaque content inside it — honoring perf budget §10 (≤2 blur layers; lists scroll opaque).

**Glass-tier discipline for this slice (legibility contract §5).**
- A management page is **one panel of glass** (`.glass .glass--tile`, which is `--lg-fill-strong` .80 — the data-text-safe fill) + the topbar. Set `--lg-tile-shadow: var(--lg-shadow)` inline on the panel so it doesn't carry the heavier tile bevel meant for bento.
- **No `.glass--lensed` anywhere in Slice 1.** Lensing is reserved (doc §2.3, §9) for the attendance sheet / Admin metric hero. People is dense admin work; lensing on a data surface is banned.
- **No `.glass--clear` under any data text** (banned by §5.1). Chips/badges that carry text sit on `--lg-fill-tint` or a semantic soft token, never clear.
- Rows inside lists/tables are **opaque tinted fills** (`rgba(var(--brand-rgb), .04)` like `.task-item`), not glass — the panel is the glass once.

**Scale.** Dense admin scale throughout: base `--t-admin-body` (15px), labels `--t-admin-label` (13px), captions `--t-admin-meta` (12px), page/section titles `--t-admin-title` / `--t-admin-display`. Big numerals only for the hub's year/count badges.

**RTL + numerals (doc §7).** Logical properties only; no `[dir=rtl]`. Wrap every phone, age, date, count in `<bdi class="num">…</bdi>` (the `.num` class already forces `direction:ltr; unicode-bidi:isolate; tabular-nums`). Directional glyphs (breadcrumb chevron, "open roster" arrow) get `.icon-directional` (new, §6) so they mirror; checkmarks/toggles/badges never mirror.

**Motion (doc §8).** Lists and tables render **instant** — no per-row entrance, no hover-reveal of actions on touch. Animate **only confirmations**: a row added/removed from the roster, a toggle pill flipping, a save success. The panel itself may use `.glass--enter` (materialize) on first paint — that's chrome, allowed. Everything gated behind `prefers-reduced-motion` (already global in tokens.css).

**Sub-navigation.** People is a section with four destinations (Hub, Schools, Classes, Clients). A shared **section sub-bar** sits directly under the topbar inside `.dash-body` (new `.subnav`, §6) so CS can move laterally. It is **not** glass (it scrolls with content) — a tinted pill rail.

---

## 1. People Hub / landing

**Route:** `/People` (role `CsOrAdmin`). Entry point: four destination cards + the current-academic-year indicator.

### Wireframe
```
┌─ glass glass--nav dash-topbar ─────────────────────────────────────────────┐
│ עורקבי            ניהול אנשים            שלום, …   [יציאה]                    │
└────────────────────────────────────────────────────────────────────────────┘
  dash-body
  ┌─ subnav (tinted rail, not glass) ──────────────────────────────────────┐
  │  ● סקירה        בתי ספר        כיתות        לקוחות                       │
  └────────────────────────────────────────────────────────────────────────┘

  ┌─ page-head ────────────────────────────────────────────────────────────┐
  │  ניהול אנשים                                  ┌─ year-chip (glass tint)─┐ │
  │  בתי ספר, כיתות ולקוחות במקום אחד              │ ● שנת הלימודים תשפ״ו    │ │
  └────────────────────────────────────────────────└────────────────────────┘─┘

  ┌─ hub-grid (3 cols → 1 col <768) ───────────────────────────────────────┐
  │ ┌ nav-card ────────┐ ┌ nav-card ────────┐ ┌ nav-card ────────┐         │
  │ │ 🏫  בתי ספר       │ │ 👥  כיתות         │ │ 🎓  לקוחות        │         │
  │ │ ניהול מוסדות      │ │ קבוצות לימוד      │ │ תלמידים ופרטיהם   │         │
  │ │             →     │ │             →     │ │            →     │         │
  │ └──────────────────┘ └──────────────────┘ └──────────────────┘         │
  └────────────────────────────────────────────────────────────────────────┘
```
> NOTE on counts: the architect's blueprint did not require live counts on the hub cards for Slice 1; show counts only if cheaply available from the services, otherwise omit the count affordance (do NOT hardcode placeholder numbers — that is a Slice-0 mistake we are not repeating).

### Markup skeleton (real classes)
```html
<main class="dash-body people">
  <nav class="subnav" aria-label="ניווט אנשים">
    <a class="subnav__item is-active" href="/People">סקירה</a>
    <a class="subnav__item" href="/People/Schools">בתי ספר</a>
    <a class="subnav__item" href="/People/Classes">כיתות</a>
    <a class="subnav__item" href="/People/Clients">לקוחות</a>
  </nav>

  <header class="page-head">
    <div>
      <h1 class="page-head__title">ניהול אנשים</h1>
      <p class="page-head__sub">בתי ספר, כיתות ולקוחות במקום אחד</p>
    </div>
    <span class="year-chip" title="שנת הלימודים הפעילה">
      <span class="year-chip__dot" aria-hidden="true"></span>
      שנת הלימודים <bdi class="num">תשפ״ו</bdi>
    </span>
  </header>

  <div class="glass glass--tile people-panel hub-grid" style="--lg-tile-shadow: var(--lg-shadow);">
    <a class="nav-card" href="/People/Schools">
      <span class="nav-card__icon" aria-hidden="true">🏫</span>
      <span class="nav-card__title">בתי ספר</span>
      <span class="nav-card__desc">ניהול מוסדות ופרטי קשר</span>
      <span class="nav-card__count"><span class="icon-directional" aria-hidden="true">←</span></span>
    </a>
    <!-- כיתות, לקוחות … same pattern -->
  </div>
</main>
```

### Exact Hebrew copy
| Element | Hebrew |
|---|---|
| Topbar title | `ניהול אנשים` |
| Page title / sub | `ניהול אנשים` / `בתי ספר, כיתות ולקוחות במקום אחד` |
| Year chip | `שנת הלימודים <bdi class="num">תשפ״ו</bdi>` |
| Subnav items | `סקירה` · `בתי ספר` · `כיתות` · `לקוחות` |
| Card titles | `בתי ספר` · `כיתות` · `לקוחות` |
| Card descriptions | `ניהול מוסדות ופרטי קשר` · `קבוצות לימוד לפי בית ספר ושנה` · `תלמידים ופרטיהם` |
| Empty year (no current year set) | chip reads `לא הוגדרה שנת לימודים` in `--warn` tint |

### Interaction / motion
Instant, no stagger. `nav-card` hover lifts `translateY(-2px)` + shadow upgrade `--lg-shadow` → `--lg-shadow-lifted` (`--dur-fast`, `--ease-glass`). Year-chip dot is a static `--ok` dot — no pulse.

---

## 2. Schools — index + create/edit form

**Routes:** `/People/Schools` (index), `/People/Schools/Create`, `/People/Schools/Edit/{id}`.
**Fields (spec §4 School):** `Name`, `City`, `ExternalWebsiteUrl` (nullable).

### 2a. Index — markup skeleton
```html
<header class="list-head">
  <h1 class="page-head__title">בתי ספר</h1>
  <a class="btn-primary" href="/People/Schools/Create">+ בית ספר חדש</a>
</header>

<form class="search-bar" method="get" role="search">
  <input class="search-bar__input" name="q" type="search"
         placeholder="חיפוש לפי שם או עיר…" aria-label="חיפוש בתי ספר">
</form>

<div class="glass glass--tile people-panel" style="--lg-tile-shadow: var(--lg-shadow);">
  <table class="data-table">
    <thead>
      <tr>
        <th scope="col">שם בית הספר</th>
        <th scope="col">עיר</th>
        <th scope="col">אתר</th>
        <th scope="col" class="data-table__actions-col">פעולות</th>
      </tr>
    </thead>
    <tbody>
      <tr class="data-row">
        <td class="data-cell data-cell--primary">עירוני א׳</td>
        <td class="data-cell">חיפה</td>
        <td class="data-cell">
          <a class="data-link" href="https://…" target="_blank" rel="noopener">
            אתר <span class="icon-directional" aria-hidden="true">↗</span></a>
        </td>
        <td class="data-cell data-cell--actions">
          <a class="btn-ghost btn-ghost--sm" href="/People/Schools/Edit/1">עריכה</a>
        </td>
      </tr>
    </tbody>
  </table>

  <!-- empty state (no rows) -->
  <div class="empty-state">
    <span class="empty-state__icon" aria-hidden="true">🏫</span>
    <p class="empty-state__title">אין בתי ספר עדיין</p>
    <p class="empty-state__hint">התחילו בהוספת בית הספר הראשון כדי לשייך אליו כיתות.</p>
    <a class="btn-primary" href="/People/Schools/Create">+ בית ספר חדש</a>
  </div>
</div>
```

### Hebrew copy — Schools
| Element | Hebrew |
|---|---|
| Page title / New button | `בתי ספר` / `+ בית ספר חדש` |
| Search placeholder | `חיפוש לפי שם או עיר…` |
| Column headers | `שם בית הספר` · `עיר` · `אתר` · `פעולות` |
| Website cell (has url / none) | `אתר ↗` / `—` |
| Row action | `עריכה` |
| Empty state | title `אין בתי ספר עדיין` · hint `התחילו בהוספת בית הספר הראשון כדי לשייך אליו כיתות.` |
| Search → no match | title `לא נמצאו תוצאות` · hint `נסו מונח חיפוש אחר.` |

### 2b. Create/Edit form
A centered `.glass .glass--tile .form-panel` (max-inline 560px) using the **recessed-well** input idiom (the new CSS in §6 extends `.auth-card input` recipe to `.form-field input/select/textarea`).
```html
<div class="glass glass--tile form-panel" style="--lg-tile-shadow: var(--lg-shadow);">
  <h1 class="form-panel__title">בית ספר חדש</h1>
  <form method="post" class="form-grid">
    <div class="form-field">
      <label class="form-field__label" for="Name">שם בית הספר <span class="req">*</span></label>
      <input id="Name" name="Input.Name" required maxlength="200">
      <p class="form-field__error" data-valmsg-for="Input.Name"></p>
    </div>
    <div class="form-field">
      <label class="form-field__label" for="City">עיר <span class="req">*</span></label>
      <input id="City" name="Input.City" required maxlength="100">
    </div>
    <div class="form-field">
      <label class="form-field__label" for="Url">כתובת אתר <span class="opt">(לא חובה)</span></label>
      <input id="Url" name="Input.ExternalWebsiteUrl" type="url"
             inputmode="url" dir="ltr" placeholder="https://example.co.il">
      <p class="form-field__hint">קישור לאתר בית הספר, אם קיים.</p>
    </div>
    <div class="form-actions">
      <a class="btn-ghost" href="/People/Schools">ביטול</a>
      <button type="submit" class="btn-primary">שמירת בית ספר</button>
    </div>
  </form>
</div>
```
| Form element | Hebrew |
|---|---|
| Heading (create / edit) | `בית ספר חדש` / `עריכת בית ספר` |
| Breadcrumb | `בתי ספר ‹ בית ספר חדש` |
| Labels | `שם בית הספר *` · `עיר *` · `כתובת אתר (לא חובה)` |
| URL field | LTR, placeholder `https://example.co.il`, hint `קישור לאתר בית הספר, אם קיים.` |
| Required / optional markers | `*` (`.req`, `--absent`) ; `(לא חובה)` (`.opt`, muted) |
| Validation: empty name | `יש להזין שם בית ספר` |
| Validation: bad url | `כתובת אתר אינה תקינה` |
| Actions | `ביטול` (ghost) · `שמירת בית ספר` (primary) |
| Save success | `בית הספר נשמר` |

---

## 3. Classes — index + create/edit form

**Routes:** `/People/Classes`, `/Create`, `/Edit/{id}`, `/Roster/{id}`.
**Fields (spec §4 Class):** `Name`, `SchoolId` (select), `AcademicYearId` (select), `Status` (Active/Archived). **No `SyllabusId` in Slice 1** (deferred to Slice 2).

### 3a. Index
Same `data-table` panel as Schools, with a **filter rail** (by school, by year, by status) and a **status chip** column. Archived classes (hidden by the global query filter) appear only when the status filter is `בארכיון` (page uses `IgnoreQueryFilters()` for that view).
```html
<td class="data-cell"><span class="status-chip status-chip--active">פעילה</span></td>
<td class="data-cell data-cell--actions">
  <a class="btn-ghost btn-ghost--sm btn-ghost--accent" href="/People/Classes/Roster/12">רוסטר</a>
  <a class="btn-ghost btn-ghost--sm" href="/People/Classes/Edit/12">עריכה</a>
</td>
```
The **primary** per-row action is `רוסטר` (`btn-ghost--accent` = brand-filled); `עריכה` is secondary.

### Hebrew copy — Classes index
| Element | Hebrew |
|---|---|
| Page title / new | `כיתות` / `+ כיתה חדשה` |
| Filter labels | `בית ספר` · `שנה` · `סטטוס` |
| Status filter pills | `פעילות` · `בארכיון` · `הכל` |
| Column headers | `שם הכיתה` · `בית ספר` · `שנה` · `סטטוס` · `פעולות` |
| Status chips | `פעילה` (`--ok`) · `בארכיון` (muted) |
| Row actions | `רוסטר` (accent) · `עריכה` |
| Empty | title `אין כיתות עדיין` · hint `צרו כיתה ושייכו אותה לבית ספר ולשנת לימודים.` |
| Empty (filtered) | `אין כיתות התואמות את הסינון` |

### 3b. Create/Edit form
Same `form-panel`. The two relationships are **selects** styled like the recessed wells. Status is a two-option **segmented toggle** (§6) not a dropdown.
```html
<div class="form-field">
  <label class="form-field__label" for="School">בית ספר <span class="req">*</span></label>
  <select id="School" name="Input.SchoolId" required class="form-select">
    <option value="" disabled selected>בחרו בית ספר…</option>
    <option value="1">עירוני א׳</option>
  </select>
</div>
<div class="form-field">
  <label class="form-field__label" for="Year">שנת לימודים <span class="req">*</span></label>
  <select id="Year" name="Input.AcademicYearId" required class="form-select">
    <option value="3" selected>תשפ״ו (נוכחית)</option>
  </select>
</div>
<fieldset class="form-field">
  <legend class="form-field__label">סטטוס</legend>
  <div class="segment" role="radiogroup" aria-label="סטטוס כיתה">
    <label class="segment__opt">
      <input type="radio" name="Input.Status" value="Active" checked><span>פעילה</span></label>
    <label class="segment__opt">
      <input type="radio" name="Input.Status" value="Archived"><span>בארכיון</span></label>
  </div>
</fieldset>
```
| Form element | Hebrew |
|---|---|
| Heading | `כיתה חדשה` / `עריכת כיתה` |
| Labels | `שם הכיתה *` · `בית ספר *` · `שנת לימודים *` · `סטטוס` |
| Select placeholders | `בחרו בית ספר…` · `בחרו שנת לימודים…` |
| Year option (current) | `תשפ״ו (נוכחית)` |
| Segment options | `פעילה` · `בארכיון` |
| Validation: no school / no year | `יש לבחור בית ספר` / `יש לבחור שנת לימודים` |
| Actions / success | `ביטול` · `שמירת כיתה` ; `הכיתה נשמרה` |

---

## 4. Clients (Students) — index + create/edit form

**Routes:** `/People/Clients`, `/Create`, `/Edit/{id}`.
**Fields (spec §4 Client):** `Name`, `ParentPhone` (nullable), `Age` (nullable int), `Address` (nullable), `Birthday` (nullable DateOnly), `IsActive`. (Tryout/payment are NOT here — they live on Enrollment, surfaced in the Roster builder §5.)

### 4a. Index
Densest table. Numeric/temporal columns wrap in `<bdi class="num">`. `IsActive` shows a status chip; an inactive client (mid-year dropout) is **dimmed** (`.data-row--muted`) but NOT hidden (archival rule: `is_active=false` ≠ archived).
```html
<tr class="data-row data-row--muted">  <!-- --muted only when IsActive=false -->
  <td class="data-cell data-cell--primary">עידו לוי</td>
  <td class="data-cell"><bdi class="num">052-7654321</bdi></td>
  <td class="data-cell"><bdi class="num">8</bdi></td>
  <td class="data-cell"><bdi class="num">04.11.2017</bdi></td>
  <td class="data-cell">רחובות</td>
  <td class="data-cell"><span class="status-chip status-chip--off">לא פעיל</span></td>
  <td class="data-cell data-cell--actions">
    <a class="btn-ghost btn-ghost--sm" href="/People/Clients/Edit/7">עריכה</a>
  </td>
</tr>
```

### Hebrew copy — Clients index
| Element | Hebrew |
|---|---|
| Page title / new | `לקוחות` / `+ לקוח חדש` |
| Search placeholder | `חיפוש לפי שם או טלפון…` |
| Active filter pills | `פעילים` · `כולם` |
| Column headers | `שם` · `טלפון הורה` · `גיל` · `יום הולדת` · `כתובת` · `סטטוס` · `פעולות` |
| Status chips | `פעיל` (`--ok`) · `לא פעיל` (muted) |
| Empty | title `אין לקוחות עדיין` · hint `הוסיפו תלמיד כדי לשבץ אותו לכיתות.` |

### 4b. Create/Edit form
`form-panel` with a 2-col grid. Phone LTR-isolated, age numeric, birthday native `type="date"`. `IsActive` is a **toggle pill** (§6) defaulting on.
```html
<div class="form-grid form-grid--2col">
  <div class="form-field">
    <label class="form-field__label" for="Name">שם מלא <span class="req">*</span></label>
    <input id="Name" name="Input.Name" required maxlength="200">
  </div>
  <div class="form-field">
    <label class="form-field__label" for="Phone">טלפון הורה</label>
    <input id="Phone" name="Input.ParentPhone" type="tel" dir="ltr"
           inputmode="tel" placeholder="050-0000000" autocomplete="tel">
  </div>
  <div class="form-field">
    <label class="form-field__label" for="Age">גיל</label>
    <input id="Age" name="Input.Age" type="number" min="3" max="21" dir="ltr" class="num">
  </div>
  <div class="form-field">
    <label class="form-field__label" for="Bday">יום הולדת</label>
    <input id="Bday" name="Input.Birthday" type="date" dir="ltr" class="num">
  </div>
  <div class="form-field form-field--full">
    <label class="form-field__label" for="Addr">כתובת</label>
    <input id="Addr" name="Input.Address">
  </div>
  <div class="form-field form-field--full">
    <span class="form-field__label">סטטוס</span>
    <label class="toggle-pill">
      <input type="checkbox" name="Input.IsActive" checked>
      <span class="toggle-pill__track" aria-hidden="true"></span>
      <span class="toggle-pill__label">פעיל</span>
    </label>
  </div>
</div>
```
> NOTE: per the architect, `ParentPhone` is **nullable** — do NOT mark it `required`. Only `Name` is required.
| Form element | Hebrew |
|---|---|
| Heading | `לקוח חדש` / `עריכת לקוח` |
| Labels | `שם מלא *` · `טלפון הורה` · `גיל` · `יום הולדת` · `כתובת` · `סטטוס` |
| Phone | LTR, placeholder `050-0000000` |
| Toggle label (on/off) | `פעיל` / `לא פעיל` |
| Validation: empty name | `יש להזין שם` |
| Validation: bad phone | `מספר טלפון אינו תקין` |
| Validation: age range | `גיל חייב להיות בין 3 ל-21` |
| Actions / success | `ביטול` · `שמירת לקוח` ; `הלקוח נשמר` |

---

## 5. Roster builder — the signature surface

**Route:** `/People/Classes/Roster/{classId}` (role `CsOrAdmin`).
**The job:** open a Class, see enrolled students, enroll/un-enroll clients. Each enrollment carries three toggles — `IsTryout`, `PaidMaterials`, `PaidMonthly`. Tryout students pin to the bottom under a TRYOUT tray (spec §9.2 / §5.D).

### Concept — two-pane inside one glass panel
- **inline-end pane (the roster — leading/right in RTL): "רשומים לכיתה"** — enrolled students, each an opaque `roster-row` with its three toggle pills + a remove action. Tryout rows pin to the bottom inside a brand-tinted `tryout-tray` (`--lg-fill-tint`), each with a `TRYOUT` badge.
- **inline-start pane (trailing/left): "תלמידים זמינים"** — clients not yet in this class, each an opaque `avail-row` with a single `+ הוספה`. A search well filters it.

RTL reading: the roster (the thing being built) is the leading/right column; the source pool is the trailing/left column you pull from.

**Perf:** the whole pane is **one glass panel** (`--lg-fill-strong`); both columns scroll **opaque rows**. Two scrollable lists + the topbar = within §10 ≤2-blur budget. The divider is a hairline, not a second glass.

### Wireframe
```
 ┌ roster-head ─────────────────────────────────────────────────────────────┐
 │ כיתות ‹ קבוצה א׳ — רוסטר        עירוני א׳ · תשפ״ו · ●פעילה                 │
 │                                            ┌ count-chip ┐ ┌ count-chip ┐    │
 │                                            │ רשומים 18  │ │ ניסיון 2   │    │
 └──────────────────────────────────────────────────────────────────────────┘
 ┌ glass glass--tile roster-pane (ONE glass; columns scroll opaque) ─────────┐
 │ ┌ ENROLLED (inline-end / leading) ────────┐│┌ AVAILABLE (inline-start) ──┐ │
 │ │ רשומים לכיתה            18              │││ תלמידים זמינים              │ │
 │ │ ┌ roster-row ──────────────────────┐   │││ 🔎 חיפוש…                   │ │
 │ │ │ דנה כהן  [חומרים][חודשי][ניסיון] ✕ │   │││ ┌ avail-row ───────────┐   │ │
 │ │ │ 050-1234567                       │   │││ │ נועה ברק  [+ הוספה]    │   │ │
 │ │ └──────────────────────────────────┘   │││ └──────────────────────┘   │ │
 │ │ ┌ tryout-tray (--lg-fill-tint) ─────┐  │││                             │ │
 │ │ │ ◇ על תנאי (ניסיון)                 │  │││                             │ │
 │ │ │ TRYOUT  מאיה ד׳ [חומרים][חודשי]  ✕ │  │││                             │ │
 │ │ └──────────────────────────────────┘  │││                             │ │
 │ └─────────────────────────────────────────┘│└────────────────────────────┘ │
 └────────────────────────────────────────────────────────────────────────────┘
```

### How each side reads
**Enrolled row (`roster-row`).** Opaque tinted row. Inline flow start→end (right→left in RTL): `[name + phone]  →  [three toggle pills]  →  [remove ✕]`. Name `--t-admin-body`/600; phone `--t-admin-meta` `<bdi class="num">` beneath. Remove control is a circular ghost `✕` at the inline-end edge; muted until hover where it tints `--absent`.

**The three toggle pills** — flat tinted (NO backdrop-filter on scrolling rows, perf §10.4; "glass pill" look from tint + hairline + inset highlight). Each on/off:
- **`חומרים`** (PaidMaterials) — off: outline on tint. on: `--ok-soft`/`--ok` + ✓.
- **`חודשי`** (PaidMonthly) — same, `--ok` family.
- **`ניסיון`** (IsTryout) — on: `--warn-soft`/`--warn` AND moves the row into the tryout-tray. off: outline.

Pills are real `<button>`s toggled in place; flipping animates the fill with `--ease-spring` (~180ms). Payment pills independent; the tryout pill additionally re-sorts the row.

**Tryout tray.** When `ניסיון` is on, the row moves to a brand-tinted sub-section pinned at the column block-end: `tryout-tray` (`--lg-fill-tint`, `--radius-card`, hairline) with a `section-label`-style header `על תנאי (ניסיון)`. Each tray row carries a **`TRYOUT` badge** at its inline-start (leading) edge — solid `--warn` pill, uppercase, never mirrored.

**Available row (`avail-row`).** Opaque, lighter. `[name + phone]  →  [+ הוספה]`. Add button `btn-ghost btn-ghost--sm btn-ghost--accent`. An already-enrolled client never appears here.

### Add / remove confirmation (the only animated moments)
- **Add:** click `+ הוספה` → row leaves available, materializes into enrolled (`.roster-row--enter`: opacity 0→1, `translateX` from inline-start ~12px + slight scale, `--ease-spring`, ~280ms). Enrolled count increments. Defaults: `IsTryout=false`, both payments `false`.
- **Remove:** click `✕` → row collapses (height→0 + fade, `--ease-glass`, ~180ms) and reappears in available. No modal (reversible); show a 4-second **undo toast** `התלמיד הוסר מהכיתה — ביטול`.
- **Toggle flip:** pill fill animates in place; tryout flip re-sorts (slide to/from the tray, same `roster-row--enter`).
- All gated behind `prefers-reduced-motion`.

> SERVER NOTE: Slice 1 has **no HTMX yet** (HTMX is added in Slice 2 per the roadmap). For Slice 1, implement add/remove/toggle as **standard form POSTs** that re-render the roster page (full-page round-trip). The optimistic/HTMX/animation behavior above is the target once HTMX lands; in Slice 1 the motion is best-effort (the page re-renders), and correctness (the enrollment is created/dropped/updated server-side) is what matters. Do NOT add a JS framework for this in Slice 1.

### Empty / edge states
| State | Hebrew |
|---|---|
| Roster empty | title `אין תלמידים רשומים` · hint `הוסיפו תלמידים מהרשימה כדי לבנות את הרוסטר.` |
| No available clients left | `כל התלמידים הזמינים כבר רשומים` |
| Available search no-match | `לא נמצאו תלמידים` |
| No tryouts | tray hidden entirely (don't render an empty tray) |

### Hebrew copy — Roster builder
| Element | Hebrew |
|---|---|
| Topbar title | `רוסטר — {שם הכיתה}` |
| Breadcrumb | `כיתות ‹ {שם הכיתה} — רוסטר` |
| Class meta line | `{בית ספר} · <bdi class="num">{שנה}</bdi> · ●פעילה` |
| Count chips | `רשומים <bdi class="num">18</bdi>` · `ניסיון <bdi class="num">2</bdi>` |
| Enrolled column header | `רשומים לכיתה` |
| Available column header | `תלמידים זמינים` |
| Available search | `חיפוש תלמיד…` |
| Toggle pills | `חומרים` · `חודשי` · `ניסיון` |
| Tryout tray header | `על תנאי (ניסיון)` |
| Tryout badge | `TRYOUT` (kept Latin/uppercase — the recognizable signature mark; `ניסיון` carries the meaning on the pill) |
| Add / remove | `+ הוספה` · remove icon-only `✕` with `aria-label="הסרה מהכיתה"` |
| Undo toast | `התלמיד הוסר מהכיתה` + action `ביטול` |
| Pill aria-labels | `שולם חומרים` · `שולם חודשי` · `תלמיד ניסיון` |
| Duplicate enroll error | `התלמיד כבר רשום לכיתה זו` |

### Roster markup skeleton
```html
<header class="roster-head">
  <nav class="breadcrumb" aria-label="ניווט">
    <a href="/People/Classes">כיתות</a>
    <span class="icon-directional" aria-hidden="true">‹</span>
    <span>קבוצה א׳ — רוסטר</span>
  </nav>
  <p class="roster-head__meta">עירוני א׳ · <bdi class="num">תשפ״ו</bdi> ·
     <span class="status-chip status-chip--active">פעילה</span></p>
  <div class="roster-head__counts">
    <span class="count-chip">רשומים <bdi class="num">18</bdi></span>
    <span class="count-chip count-chip--tryout">ניסיון <bdi class="num">2</bdi></span>
  </div>
</header>

<div class="glass glass--tile roster-pane" style="--lg-tile-shadow: var(--lg-shadow);">
  <section class="roster-col roster-col--enrolled" aria-label="רשומים לכיתה">
    <div class="section-label">רשומים לכיתה</div>
    <ul class="roster-list" role="list">
      <li class="roster-row">
        <div class="roster-row__who">
          <span class="roster-row__name">דנה כהן</span>
          <bdi class="roster-row__phone num">050-1234567</bdi>
        </div>
        <div class="roster-row__toggles">
          <button type="submit" class="pill-toggle is-on" name="toggle" value="materials:5"
                  aria-pressed="true" aria-label="שולם חומרים">חומרים</button>
          <button type="submit" class="pill-toggle" name="toggle" value="monthly:5"
                  aria-pressed="false" aria-label="שולם חודשי">חודשי</button>
          <button type="submit" class="pill-toggle pill-toggle--tryout" name="toggle" value="tryout:5"
                  aria-pressed="false" aria-label="תלמיד ניסיון">ניסיון</button>
        </div>
        <button type="submit" class="roster-row__remove" name="remove" value="5"
                aria-label="הסרה מהכיתה">✕</button>
      </li>
    </ul>

    <!-- tryout tray pinned to block-end; rendered only if any tryout exists -->
    <div class="tryout-tray">
      <div class="section-label tryout-tray__label">על תנאי (ניסיון)</div>
      <ul class="roster-list" role="list">
        <li class="roster-row roster-row--tryout">
          <span class="tryout-badge">TRYOUT</span>
          <div class="roster-row__who">
            <span class="roster-row__name">מאיה ד׳</span>
            <bdi class="roster-row__phone num">053-7777777</bdi>
          </div>
          <div class="roster-row__toggles">…</div>
          <button type="submit" class="roster-row__remove" name="remove" value="9" aria-label="הסרה מהכיתה">✕</button>
        </li>
      </ul>
    </div>
  </section>

  <section class="roster-col roster-col--available" aria-label="תלמידים זמינים">
    <div class="section-label">תלמידים זמינים</div>
    <form class="search-bar search-bar--inset" role="search" method="get">
      <input class="search-bar__input" name="q" type="search" placeholder="חיפוש תלמיד…" aria-label="חיפוש תלמיד">
    </form>
    <ul class="avail-list" role="list">
      <li class="avail-row">
        <div class="roster-row__who">
          <span class="roster-row__name">נועה ברק</span>
          <bdi class="roster-row__phone num">050-2223344</bdi>
        </div>
        <form method="post"><button type="submit" class="btn-ghost btn-ghost--sm btn-ghost--accent"
                name="add" value="22">+ הוספה</button></form>
      </li>
    </ul>
  </section>
</div>
```
> The `name/value` attributes above show one viable POST-handler shape for full-page-roundtrip Slice 1. The implementer + architect may choose a cleaner handler design (separate POST handlers `OnPostAdd`, `OnPostRemove`, `OnPostToggle`); the design only constrains the visible markup/classes/copy.

---

## 6. Shared additions to `base.css` (one task — paste-ready)

All logical-property-native, token-driven. The recessed-well form fields extend the existing `.auth-card input` recipe. **No new tokens required** — everything references tokens already in `tokens.css` (implementer must verify each exists). Append after the existing component blocks, **before** the `@supports not` fallback block (and add the noted lines *into* that fallback block).

```css
/* ============================================================
   SLICE 1 — People surfaces
   Reuses: .glass/.glass--tile, .btn-primary/.btn-ghost, .section-label,
   .num, recessed-well input recipe (from .auth-card input).
   Perf: panels are the ONLY glass; rows/tables are opaque fills.
   ============================================================ */

/* ---- Section sub-navigation (tinted rail, NOT glass — scrolls with body) ---- */
.subnav {
  display: flex; flex-wrap: wrap; gap: var(--sp-2);
  margin-block-end: var(--sp-6);
  padding: var(--sp-1);
  background: var(--lg-fill-tint);
  border: 1px solid rgba(var(--brand-rgb), .12);
  border-radius: var(--radius-pill);
  inline-size: fit-content;
  max-inline-size: 100%;
}
.subnav__item {
  padding-block: var(--sp-2); padding-inline: var(--sp-5);
  border-radius: var(--radius-pill);
  font-size: var(--t-admin-label); font-weight: var(--fw-medium);
  color: var(--brand-ink-muted); text-decoration: none;
  transition: background var(--dur-fast) var(--ease-glass), color var(--dur-fast) var(--ease-glass);
}
.subnav__item:hover { color: var(--brand); background: rgba(var(--brand-rgb), .08); }
.subnav__item.is-active { color: #fff; background: var(--brand); font-weight: var(--fw-semibold); }

/* ---- Page / list headers ---- */
.page-head, .list-head {
  display: flex; align-items: flex-end; justify-content: space-between;
  gap: var(--sp-4); margin-block-end: var(--sp-5); flex-wrap: wrap;
}
.page-head__title {
  margin: 0; font-size: var(--t-admin-display); font-weight: var(--fw-bold);
  letter-spacing: var(--ls-tight); color: var(--brand-ink);
}
.page-head__sub { margin: var(--sp-1) 0 0; font-size: var(--t-admin-body); color: var(--brand-ink-muted); }

/* ---- Current-year chip (brand-tinted glass; small text on tint clears AA) ---- */
.year-chip {
  display: inline-flex; align-items: center; gap: var(--sp-2);
  padding-block: var(--sp-2); padding-inline: var(--sp-4);
  background: var(--lg-fill-tint);
  border: 1px solid rgba(var(--brand-rgb), .20);
  border-radius: var(--radius-pill);
  font-size: var(--t-admin-label); font-weight: var(--fw-semibold); color: var(--brand);
}
.year-chip__dot { inline-size: 7px; block-size: 7px; border-radius: 50%; background: var(--ok); flex: 0 0 auto; }

/* ---- People glass panel: the ONE glass surface; author sets --lg-tile-shadow inline ---- */
.people-panel { padding: var(--sp-6); }

/* ---- Hub destination grid + cards ---- */
.hub-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--sp-5); }
.nav-card {
  display: flex; flex-direction: column; gap: var(--sp-2);
  padding: var(--sp-5);
  background: rgba(var(--brand-rgb), .04);
  border: 1px solid rgba(var(--brand-rgb), .10);
  border-radius: var(--radius-card);
  text-decoration: none; color: var(--brand-ink);
  box-shadow: var(--lg-shadow);
  transition: transform var(--dur-fast) var(--ease-glass), box-shadow var(--dur-fast) var(--ease-glass);
}
.nav-card:hover { transform: translateY(-2px); box-shadow: var(--lg-shadow-lifted); }
.nav-card__icon  { font-size: 28px; line-height: 1; }
.nav-card__title { font-size: var(--t-admin-title); font-weight: var(--fw-bold); }
.nav-card__desc  { font-size: var(--t-admin-meta); color: var(--brand-ink-muted); flex: 1; }
.nav-card__count {
  display: inline-flex; align-items: center; gap: var(--sp-2);
  font-size: var(--t-admin-title); font-weight: var(--fw-bold); color: var(--brand);
  margin-block-start: var(--sp-2);
}

/* ---- Search bar (recessed well, reusing the auth-input recipe) ---- */
.search-bar { margin-block-end: var(--sp-4); }
.search-bar__input {
  inline-size: 100%; max-inline-size: 420px;
  background: rgba(var(--brand-rgb), .07);
  border: none; border-radius: var(--radius-chip);
  padding-block: var(--sp-3); padding-inline: var(--sp-5);
  font-family: var(--font-ui); font-size: var(--t-admin-body); color: var(--brand-ink);
  outline: none;
  box-shadow:
    inset 0 2px 6px rgba(var(--brand-rgb), .16),
    inset 0 -1px 0 rgba(255,255,255,.55),
    inset 0 1px 0 rgba(var(--brand-rgb), .18);
  transition: box-shadow var(--dur-fast) var(--ease-glass);
}
.search-bar__input::placeholder { color: var(--brand-ink-faint); }
.search-bar__input:focus {
  box-shadow:
    inset 0 2px 6px rgba(var(--brand-rgb), .12),
    inset 0 -3px 12px rgba(var(--brand-rgb), .18),
    0 0 0 2px rgba(var(--brand-rgb), .26);
}
.search-bar--inset .search-bar__input { max-inline-size: 100%; }

/* ---- Data table (panel is glass; header tinted-sticky; rows opaque) ---- */
.data-table { inline-size: 100%; border-collapse: collapse; }
.data-table thead th {
  position: sticky; inset-block-start: 0; z-index: 1;
  text-align: start; font-size: var(--t-admin-label); font-weight: var(--fw-semibold);
  letter-spacing: var(--ls-wide); color: var(--brand-ink-muted);
  padding-block: var(--sp-3); padding-inline: var(--sp-4);
  background: var(--lg-fill-deep);
  border-block-end: 1px solid rgba(var(--brand-rgb), .14);
}
.data-row { transition: background var(--dur-fast) var(--ease-glass); }
.data-row + .data-row .data-cell { border-block-start: 1px solid rgba(var(--brand-rgb), .08); }
.data-row:hover .data-cell { background: rgba(var(--brand-rgb), .05); }
.data-row--muted { opacity: .6; }
.data-cell {
  padding-block: var(--sp-3); padding-inline: var(--sp-4);
  font-size: var(--t-admin-body); color: var(--brand-ink); vertical-align: middle;
}
.data-cell--primary { font-weight: var(--fw-semibold); }
.data-cell--actions { text-align: end; white-space: nowrap; }
.data-cell--actions .btn-ghost--sm { margin-inline-start: var(--sp-2); }
.data-link { color: var(--brand); text-decoration: none; font-weight: var(--fw-medium); }
.data-link:hover { text-decoration: underline; }

/* ---- Status chips ---- */
.status-chip {
  display: inline-flex; align-items: center; gap: var(--sp-1);
  font-size: var(--t-admin-meta); font-weight: var(--fw-semibold);
  padding-block: 2px; padding-inline: var(--sp-2); border-radius: var(--radius-pill);
}
.status-chip::before { content: "●"; font-size: 8px; line-height: 1; }
.status-chip--active { background: var(--ok-soft); color: var(--ok); }
.status-chip--off,
.status-chip--archived { background: rgba(var(--brand-rgb), .08); color: var(--brand-ink-muted); }
.status-chip--off::before { content: "◦"; }

/* ---- Small / accent ghost button variants ---- */
.btn-ghost--sm { padding-block: var(--sp-1); padding-inline: var(--sp-3); font-size: var(--t-admin-meta); }
.btn-ghost--accent { background: var(--brand); color: #fff; border-color: transparent; }
.btn-ghost--accent:hover { background: var(--brand-tint); border-color: transparent; }

/* ---- Empty state ---- */
.empty-state {
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: var(--sp-3); text-align: center; color: var(--brand-ink-muted);
  padding-block: var(--sp-12); padding-inline: var(--sp-6);
}
.empty-state__icon  { font-size: 44px; opacity: .35; line-height: 1; }
.empty-state__title { font-size: var(--t-admin-title); font-weight: var(--fw-bold); color: var(--brand-ink); }
.empty-state__hint  { font-size: var(--t-admin-body); max-inline-size: 320px; line-height: var(--lh-normal); }
.empty-state .btn-primary { margin-block-start: var(--sp-3); }

/* ---- Management form (recessed wells: extend the auth-input recipe to selects/textarea) ---- */
.form-panel { max-inline-size: 560px; margin-inline: auto; padding: var(--sp-8); }
.form-panel__title { margin: 0 0 var(--sp-6); font-size: var(--t-admin-display); font-weight: var(--fw-bold); color: var(--brand-ink); }
.form-grid { display: flex; flex-direction: column; gap: var(--sp-5); }
.form-grid--2col { display: grid; grid-template-columns: 1fr 1fr; gap: var(--sp-5); }
.form-field { display: flex; flex-direction: column; gap: var(--sp-2); margin: 0; border: 0; padding: 0; min-inline-size: 0; }
.form-field--full { grid-column: 1 / -1; }
.form-field__label {
  font-size: var(--t-admin-meta); font-weight: var(--fw-semibold);
  letter-spacing: var(--ls-wide); text-transform: uppercase;
  color: var(--brand-ink-muted); padding-inline-start: var(--sp-1);
}
.form-field__hint  { font-size: var(--t-admin-meta); color: var(--brand-ink-muted); padding-inline-start: var(--sp-1); }
.form-field__error { font-size: var(--t-admin-meta); color: var(--absent); padding-inline-start: var(--sp-1); }
.req { color: var(--absent); }
.opt { color: var(--brand-ink-faint); font-weight: var(--fw-regular); text-transform: none; letter-spacing: 0; }

/* recessed wells for management inputs/selects/textarea — same recipe as .auth-card input */
.form-field input,
.form-field select,
.form-field textarea,
.form-select {
  inline-size: 100%;
  background: rgba(var(--brand-rgb), .07);
  border: none; border-radius: var(--radius-chip);
  padding-block: var(--sp-3); padding-inline: var(--sp-5);
  font-family: var(--font-ui); font-size: var(--t-admin-body); color: var(--brand-ink);
  outline: none;
  box-shadow:
    inset 0 2px 6px rgba(var(--brand-rgb), .16),
    inset 0 4px 14px rgba(var(--brand-rgb), .08),
    inset 0 -1px 0 rgba(255,255,255,.55),
    inset 0 1px 0 rgba(var(--brand-rgb), .18);
  transition: box-shadow var(--dur-fast) var(--ease-glass), background var(--dur-fast) var(--ease-glass);
}
.form-field textarea { min-block-size: 96px; resize: vertical; line-height: var(--lh-snug); }
.form-field input::placeholder, .form-field textarea::placeholder { color: var(--brand-ink-faint); }
.form-field input:focus,
.form-field select:focus,
.form-field textarea:focus,
.form-select:focus {
  box-shadow:
    inset 0 2px 6px rgba(var(--brand-rgb), .12),
    inset 0 -3px 12px rgba(var(--brand-rgb), .20),
    0 0 0 2px rgba(var(--brand-rgb), .28), 0 0 0 4px rgba(var(--brand-rgb), .10);
}
.form-actions {
  display: flex; gap: var(--sp-3); justify-content: flex-end;
  margin-block-start: var(--sp-4); padding-block-start: var(--sp-5);
  border-block-start: 1px solid rgba(var(--brand-rgb), .10);
}

/* ---- Segmented control (binary status: Active/Archived) ---- */
.segment {
  display: inline-flex; padding: 3px; gap: 2px;
  background: rgba(var(--brand-rgb), .07); border-radius: var(--radius-pill);
  box-shadow: inset 0 2px 6px rgba(var(--brand-rgb), .14);
}
.segment__opt { position: relative; }
.segment__opt input { position: absolute; opacity: 0; inset: 0; margin: 0; cursor: pointer; }
.segment__opt span {
  display: block; padding-block: var(--sp-2); padding-inline: var(--sp-5);
  border-radius: var(--radius-pill); font-size: var(--t-admin-label);
  font-weight: var(--fw-medium); color: var(--brand-ink-muted);
  transition: background var(--dur-fast) var(--ease-glass), color var(--dur-fast) var(--ease-glass);
}
.segment__opt input:checked + span { background: var(--brand); color: #fff; font-weight: var(--fw-semibold); }
.segment__opt input:focus-visible + span { box-shadow: 0 0 0 2px rgba(var(--brand-rgb), .35); }

/* ---- Toggle pill (is_active; standalone on/off) ---- */
.toggle-pill { display: inline-flex; align-items: center; gap: var(--sp-2); cursor: pointer; }
.toggle-pill input { position: absolute; opacity: 0; pointer-events: none; }
.toggle-pill__track {
  inline-size: 40px; block-size: 24px; border-radius: var(--radius-pill);
  background: rgba(var(--brand-rgb), .18); position: relative;
  transition: background var(--dur-fast) var(--ease-spring);
  box-shadow: inset 0 1px 3px rgba(var(--brand-rgb), .20);
}
.toggle-pill__track::after {
  content: ""; position: absolute; inset-block-start: 3px; inset-inline-start: 3px;
  inline-size: 18px; block-size: 18px; border-radius: 50%; background: #fff;
  box-shadow: 0 1px 2px rgba(var(--brand-rgb), .35);
  transition: inset-inline-start var(--dur-fast) var(--ease-spring);
}
.toggle-pill input:checked + .toggle-pill__track { background: var(--ok); }
.toggle-pill input:checked + .toggle-pill__track::after { inset-inline-start: 19px; }
.toggle-pill input:focus-visible + .toggle-pill__track { box-shadow: 0 0 0 2px rgba(var(--brand-rgb), .35); }
.toggle-pill__label { font-size: var(--t-admin-body); color: var(--brand-ink); }

/* ---- Breadcrumb ---- */
.breadcrumb { display: flex; align-items: center; gap: var(--sp-2); font-size: var(--t-admin-label); color: var(--brand-ink-muted); }
.breadcrumb a { color: var(--brand); text-decoration: none; }
.breadcrumb a:hover { text-decoration: underline; }

/* ---- Directional glyphs: mirror only these, in RTL (doc §7) ---- */
.icon-directional { display: inline-block; }
:dir(rtl) .icon-directional { transform: scaleX(-1); }

/* ============================================================
   ROSTER BUILDER (signature surface)
   ============================================================ */
.roster-head { display: flex; flex-wrap: wrap; align-items: center; gap: var(--sp-3); margin-block-end: var(--sp-5); }
.roster-head__meta { margin: 0; font-size: var(--t-admin-label); color: var(--brand-ink-muted); display: flex; align-items: center; gap: var(--sp-2); }
.roster-head__counts { display: flex; gap: var(--sp-2); margin-inline-start: auto; }
.count-chip {
  display: inline-flex; align-items: center; gap: var(--sp-2);
  padding-block: var(--sp-1); padding-inline: var(--sp-3); border-radius: var(--radius-pill);
  background: var(--lg-fill-tint); border: 1px solid rgba(var(--brand-rgb), .18);
  font-size: var(--t-admin-label); font-weight: var(--fw-semibold); color: var(--brand);
}
.count-chip--tryout { background: var(--warn-soft); border-color: rgba(200,134,27,.28); color: var(--warn); }

/* Two-pane: enrolled (leading/inline-end) | hairline | available (inline-start).
   ONE glass panel; both columns scroll opaque rows inside. */
.roster-pane { display: grid; grid-template-columns: 1.4fr 1fr; gap: 0; padding: 0; overflow: hidden; }
.roster-col { display: flex; flex-direction: column; padding: var(--sp-5); min-block-size: 0; max-block-size: calc(100vh - 220px); overflow-y: auto; }
.roster-col--available { border-inline-start: 1px solid rgba(var(--brand-rgb), .12); }
.roster-list, .avail-list { list-style: none; margin: var(--sp-3) 0 0; padding: 0; display: flex; flex-direction: column; gap: var(--sp-2); }

/* opaque rows (NOT glass — the pane is the glass once) */
.roster-row, .avail-row {
  display: flex; align-items: center; gap: var(--sp-3);
  padding-block: var(--sp-3); padding-inline: var(--sp-3);
  background: rgba(var(--brand-rgb), .04);
  border: 1px solid rgba(var(--brand-rgb), .09);
  border-radius: var(--radius-chip);
}
.roster-row__who { display: flex; flex-direction: column; gap: 2px; min-inline-size: 0; flex: 1; }
.roster-row__name { font-size: var(--t-admin-body); font-weight: var(--fw-semibold); color: var(--brand-ink); }
.roster-row__phone { font-size: var(--t-admin-meta); color: var(--brand-ink-muted); }
.roster-row__toggles { display: flex; gap: var(--sp-1); flex: 0 0 auto; }
.roster-row__remove {
  flex: 0 0 auto; inline-size: 26px; block-size: 26px; border-radius: 50%;
  border: 1px solid rgba(var(--brand-rgb), .14); background: transparent;
  color: var(--brand-ink-muted); cursor: pointer; line-height: 1; font-size: 13px;
  transition: background var(--dur-fast) var(--ease-glass), color var(--dur-fast) var(--ease-glass);
}
.roster-row__remove:hover { background: var(--absent-soft); color: var(--absent); border-color: rgba(178,58,72,.3); }

/* Toggle pills inside a row — flat tinted (NO backdrop-filter on scrolling rows, perf §10) */
.pill-toggle {
  font-size: var(--t-admin-meta); font-weight: var(--fw-semibold);
  padding-block: 3px; padding-inline: var(--sp-2); border-radius: var(--radius-pill);
  background: var(--lg-fill-tint); color: var(--brand-ink-muted);
  border: 1px solid rgba(var(--brand-rgb), .16); cursor: pointer;
  box-shadow: inset 0 1px 0 rgba(255,255,255,.4);
  transition: background var(--dur-fast) var(--ease-spring), color var(--dur-fast) var(--ease-spring), border-color var(--dur-fast) var(--ease-spring);
}
.pill-toggle.is-on { background: var(--ok-soft); color: var(--ok); border-color: rgba(46,125,91,.3); }
.pill-toggle.is-on::before { content: "✓ "; }
.pill-toggle--tryout.is-on { background: var(--warn-soft); color: var(--warn); border-color: rgba(200,134,27,.3); }
.pill-toggle--tryout.is-on::before { content: "◇ "; }

/* Tryout tray — pinned at the enrolled column's block-end, brand-tinted */
.tryout-tray {
  margin-block-start: var(--sp-4);
  padding: var(--sp-3);
  background: var(--lg-fill-tint);
  border: 1px solid rgba(var(--brand-rgb), .14);
  border-radius: var(--radius-card);
}
.tryout-tray__label { border-block-end: none; color: var(--warn); }
.roster-row--tryout { background: rgba(200,134,27,.06); border-color: rgba(200,134,27,.18); }
.tryout-badge {
  flex: 0 0 auto; font-size: var(--t-admin-meta); font-weight: var(--fw-bold);
  letter-spacing: var(--ls-wide);
  padding-block: 2px; padding-inline: var(--sp-2); border-radius: var(--radius-chip);
  background: var(--warn); color: #fff;
}

/* Roster confirmation motion — only add/remove/flip animate (doc §8) */
@media (prefers-reduced-motion: no-preference) {
  @keyframes roster-row-enter {
    from { opacity: 0; transform: translateX(calc(-1 * var(--sp-3))) scale(.98); }
    to   { opacity: 1; transform: translateX(0) scale(1); }
  }
  .roster-row--enter { animation: roster-row-enter var(--dur-mid) var(--ease-spring) both; }
}

/* ---- Undo toast (un-enroll safety) ---- */
.toast {
  position: fixed; inset-block-end: var(--sp-6); inset-inline: 0; margin-inline: auto;
  inline-size: fit-content; max-inline-size: 90vw; z-index: 200;
  display: flex; align-items: center; gap: var(--sp-4);
  padding-block: var(--sp-3); padding-inline: var(--sp-5);
  background: var(--lg-fill-solid);
  border: var(--lg-hairline); border-radius: var(--radius-pill);
  box-shadow: var(--lg-shadow-lifted);
  font-size: var(--t-admin-body); color: var(--brand-ink);
}
.toast__action { background: none; border: 0; color: var(--brand); font-weight: var(--fw-semibold); cursor: pointer; font-size: var(--t-admin-body); }

/* ---- Responsive: management surfaces collapse < 768px ---- */
@media (max-width: 767px) {
  .hub-grid { grid-template-columns: 1fr; }
  .form-grid--2col { grid-template-columns: 1fr; }
  .roster-pane { grid-template-columns: 1fr; }
  .roster-col { max-block-size: none; }
  .roster-col--available { border-inline-start: none; border-block-start: 1px solid rgba(var(--brand-rgb), .12); }
  .data-table thead { display: none; }
  .roster-head__counts { margin-inline-start: 0; }
}
```

**Add into the existing `@supports not (...)` fallback block:**
```css
@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .form-field input, .form-field select, .form-field textarea, .form-select { background: var(--lg-fill-solid); }
  .data-table thead th { background: var(--lg-fill-solid); }
}
```

---

## 7. Legibility & perf self-check (contract §5 / budget §10)
- Body/data text on `--lg-fill-strong` (.80) for panels; opaque tinted fills for rows — never `--lg-fill`/clear. ✔ §5.1
- Sticky table header `--lg-fill-deep` (opaque-enough); muted-blue text clears AA at dense scale. ✔ §5.2
- Text chips on tint/semantic-soft/solid; TRYOUT badge white on solid `--warn`. ✔ §5.4
- Blur layers: topbar `glass--nav` (1) + one panel (2) = 2 max; rows/pills/chips/table = zero backdrop-filter. ✔ §10.1/10.2
- No `.glass--lensed` on any People surface. ✔ §10.3
- `-webkit-` + `@supports not` fallback for new wells + sticky header. ✔ §10.6
- RTL: logical properties only; numerals in `<bdi class="num">`; only `.icon-directional` mirrors. ✔ §7

---

## Deferred / out-of-scope (designer-flagged)
- **Bulk/multi-select enroll** — not designed; Slice 1 is single add/remove. Revisit as a Slice-1.1 polish if rosters routinely exceed ~30.
- **HTMX/optimistic server wiring** — Slice 1 uses full-page POST round-trips (HTMX lands Slice 2). Motion is best-effort in Slice 1.
- **Full mobile table reflow** (`thead{display:none}` stub) — desktop-first surfaces; full mobile card-stack deferred.
