# Orkabi — Slice 4 "Logistics" (dispute loop) Liquid-Glass Design Spec

> **For:** the implementer subagents. **Status:** design spec, not production code. The CSS blocks are paste-ready *proposals* — verify every token against the live `tokens.css` before pasting.
> **Binding above this doc:** `docs/design/liquid-glass-design-system.md`, plus the **actual** `wwwroot/css/tokens.css` + `wwwroot/css/base.css` (both read this session — every class/token name below was confirmed present in the live files unless flagged **NEW**).
> **Restraint note (carried from Slice 3 §0).** This slice's UI is **light** — it is one dense admin orders table + one instructor accept/dispute action, both built almost entirely from the existing vocabulary. There are exactly **two** genuinely-new pieces: one chip variant (`.status-chip--packed`) and one tiny read-line (`.dispute-note`). The dispute textarea is the existing `.form-field textarea` recessed-well recipe; the orders list is `.data-table` as-is; filters are the existing `.status-pill-rail` + `.filter-select`; the row-swap is the established `_VacationRow`/`_SubRow` precedent. Everything else is reuse.
> **Automations have NO new UI.** The Slice-4 scheduler + birthday/absence/dropout/tryout Action-Items surface as `Action_Item`s on the **existing Slice-3 `/Operations/ActionItems`** page (`.action-card` + `.action-type`, both already in base.css with the full enum). The **Disputed** branch likewise writes an urgent Logistics `Action_Item` that appears there. This spec designs **only the Logistics order surfaces** — see §6 for the explicit Action-Items hand-off.

---

## 0. Conventions that apply to every surface (carried from Slice 1–3 §0)

**Two scales, one shell.** Every page reuses the existing shell exactly as Slices 1–3 do:
```html
<div class="dash-shell">
  <header class="glass glass--nav dash-topbar"> … </header>   <!-- ONE blur layer, fixed/sticky -->
  <main class="dash-body"> … </main>
</div>
```
The topbar is the **only** `glass--nav` (blur) on screen for every Slice-4 surface. **No bottom sheet, no `.glass--lensed` anywhere this slice** — these are a dense orders table and a small action form; lensing is banned on dense surfaces (perf §10.3, legibility §5). The single hero/lensed surface remains the Slice-2 attendance sheet.

- **Logistics/Admin orders list** = **dense admin scale**: base `--t-admin-body` (15px), `--t-admin-label` (13px), `--t-admin-meta` (12px). Reuse `.subnav`, `.page-head`, `.data-table`, `.status-chip`, `.status-pill-rail`/`.status-pill`, `.filter-select`, `.empty-state`, `.btn-ghost--sm`/`--accent`, `.model-chip` verbatim.
- **Instructor accept/dispute action** = mobile-first **instructor scale**: base `--t-body` (17px), labels `--t-label` (15px), meta `--t-meta` (13px). The instructor's "my class orders" view is a card-per-order read on a `.form-panel--instructor`-scaled panel; the dispute form is the existing `.form-field textarea` recessed well sized up by `.form-panel--instructor` (no new scale machinery).

**Glass-tier discipline (legibility §5 / perf §10).** A page is **one glass panel** (`.glass .glass--tile` = `--lg-fill-strong` .80, the data-text-safe fill, with `--lg-tile-shadow: var(--lg-shadow)` set inline as every Slice-1/2/3 panel does) + the fixed topbar. Rows inside the orders table are the **opaque tinted** `.data-row` recipe, never glass. The instructor order cards reuse the **opaque tinted** `.action-card` recipe (the panel is the glass once). Pills/chips/badges carry zero `backdrop-filter`. No `.glass--clear` under data text. The status-chip recolor (incl. the new `--packed`) is flat-tint, zero blur.

**RTL + numerals (§7).** Logical properties only; no `[dir=rtl]` file. Wrap every quantity, date, and count in `<bdi class="num">…</bdi>`. **Days/months in Hebrew.** Directional glyphs (breadcrumb chevron) get `.icon-directional`; status dots, checkmarks, the packed/dispute dots **never** mirror.

**Motion (§8).** Animate **only confirmations**: a Packed / Accepted / Disputed row-swap fades the new fragment in (reuse the existing gated `lg-fade-up`, exactly as `#syllabus-models.htmx-settling` does), and the status-chip color change *is* the confirmation. The **generate-orders** action returns a freshly-rendered list `<tbody>` that fades in once (gated). **No per-row entrance staggers.** Everything globally gated by `prefers-reduced-motion`.

**HTMX fragment map (what swaps, never a full reload).** Mirrors the Slice-2 `_SubRow` / Slice-3 `_VacationRow` substitution-row precedent exactly. Partials are `_X.cshtml`, each a single swappable node with a stable `id`. Anti-forgery: global `hx-headers` on `<body>` (implementer note, not a design constraint).

| Surface | Trigger | `hx-*` | Swapped fragment |
|---|---|---|---|
| §2 Logistics marks Packed | `נארז` button | `hx-post="?handler=Pack&id={id}"` `hx-target="#order-row-{id}"` `hx-swap="outerHTML"` | `_OrderRow.cshtml` (row → Packed: `--packed` chip + delivered date) |
| §2 Generate orders | `צור הזמנות` button | `hx-post="?handler=Generate"` `hx-target="#orders-body"` `hx-swap="outerHTML"` | `_OrdersBody.cshtml` (the `<tbody>` re-rendered with newly-seeded Pending rows) |
| §3 Instructor marks Accepted | `התקבל` button | `hx-post="?handler=Accept&id={id}"` `hx-target="#order-card-{id}"` `hx-swap="outerHTML"` | `_OrderCard.cshtml` (card → Accepted: `--approved` chip, actions removed) |
| §3 Instructor opens dispute | `מחלוקת` button | `hx-get="?handler=DisputeForm&id={id}"` `hx-target="#order-card-{id}"` `hx-swap="outerHTML"` | `_OrderDisputeForm.cshtml` (card → inline notes textarea + submit/cancel) |
| §3 Instructor submits dispute | `שליחת מחלוקת` button | `hx-post="?handler=Dispute&id={id}"` `hx-target="#order-card-{id}"` `hx-swap="outerHTML"` | `_OrderCard.cshtml` (card → Disputed: `--rejected` chip + `.dispute-note` showing the notes) |

> The dispute submit is an **inline HTMX swap** (the card morphs into the notes form, then morphs to the Disputed state on submit) — **not** a separate page or a CSS modal. This keeps the instructor on their one "my class orders" screen and matches the in-place precedent. The `_OrderRow.cshtml` (admin) and `_OrderCard.cshtml` (instructor) are **two presentations of the same `Logistics_Order`** — admin sees the dense table row, the instructor sees the card. Both expose only the actions valid for their role + the order's current status (server-gated).

---

## 1. Logistics section + subnav

**Routes:** `/Logistics` (orders list — the section landing), `/Logistics/MyOrders` (instructor "my class orders"). Mirrors the People/Curriculum/Scheduling/Operations section pattern. **Topbar title `לוגיסטיקה`.**

The subnav is **role-aware** (same mechanism as Operations §1): Logistics/Admin manage the org-wide orders table; instructors act on their own class's orders. Reuse `.subnav` / `.subnav__item` exactly (the tinted rail, not glass, that scrolls with the body — confirmed in base.css).

```
┌─ glass glass--nav dash-topbar ─ עורקבי · לוגיסטיקה · {שלום, רון | מנהל} ───┐
  dash-body

  ┌ subnav (role-aware) ──────────────────────────────────────────────────────┐
  │  Logistics/Admin:   [הזמנות]                                               │
  │  Instructor:        [ההזמנות של הכיתה שלי]                                 │
  └────────────────────────────────────────────────────────────────────────────┘
```

**Destinations & exact Hebrew labels**

| Audience | Subnav items (in order) | Lands on |
|---|---|---|
| Logistics / Admin | `הזמנות` | `/Logistics` — the dense orders table (§2) |
| Instructor | `ההזמנות של הכיתה שלי` | `/Logistics/MyOrders` — accept/dispute cards (§3) |

> The section is deliberately single-destination per role this slice (one orders surface each) — the subnav still ships so Logistics matches the established section chrome and so future Logistics destinations (inventory, suppliers) have a home. An instructor who is also Admin sees both items.

---

## 2. Orders list — Logistics / Admin (dense)

**Data (spec §4):** `Logistics_Order` — `class_id`, `model_id`, `quantity`, `status` (Pending / Packed / Accepted / Disputed), `dispute_notes`, `delivered_at`.

**Route:** `/Logistics`, role `LogisticsOrAdmin`, dense. A `.data-table` of all orders. **Columns:** class · model (lesson-model, rendered as a light `.model-chip`) · quantity · status (`.status-chip`) · delivered date · actions. Filters by **status** (`.status-pill-rail`) and **class** (`.filter-select`). A **`צור הזמנות`** affordance (top-end of the page-head) triggers `SupplyPacingService` to seed Pending orders and HTMX-swaps the table body.

### 2a. Layout

```
┌ page-head ─ הזמנות לוגיסטיקה                              [ צור הזמנות ] ───┐
│  ┌ filter rail ─────────────────────────────────────────────────────────┐  │
│  │ [הכול][ממתין][נארז][התקבל][במחלוקת]      כיתה: [ כל הכיתות ▾ ]          │  │
│  └────────────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────────┘
┌ glass glass--tile people-panel (data-table) ─────────────────────────────────┐
│ כיתה   │ דגם                  │ כמות │ סטטוס      │ נמסר ב…   │ פעולות         │
│ ג׳2    │ ● קופסת אוצרות       │ 24   │ ●ממתין     │ —         │ [נארז]         │
│ ד׳1    │ ● מסגרת פסיפס        │ 18   │ ◑נארז      │ 24 ביוני  │ —              │  ← after Pack
│ ה׳3    │ ● דגם הר־געש         │ 30   │ ●התקבל     │ 22 ביוני  │ —              │
│ ג׳1    │ ● קופסת אוצרות       │ 24   │ ✕במחלוקת   │ 23 ביוני  │ —              │
│        │                      │      │            │ הערת מחלוקת: חסרו 3 ערכות │  ← .dispute-note
└────────────────────────────────────────────────────────────────────────────┘
```

**How it reads.**
- The **model** cell uses `.model-chip` lightly (brand-tinted pill + live dot) so the lesson-model kinship is visible without dominating the row — this is the slice's nod to the §9.4 signature element. (If a row is dense on narrow screens the chip wraps gracefully; the chip is flat tint, zero blur, table-safe.)
- **Quantity** renders `<bdi class="num">24</bdi>`.
- **Status** is the 4-state `.status-chip` (see §4 for the full mapping). `Pending → --pending`, **`Packed → --packed` (NEW variant)**, `Accepted → --approved`, `Disputed → --rejected`.
- **`נמסר ב…`** (delivered_at) shows `—` while Pending; once Packed it renders the date `<bdi class="num">24 ביוני</bdi>` (the moment Logistics marked it packed/sent). It stays populated through Accepted/Disputed.
- **Actions:** a Pending row shows one `נארז` button (accent ghost) → HTMX row-swap to Packed. Every other status shows `—` (the instructor, not Logistics, drives Packed→Accepted/Disputed; §3). A Disputed row appends a `.dispute-note` (NEW, tiny) line spanning the row's block-end so Admin sees *why* without leaving the table — this is the Admin-visible end of the dispute loop (the urgent Action-Item it also writes lives on `/Operations/ActionItems`, §6).

**Filters.** The status rail reuses `.status-pill-rail` / `.status-pill` (confirmed in base.css — the Classes filter rail). The class filter reuses `.filter-select` inside a `.filter-group`. Both are plain GET query-string filters (full nav, server re-renders the list) — **no HTMX needed for filtering**, matching the Classes precedent. Only Pack and Generate use HTMX.

**Generate orders.** `צור הזמנות` sits at the page-head trailing edge as a `.btn-primary` (it is the page's one primary verb). On click it `hx-post="?handler=Generate"` and swaps `#orders-body` (the `<tbody>`) with `_OrdersBody.cshtml` re-rendered to include the newly-seeded Pending rows; the new body fades in once via gated `lg-fade-up`. A `.htmx-indicator` spinner sits beside the button (reuse the existing `.htmx-indicator` opacity pattern). If the seeder produces nothing new, the server returns the same body (idempotent) — no error state needed.

### 2b. Exact Hebrew copy

| Element | Hebrew |
|---|---|
| Topbar title | `לוגיסטיקה` |
| Page title | `הזמנות לוגיסטיקה` |
| Generate button | `צור הזמנות` |
| Generate (in-progress, aria/indicator) | `יוצר הזמנות…` |
| Generate success (toast) | `ההזמנות נוצרו` |
| Status filter pills | `הכול` · `ממתין` · `נארז` · `התקבל` · `במחלוקת` |
| Class filter label / all-option | `כיתה:` · `כל הכיתות` |
| Columns | `כיתה` · `דגם` · `כמות` · `סטטוס` · `נמסר ב…` · `פעולות` |
| Status chips | `ממתין` (`--pending`) · `נארז` (`--packed`, NEW) · `התקבל` (`--approved`) · `במחלוקת` (`--rejected`) |
| Pack action | `נארז` (`btn-ghost--accent`) |
| Delivered-empty cell | `—` |
| Dispute-note line (Admin-visible) | `הערת מחלוקת: {dispute_notes}` |
| No orders at all (empty) | title `אין הזמנות` · hint `לחצו "צור הזמנות" כדי להתחיל.` |
| No orders for filter | title `אין הזמנות בסטטוס זה` · hint `שנו את הסינון או צרו הזמנות חדשות.` |

### 2c. Markup skeleton

```html
<div class="dash-body">
  <nav class="subnav" aria-label="ניווט לוגיסטיקה">
    <a class="subnav__item is-active" href="/Logistics">הזמנות</a>
  </nav>

  <div class="page-head">
    <div>
      <h1 class="page-head__title">הזמנות לוגיסטיקה</h1>
    </div>
    <button class="btn-primary"
            hx-post="?handler=Generate" hx-target="#orders-body" hx-swap="outerHTML">
      צור הזמנות
      <span class="htmx-indicator" aria-hidden="true">…</span>
    </button>
  </div>

  <!-- filter rail: status pills (GET) + class select (GET). Reuses Classes recipe. -->
  <form method="get" class="classes-filter-rail">
    <div class="status-pill-rail" role="tablist" aria-label="סינון לפי סטטוס">
      <button class="status-pill status-pill--on" name="status" value="">הכול</button>
      <button class="status-pill" name="status" value="Pending">ממתין</button>
      <button class="status-pill" name="status" value="Packed">נארז</button>
      <button class="status-pill" name="status" value="Accepted">התקבל</button>
      <button class="status-pill" name="status" value="Disputed">במחלוקת</button>
    </div>
    <div class="filter-group">
      <label class="form-field__label" for="ClassFilter">כיתה:</label>
      <select id="ClassFilter" name="classId" class="form-select filter-select"
              onchange="this.form.submit()">
        <option value="">כל הכיתות</option>
        <option value="3">ג׳2</option>
      </select>
    </div>
  </form>

  <div class="glass glass--tile people-panel" style="--lg-tile-shadow: var(--lg-shadow);">
    <table class="data-table">
      <thead>
        <tr>
          <th>כיתה</th><th>דגם</th><th>כמות</th><th>סטטוס</th><th>נמסר ב…</th><th>פעולות</th>
        </tr>
      </thead>
      <tbody id="orders-body">
        <!-- one _OrderRow per order; Pending row shown below -->
        <tr class="data-row" id="order-row-31">
          <td class="data-cell data-cell--primary">ג׳2</td>
          <td class="data-cell">
            <span class="model-chip"><span class="model-chip__dot" aria-hidden="true"></span>קופסת אוצרות</span>
          </td>
          <td class="data-cell"><bdi class="num">24</bdi></td>
          <td class="data-cell"><span class="status-chip status-chip--pending">ממתין</span></td>
          <td class="data-cell">—</td>
          <td class="data-cell data-cell--actions">
            <button class="btn-ghost btn-ghost--sm btn-ghost--accent"
                    hx-post="?handler=Pack&id=31" hx-target="#order-row-31" hx-swap="outerHTML">נארז</button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</div>
```

**`_OrderRow.cshtml` after the Pack swap** (status → Packed, delivered date filled, action cleared):
```html
<tr class="data-row" id="order-row-31">
  <td class="data-cell data-cell--primary">ג׳2</td>
  <td class="data-cell"><span class="model-chip"><span class="model-chip__dot" aria-hidden="true"></span>קופסת אוצרות</span></td>
  <td class="data-cell"><bdi class="num">24</bdi></td>
  <td class="data-cell"><span class="status-chip status-chip--packed">נארז</span></td>
  <td class="data-cell"><bdi class="num">24 ביוני</bdi></td>
  <td class="data-cell data-cell--actions">—</td>
</tr>
```

**A Disputed row** (read-only on the admin table — the dispute came from the instructor; admin sees the note + already has the urgent Action-Item on `/Operations/ActionItems`):
```html
<tr class="data-row" id="order-row-28">
  <td class="data-cell data-cell--primary">ג׳1</td>
  <td class="data-cell"><span class="model-chip"><span class="model-chip__dot" aria-hidden="true"></span>קופסת אוצרות</span></td>
  <td class="data-cell"><bdi class="num">24</bdi></td>
  <td class="data-cell">
    <span class="status-chip status-chip--rejected">במחלוקת</span>
    <span class="dispute-note">הערת מחלוקת: חסרו <bdi class="num">3</bdi> ערכות</span>
  </td>
  <td class="data-cell"><bdi class="num">23 ביוני</bdi></td>
  <td class="data-cell data-cell--actions">—</td>
</tr>
```

**Interaction / motion.** Pack → row swaps (`outerHTML`); the swapped row plays `lg-fade-up` (gated) — apply via the existing `.htmx-settling` hook on the row (mirror `#syllabus-models.htmx-settling`). The chip color change (pending warn → packed brand) is the confirmation. Generate → `#orders-body` swaps; the new body fades once. Reduced-motion: instant swaps. **No undo** this slice (flagged §7), matching the Slice-2/3 single-action precedent.

---

## 3. Instructor order action — "my class orders" (instructor scale)

**Route:** `/Logistics/MyOrders`, role `InstructorOrAdmin`, instructor scale. The instructor sees the orders **for their own class** as a list of cards (one per order). For orders in status **`Packed`**, the card offers **`התקבל` (Accept)** and **`מחלוקת` (Dispute)**. Accept is a one-tap confirm; Dispute opens an inline notes textarea (`dispute_notes`) before submit. Pending orders (not yet packed by Logistics) and already-resolved orders (Accepted/Disputed) are **read-only** cards.

> **Why cards, not a table.** The instructor is mobile-first and acts on few orders; an order here reads like a short actionable ticket (model + quantity + a primary decision + an optional notes form), which is exactly the shape the existing `.action-card` already serves (Slice 3). Reusing `.action-card` (opaque tinted, the panel is the glass once) means **zero new layout CSS** for this surface — only the inline dispute form and the disputed-note read-line are added, and both reuse existing recipes.

### 3a. Layout (the three states of one card)

```
┌ glass glass--nav dash-topbar ─ עורקבי · לוגיסטיקה · ההזמנות של הכיתה שלי ──┐
  dash-body  ┌ subnav ─ [ההזמנות של הכיתה שלי] ──────────────────────────────┐

  ┌ glass glass--tile people-panel form-panel--instructor ───────────────────┐
  │                                                                          │
  │  ┌ action-card ─ Packed (actionable) ────────────────────────────────┐  │
  │  │ ● קופסת אוצרות · כיתה ג׳2                          ◑נארז           │  │  ← model-chip + status
  │  │ כמות: 24 · נמסר ב־24 ביוני                                          │  │
  │  │                                       [ מחלוקת ]   [ התקבל ]        │  │
  │  └──────────────────────────────────────────────────────────────────────┘  │
  │                                                                          │
  │  ┌ action-card ─ dispute form open (after [מחלוקת]) ──────────────────┐  │
  │  │ ● קופסת אוצרות · כיתה ג׳2                          ◑נארז           │  │
  │  │ כמות: 24 · נמסר ב־24 ביוני                                          │  │
  │  │ ── מה הבעיה? ──                                                     │  │
  │  │ [ textarea: חסרו ערכות, פריט פגום, כמות שגויה…                 ]    │  │
  │  │                                  [ ביטול ]   [ שליחת מחלוקת ]       │  │
  │  └──────────────────────────────────────────────────────────────────────┘  │
  │                                                                          │
  │  ┌ action-card is-resolved ─ Accepted ───────────────────────────────┐  │
  │  │ ● דגם הר־געש · כיתה ג׳2                            ●התקבל          │  │
  │  │ כמות: 30 · נמסר ב־22 ביוני                       ✓ אושרה הקבלה     │  │
  │  └──────────────────────────────────────────────────────────────────────┘  │
  │                                                                          │
  │  ┌ action-card is-resolved ─ Disputed ───────────────────────────────┐  │
  │  │ ● קופסת אוצרות · כיתה ג׳1                          ✕במחלוקת        │  │
  │  │ כמות: 24 · נמסר ב־23 ביוני                                          │  │
  │  │ הערת מחלוקת: חסרו 3 ערכות                                           │  │  ← .dispute-note
  │  └──────────────────────────────────────────────────────────────────────┘  │
  └────────────────────────────────────────────────────────────────────────────┘
```

**How it reads.**
- The panel is `.form-panel--instructor`-scaled so the card text + buttons are thumb-sized on a phone (reuses the Slice-3 instructor-scale modifier verbatim — it bumps title/label/field/segment sizes; here it lifts the card body to instructor scale via the `.action-card__desc` font already at `--t-admin-body` → bumped by an added rule, see §5 NEW).
- Each card's top row = `.model-chip` (model + dot) + `כיתה {N}` + the right-aligned `.status-chip`.
- The body line = `כמות: <bdi class="num">24</bdi> · נמסר ב־<bdi class="num">24 ביוני</bdi>`.
- **Packed card:** two buttons in `.action-card__actions` — `מחלוקת` (plain `.btn-ghost--sm`, the cautious secondary) and `התקבל` (`.btn-ghost--sm.btn-ghost--accent`, the affirmative primary). Accept is leading-most/trailing per the existing `justify-content:flex-end` (both sit at the trailing edge; Accept is the rightmost = inline-start in RTL = the natural primary position).
- **Accept** → one tap → card swaps to `.is-resolved` Accepted state: `--approved` chip, actions replaced by a `✓ אושרה הקבלה` resolved-meta (reuse `.action-card__resolved-meta`, already `--ok`).
- **Dispute** → `מחלוקת` swaps the card to its **dispute-form** state (inline): the body gains a `.form-field` with `textarea` (the existing recessed-well recipe, instructor-scaled) labelled `מה הבעיה?`, plus `ביטול` (cancels back to the Packed card) / `שליחת מחלוקת` (submits). On submit → card swaps to `.is-resolved` Disputed state: `--rejected` chip + a `.dispute-note` line echoing the entered notes. The submit **also** writes the urgent Logistics `Action_Item` (server-side) that appears on `/Operations/ActionItems` — **no UI here for that** (§6).

### 3b. Exact Hebrew copy

| Element | Hebrew |
|---|---|
| Topbar / subnav title | `ההזמנות של הכיתה שלי` |
| Card model+class line | `{model} · כיתה {כיתה}` (model inside `.model-chip`) |
| Card quantity+delivered line | `כמות: <bdi class="num">{N}</bdi> · נמסר ב־<bdi class="num">{D ב{חודש}}</bdi>` |
| Accept action | `התקבל` (`btn-ghost--accent`) |
| Dispute action | `מחלוקת` (`btn-ghost`) |
| Accepted resolved-meta (after) | `✓ אושרה הקבלה` |
| Dispute form label | `מה הבעיה?` |
| Dispute textarea placeholder | `חסרו ערכות, פריט פגום, כמות שגויה…` |
| Dispute submit / cancel | `שליחת מחלוקת` · `ביטול` |
| Dispute validation (empty notes) | `יש לפרט את הבעיה` |
| Dispute submit success (inline confirm via swap) | (the row-swap to Disputed *is* the confirmation; optional toast `המחלוקת נשלחה`) |
| Disputed read-line (after) | `הערת מחלוקת: {dispute_notes}` |
| Status chips | `נארז` (`--packed`) · `התקבל` (`--approved`) · `במחלוקת` (`--rejected`) · `ממתין` (`--pending`, read-only on a not-yet-packed order) |
| Empty (no orders for my class) | title `אין הזמנות לכיתה שלך` · hint `הזמנות חדשות יופיעו כאן כשייווצרו.` |

### 3c. Markup skeleton

**`_OrderCard.cshtml` — Packed (actionable) state**
```html
<article class="action-card" id="order-card-31">
  <div class="action-card__top">
    <span class="model-chip"><span class="model-chip__dot" aria-hidden="true"></span>קופסת אוצרות</span>
    <span class="action-card__class">כיתה ג׳2</span>
    <span class="status-chip status-chip--packed" style="margin-inline-start:auto;">נארז</span>
  </div>
  <p class="action-card__desc">
    כמות: <bdi class="num">24</bdi> · נמסר ב־<bdi class="num">24 ביוני</bdi>
  </p>
  <div class="action-card__actions">
    <button class="btn-ghost btn-ghost--sm"
            hx-get="?handler=DisputeForm&id=31" hx-target="#order-card-31" hx-swap="outerHTML">מחלוקת</button>
    <button class="btn-ghost btn-ghost--sm btn-ghost--accent"
            hx-post="?handler=Accept&id=31" hx-target="#order-card-31" hx-swap="outerHTML">התקבל</button>
  </div>
</article>
```

**`_OrderDisputeForm.cshtml` — inline dispute form state** (same card id; morphs in place)
```html
<article class="action-card" id="order-card-31">
  <div class="action-card__top">
    <span class="model-chip"><span class="model-chip__dot" aria-hidden="true"></span>קופסת אוצרות</span>
    <span class="action-card__class">כיתה ג׳2</span>
    <span class="status-chip status-chip--packed" style="margin-inline-start:auto;">נארז</span>
  </div>
  <p class="action-card__desc">כמות: <bdi class="num">24</bdi> · נמסר ב־<bdi class="num">24 ביוני</bdi></p>

  <form class="form-field form-field--full"
        hx-post="?handler=Dispute&id=31" hx-target="#order-card-31" hx-swap="outerHTML">
    <label class="form-field__label" for="DisputeNotes">מה הבעיה? <span class="req">*</span></label>
    <textarea id="DisputeNotes" name="DisputeNotes"
              placeholder="חסרו ערכות, פריט פגום, כמות שגויה…" required></textarea>
    <span class="form-field__error" data-valmsg-for="DisputeNotes"></span>
    <div class="action-card__actions" style="gap:var(--sp-3);">
      <button type="button" class="btn-ghost btn-ghost--sm"
              hx-get="?handler=Card&id=31" hx-target="#order-card-31" hx-swap="outerHTML">ביטול</button>
      <button type="submit" class="btn-ghost btn-ghost--sm btn-ghost--accent">שליחת מחלוקת</button>
    </div>
  </form>
</article>
```

**`_OrderCard.cshtml` after Accept** (resolved, affirmative):
```html
<article class="action-card is-resolved" id="order-card-31">
  <div class="action-card__top">
    <span class="model-chip"><span class="model-chip__dot" aria-hidden="true"></span>דגם הר־געש</span>
    <span class="action-card__class">כיתה ג׳2</span>
    <span class="status-chip status-chip--approved" style="margin-inline-start:auto;">התקבל</span>
  </div>
  <p class="action-card__desc">כמות: <bdi class="num">30</bdi> · נמסר ב־<bdi class="num">22 ביוני</bdi></p>
  <span class="action-card__resolved-meta">✓ אושרה הקבלה</span>
</article>
```

**`_OrderCard.cshtml` after Dispute submit** (resolved, disputed + notes):
```html
<article class="action-card is-resolved" id="order-card-28">
  <div class="action-card__top">
    <span class="model-chip"><span class="model-chip__dot" aria-hidden="true"></span>קופסת אוצרות</span>
    <span class="action-card__class">כיתה ג׳1</span>
    <span class="status-chip status-chip--rejected" style="margin-inline-start:auto;">במחלוקת</span>
  </div>
  <p class="action-card__desc">כמות: <bdi class="num">24</bdi> · נמסר ב־<bdi class="num">23 ביוני</bdi></p>
  <span class="dispute-note">הערת מחלוקת: חסרו <bdi class="num">3</bdi> ערכות</span>
</article>
```

**Interaction / motion.** All three transitions are HTMX `outerHTML` swaps of `#order-card-{id}`; each swapped fragment plays `lg-fade-up` (gated, via `.htmx-settling`). The status-chip recolor + the dim-to-`.is-resolved` are the confirmation. `מחלוקת` → form morph (no nav). `ביטול` re-fetches the Packed card. Reduced-motion: instant. The dispute textarea is the existing recessed-well `.form-field textarea` — the panel's `.form-panel--instructor` scale lifts it to instructor size automatically.

---

## 4. Status chips for the 4 states

Three of the four states map onto **existing** `status-chip` variants verbatim; only **Packed** needs a new variant. The chip carries both a dot/glyph **and** the Hebrew label (never color-alone — accessibility, §5 / Slice-3 self-check).

| `status` | Hebrew | Variant | Dot/glyph | Hue source | Status |
|---|---|---|---|---|---|
| Pending | `ממתין` | `.status-chip--pending` | `●` | `--warn` / `--warn-soft` | **reuse** |
| Packed | `נארז` | `.status-chip--packed` | `◑` (half-filled = "in transit / packed, awaiting receipt") | **`--brand` / brand-soft** | **NEW** |
| Accepted | `התקבל` | `.status-chip--approved` | `●` | `--ok` / `--ok-soft` | **reuse** |
| Disputed | `במחלוקת` | `.status-chip--rejected` | `✕` | `--absent` / `--absent-soft` | **reuse** |

**Why a new `--packed` variant (not reuse `--active`).** `--active` already means "live/on" (`--ok` green, used for open Action-Items and active classes) — reusing it for Packed would read as "done/good", colliding with Accepted's green. Packed is a **mid-flow, brand-neutral** state ("we sent it, awaiting the instructor's receipt"), so it earns the **Blue-Jay brand** hue — distinct from the warn/ok/absent semantic trio, and it reinforces the brand at the one neutral step. The `◑` glyph (vs the solid `●`) signals "half-way through the loop" at a glance. This is the slice's single new chip variant, exactly as the brief anticipated.

**NEW CSS (the one chip variant):**
```css
/* Packed — mid-flow brand-neutral state (distinct from --active/--approved green).
   Flat brand tint, zero backdrop-filter (lives in scrolling tables/cards). */
.status-chip--packed { background: var(--info); color: var(--brand); }   /* --info = rgba(brand,.12) */
.status-chip--packed::before { content: "◑"; font-size: 9px; line-height: 1; }
```
> `--info` (`rgba(var(--brand-rgb), .12)`) is confirmed present in tokens.css. `--brand` text on a 12%-brand tint over the `--lg-fill-strong` (.80) panel clears AA for the chip's `--t-admin-meta` semibold label (same contrast class as the existing `.hours-chip`/`.model-chip` brand-on-tint pattern already shipped). If the implementer wants a touch more punch, `background: var(--lg-fill-tint)` (8% brand) is the alternative — but `--info` (12%) is preferred for a hair more presence at this mid-flow step.

---

## 5. NEW CSS — shared additions to `base.css` (consolidated, paste-ready)

> Append after the Slice-3 blocks, **before** the `@supports not (...)` fallback. **No new tokens required** — every token used (`--brand`/`--brand-rgb`, `--info`, `--lg-fill-tint`, `--radius-pill`, `--t-admin-meta`/`--t-admin-body`/`--t-body`/`--t-label`, `--sp-*`, `--fw-*`, `--brand-ink-muted`, `--absent`) is confirmed present in the live `tokens.css`. If any is missing, STOP and flag — do not invent.
> This is **tiny** — the slice's entire CSS footprint is: one status-chip variant, one dispute-note read-line, one small class-label span, and one instructor-scale bump for the reused `.action-card`. Everything else (data-table, status pills, model-chip, action-card, form-field textarea, filter rail, empty-state, btn-ghost) is reused verbatim.

```css
/* ============================================================
   SLICE 4 — Logistics (dispute loop)
   Reuses verbatim: .subnav, .page-head, .data-table(+.data-row/.data-cell/
   .data-cell--primary/--actions), .status-chip(+--pending/--approved/--rejected),
   .status-pill-rail/.status-pill, .filter-select/.filter-group/.classes-filter-rail,
   .model-chip, .action-card(+__top/__desc/__actions/__resolved-meta/.is-resolved),
   .form-field(+--full)/textarea (recessed-well), .form-panel--instructor,
   .btn-primary/.btn-ghost(+--sm/--accent), .empty-state, .htmx-indicator,
   .num, .req. Perf: topbar is the only blur; panel is glass once; rows/cards opaque.
   NO .glass--lensed, NO .glass--clear under data text.
   ============================================================ */

/* ---- §4 Packed status chip (the ONE new chip variant) ----
   Mid-flow brand-neutral state — distinct from --active/--approved green.
   Flat brand tint, zero backdrop-filter (table/card-safe). */
.status-chip--packed { background: var(--info); color: var(--brand); }
.status-chip--packed::before { content: "◑"; font-size: 9px; line-height: 1; }

/* ---- §2/§3 Dispute-note read-line — shows dispute_notes after a Disputed swap.
   Sibling-of-chip in the admin status cell; standalone line in the instructor card.
   Tiny muted-meta, analogous to .approve-meta. Notes wrap; quantities go in <bdi>. */
.dispute-note {
  display: block; margin-block-start: 2px;
  font-size: var(--t-admin-meta); color: var(--brand-ink-muted);
  line-height: var(--lh-snug);
}
/* Instructor card scale: the disputed note reads at instructor meta inside an
   instructor-scaled panel (so it isn't dense-tiny on a phone). */
.form-panel--instructor .dispute-note { font-size: var(--t-meta); }

/* ---- §3 Order-card class label (the "כיתה N" beside the model-chip) ---- */
.action-card__class {
  font-size: var(--t-admin-meta); font-weight: var(--fw-semibold);
  color: var(--brand-ink-muted);
}

/* ---- §3 Instructor-scale lift for the reused .action-card body on MyOrders.
   .action-card defaults to dense (--t-admin-*); inside an instructor-scaled
   panel the card body + meta rise to instructor scale for thumb reading. ---- */
.form-panel--instructor .action-card__desc       { font-size: var(--t-body); }
.form-panel--instructor .action-card__class       { font-size: var(--t-label); }
.form-panel--instructor .action-card__resolved-meta { font-size: var(--t-label); }
```

**`@supports not (...)` fallback:** nothing to add. The packed chip, dispute-note, class label, and instructor bumps carry **zero** `backdrop-filter`; the order cards reuse `.action-card` (already opaque); the dispute textarea reuses `.form-field textarea`, which is **already** covered by the existing fallback line (`.form-field input, .form-field select, .form-field textarea, .form-select { background: var(--lg-fill-solid); }`). **No new fallback lines required** — flagged for the implementer to confirm rather than blindly append.

---

## 6. Automations → existing `/Operations/ActionItems` (NO new UI)

**Explicit hand-off — this is binding.** The Slice-4 automations have **zero** new UI surfaces. They all surface as `Action_Item`s on the **already-shipped Slice-3 `/Operations/ActionItems`** page, using the **existing** `.action-card` + `.action-type` components (both in base.css, and `.action-type` already supports the full enum):

| Automation | `Action_Item.type` | Existing badge class | Existing Hebrew label | Existing dot |
|---|---|---|---|---|
| **Disputed order** (the §3 dispute branch) | `Dispute` | `.action-type--dispute` | `מחלוקת` | `--absent` |
| Birthday | `Birthday` | `.action-type--birthday` | `יום הולדת` | `--brand-tint` |
| Absence | `Absence` | `.action-type--absence` | `היעדרות` | `--absent` |
| Dropout / tryout follow-up | `Tryout_Followup` | `.action-type--tryout` | `מעקב ניסיון` | `--warn` |
| Generic scheduler task | `Task` | `.action-type` (base) | `משימה` | `--brand` |

The dispute submit (§3) writes an **urgent** `Action_Item` (type=`Dispute`, `assigned_to_role=Admin/Logistics`, `related_entity_id`=the order, `description` = a server-generated Hebrew line e.g. `מחלוקת על הזמנה · כיתה ג׳1 · דגם "קופסת אוצרות": חסרו 3 ערכות`, `due_date` near-term). It appears on `/Operations/ActionItems` as an `.action-card` with `.action-type--dispute`, resolvable via the existing `סמן כטופל` swap. **No design work here** — the page, card, badge, resolve flow, and copy already exist (Slice-3 §5). The dropout/absence/birthday/tryout Action-Items are likewise pure server emissions into the same page.

> If the Slice-4 Action-Item descriptions want richer per-type copy than Slice-3's Gap example, that is a **server string** concern (the `description` field), not a UI/CSS change — the card renders whatever Hebrew the server writes. Flagged as a server detail, not designed here.

---

## 7. Legibility & perf self-check (contract §5 / budget §10)

- **Blur budget:** every Slice-4 page = topbar (1 blur) + one `.glass--tile` panel (2). The orders table rows are opaque `.data-row`; the instructor cards are opaque `.action-card`; chips/pills/model-chip carry zero `backdrop-filter`. **No `.glass--lensed`, no `.glass--clear` under data text.** ✔ §10.1/10.2/10.3, §5.1
- **Data text on safe fill:** orders table + instructor cards sit on `--lg-fill-strong` (.80) panels; the dispute textarea is the recessed-well tinted fill (existing, AA-cleared in Slice 1). ✔ §5.1
- **Packed chip contrast:** `--brand` text on `--info` (12% brand) over the .80 panel is the same brand-on-tint class as the shipped `.model-chip`/`.hours-chip` — clears AA at semibold `--t-admin-meta`. ✔ §5.2/5.4
- **Not color-alone:** all four status chips carry the Hebrew label + a distinct glyph (`●` pending, `◑` packed, `●` approved, `✕` rejected); the model-chip carries the model name + dot. ✔ accessibility
- **RTL:** logical properties only; quantities/dates/counts in `<bdi class="num">`; Hebrew months/days; status pills + class select flip for free; no glyph mirroring beyond `.icon-directional` (not needed this slice). The `◑` half-fill glyph is symmetric-enough to not require mirroring; if it ever reads directionally, it is decorative beside the label and may stay fixed. ✔ §7
- **Motion:** confirmations only (Pack/Accept/Dispute row/card swaps + Generate body swap via gated `lg-fade-up`/`.htmx-settling`; chip recolor = confirmation); no entrance staggers; reduced-motion inherited. ✔ §8
- **Reuse vs invention:** **2 real additions** (`.status-chip--packed`, `.dispute-note`) + 1 trivial label span (`.action-card__class`) + 3 one-line instructor-scale bumps. Data-table, status-pill rail, filter-select, model-chip, action-card, form-field textarea, empty-state, btn-ghost, form-panel--instructor — all reused verbatim. ✔ restraint

---

## 8. Deferred / out-of-scope (designer-flagged, per global rule §5)

These are explicit, not silent:

- **Rich Action Hub → Slice 5 (scoped by the build plan, not deferred by me).** The Disputed-order urgent `Action_Item` and the other Slice-4 automations surface on the **minimal** Slice-3 `/Operations/ActionItems` read (§6). The live-polling, unified inbox, assignment/ticketing, and command-center bento remain Slice 5. The `.action-type` enum already supports `Dispute`/`Birthday`/`Absence`/`Tryout_Followup`, so nothing new is needed for these to appear.
- **No undo after a status change** — Pack (Logistics), Accept and Dispute (instructor) are single-action with **no undo** this slice, matching the Slice-2 substitution / Slice-3 approval precedent (which also have no undo). If a toast-undo (like the Slice-1 un-enroll `.toast`) is wanted, say so — it's a small addition using the existing `.toast`, not built here.
- **Admin cannot re-pack / resolve a dispute from the orders table this slice** — a Disputed order shows the note (read-only) on the admin table; the *resolution* happens on `/Operations/ActionItems` (resolve the urgent item) and/or by re-running the loop. A "re-send / re-pack a disputed order" admin action is **not** designed here — flag if the workflow needs it (it would be one more `_OrderRow` action button on Disputed rows).
- **Generate-orders scope/idempotency** — the design assumes `SupplyPacingService` is server-driven and idempotent (re-running seeds only genuinely-new Pending orders). The seeding rules (which classes/models, how many) are a server/business concern; I did not design them. The UI just swaps in whatever rows the server returns.
- **`delivered_at` semantics** — the UI renders the server's `delivered_at` (populated when Logistics marks Packed). Whether "delivered" means "shipped/packed" vs "physically received" is a domain decision; the column label `נמסר ב…` reads naturally for either. Flag if you want a distinct "packed at" vs "received at" pair (would be one more column).
- **Filter persistence / "my class" resolution** — the status/class GET filters and the instructor→class mapping (which class an instructor "owns" for `/Logistics/MyOrders`) are server concerns, not designed here.
- **Quantity editing** — orders are read-only quantities in this UI (Logistics packs the seeded quantity; the instructor accepts or disputes). No quantity-edit control is designed. If Logistics needs to adjust quantity before packing, flag it (would reuse a `.form-field input.num` inline).

**Everything else in the brief is specified. No other items deferred.**
