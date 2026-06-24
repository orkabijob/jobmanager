# Orkabi — Slice 2 "Curriculum + Scheduling" Liquid-Glass Design Spec

> **For:** the implementer subagents. **Status:** design spec, not production code.
> **Binding above this doc:** `docs/design/liquid-glass-design-system.md`, plus the **actual** `wwwroot/css/tokens.css` + `wwwroot/css/base.css`. This spec uses the **real, elevated** token names — confirmed present this session: `--radius-card` is **22px**, `--radius-hero`/`--radius-sheet` **34/30px**, `--lg-fill-strong` is **.80**, `--lg-fill-deep` **.88**, the backdrop is the **photo** `bg-liquid.jpg` + scrim, `.glass--lensed` uses the **`.glass__lens` text-immune child** (NOT `filter` on the tile), and `--brand-ink-muted`/`--brand-ink-faint`/`--warn`/`--warn-soft`/`--ok`/`--ok-soft`/`--absent`/`--absent-soft`/`--info`/`--ls-wide`/`--ls-widest`/`--dur-instant` all exist.
> **IMPLEMENTER RULE:** before using any token below, confirm it exists in the real `tokens.css`. If a token named here is missing, STOP and flag it — do not invent a value.
> **Goal of the slice:** "Instructors take attendance; admins build syllabi and schedules." Five surfaces below; the **Attendance screen (§2)** is THE signature.
> **HTMX lands this slice.** Surfaces marked HTMX swap server-rendered partials; the attendance screen is the one place using the thin REST API + a small Motion-One slice.

---

## 0. Conventions that apply to every surface

**Two scales, one shell.** Every page reuses the existing shell exactly as `Instructor.cshtml` / `Classes/Index.cshtml` do:
```html
<div class="dash-shell">
  <header class="glass glass--nav dash-topbar"> … </header>   <!-- ONE blur layer, fixed/sticky -->
  <main class="dash-body"> … </main>
</div>
```
The topbar is the **only** `glass--nav` (blur) on screen. The attendance screen replaces it with a **bottom sheet** as the second blur surface — that is the one place a sheet header may use `.glass--lensed` (desktop/hero only).

- **Surfaces 1, 2, 3 (instructor)** = mobile-first **instructor scale**: base `--t-body` (17px), `--t-title` (21px), `--t-label` (15px), `--t-meta` (13px), CTA `--t-hero-cta` (30px). Thumb zones, fast motion (≤200ms).
- **Surfaces 4, 5 (admin/CS)** = **dense admin scale**: base `--t-admin-body` (15px), `--t-admin-label` (13px), `--t-admin-meta` (12px), titles `--t-admin-title`/`--t-admin-display`. Reuse `.subnav`, `.data-table`, `.form-field`, `.segment`, `.status-chip`, `.empty-state` exactly as Slice 1.

**Glass-tier discipline (legibility §5 / perf §10).**
- A page is **one glass panel** (`.glass .glass--tile` = `--lg-fill-strong` .80, the data-text-safe fill) + the fixed topbar. Set `--lg-tile-shadow: var(--lg-shadow)` inline on management panels (lighter bevel than the bento tile default), as Slice 1 does.
- **`.glass--lensed` appears on exactly ONE surface this slice:** the attendance bottom-sheet **header** (desktop/hero only, dropped on mobile + reduced settings by the existing media queries). Nowhere else. Admin syllabus/schedule are dense data — lensing banned there (Slice-1 rule continues).
- Rows inside any scrolling list (attendance rows, syllabus model rows, shift-instance rows, sub-request rows) are **opaque tinted fills**, never glass — the panel is the glass once. Pills/chips/badges carry zero `backdrop-filter`.
- **No `.glass--clear` under data text** (§5.1).

**RTL + numerals (§7).** Logical properties only; no `[dir=rtl]` file. Wrap every time, date, count, "X of N", phone in `<bdi class="num">…</bdi>`. **Days of week always in Hebrew:** `ראשון · שני · שלישי · רביעי · חמישי · שישי · שבת`. Times in `<bdi>` (`<bdi>16:30</bdi>`). Time **ranges** wrap the whole range in one `<bdi>` so the en-dash doesn't reorder: `<bdi class="num">16:00–17:30</bdi>`. Directional glyphs (breadcrumb chevron, reorder arrows that imply flow) get `.icon-directional`; **checkmarks, the Present/Absent fill direction, the up/down reorder arrows, status dots, the model-chip dot never mirror as glyphs** — but the attendance fill *direction* is handled by logical properties, so it flips for free (fill grows from the **leading/inline-start = right** edge in RTL).

**Motion (§8).** Animate **only confirmations**: the attendance Present/Absent fill, the submit count-up, the sheet open (materialize), the "X of N" pill flip on HTMX swap, a syllabus row reorder settle. **No** per-row entrance staggers on instructor surfaces (opened ~20×/day — instant). Instructor motion ≤200ms (`--dur-fast`/`--dur-instant`); sheet open uses `--ease-glass` ~440ms (`--dur-slow`). Everything already globally gated by `prefers-reduced-motion` in tokens.css; new keyframes are additionally wrapped in `@media (prefers-reduced-motion: no-preference)`.

**HTMX fragment map (what swaps, never a full reload).**
| Surface | Trigger | `hx-*` | Swapped fragment |
|---|---|---|---|
| §2 Attendance | tap/submit | **NOT HTMX — thin REST API** (`POST /api/attendance`, idempotency key, optimistic) | row state is client-side; server returns 200/conflict |
| §3 Lesson-log | model `<select>` change | `hx-post` `hx-target="#lesson-pacing"` `hx-swap="outerHTML"` | `_LessonPacing.cshtml` (the "X of N" chip + bar) |
| §3 Lesson-log | status segment / notes save | `hx-post` `hx-target="#lesson-status"` `hx-swap="outerHTML"` | `_LessonStatus.cshtml` |
| §4 Syllabus | up/down reorder | `hx-post` `hx-target="#syllabus-models"` `hx-swap="outerHTML"` | `_SyllabusModelList.cshtml` |
| §5 Schedule | "generate instances" | `hx-post` `hx-target="#instances-panel"` `hx-swap="outerHTML"` `hx-indicator` | `_InstanceList.cshtml` |
| §5 Substitution | request / approve | `hx-post` `hx-target="closest .sub-row"` `hx-swap="outerHTML"` | `_SubRow.cshtml` |

> Precedent already in repo: `People/Classes/_RosterRow.cshtml`. Keep partials named `_X.cshtml`, each rendering a single swappable node with a stable `id`. Anti-forgery: HTMX must send `RequestVerificationToken` (one global `hx-headers` on `<body>` reading the page's token — note for the implementer, not a design constraint).

---

## 1. Instructor home / "today" screen — THE signature instructor surface

**Route:** `/Dashboard/Instructor` (role `InstructorOrAdmin`) — replaces the current stub. Mobile-first, instructor scale.
**Shows:** today's shift(s) for the logged-in instructor (date-scoped per workflow §B: only `actual_instructor_id == me AND date == today` are openable), the current **lesson-model chip** (§9.4) per shift, and the SOLID Blue-Jay **"קח נוכחות" monolith** (§9.1) — ~40% viewport, lower-center thumb zone.

### Wireframe (mobile, 1 col)
```
┌─ glass glass--nav dash-topbar ─────────────────────────────┐
│ עורקבי              מדריך              שלום, רון  [יציאה]    │
└────────────────────────────────────────────────────────────┘
  dash-body (today-stage — narrower max-inline, centered)

  ┌─ today-head ───────────────────────────────────────────┐
  │  שלום, רון                                              │
  │  יום שלישי · 24 ביוני                          ← Heb day │
  └────────────────────────────────────────────────────────┘

  ── המפגשים שלי היום ────────────────────────────  (section-label)

  ┌─ glass glass--tile shift-card  (today's shift) ────────┐
  │  ┌ model-chip (brand-tint) ─┐          16:00–17:30      │  ← time bdi, inline-end
  │  │ ● דגם: קופסת אוצרות      │                           │
  │  └──────────────────────────┘                          │
  │  כיתה ג׳2 · עירוני א׳                                   │
  │  רשומים 18 · נוכחות אחרונה: 14 ✓                        │
  │                                                         │
  │  ┌══════ hero-solid "קח נוכחות" MONOLITH ══════════┐    │
  │  ║              קח נוכחות                          ║    │  ← ~40% vp, thumb zone
  │  ║         18 תלמידים · 16:00                       ║    │
  │  └════════════════════════════════════════════════┘    │
  └────────────────────────────────────────────────────────┘

  ┌─ glass shift-card  (later shift, locked until its time?) ┐
  │  … same, monolith present but secondary CTA style …      │
  └─────────────────────────────────────────────────────────┘

  ── אין עוד מפגשים היום ──  (when only one)
```
> If the instructor has **no shift today**: a single `.empty-state` inside one glass panel — no monolith. Copy in table below. The monolith only renders when there is an openable shift.

### How it reads
- **`today-head`** — `שלום, {שם}` at `--t-title`/700, and the date line `{יום בעברית} · <bdi class="num">24 ביוני</bdi>` at `--t-label`, muted. The Hebrew weekday is the recognizer; the numeric date is `<bdi>`.
- **`shift-card`** is one `.glass .glass--tile` per shift (instructor rarely has >2–3/day, so 2–3 glass panels max is within the ≤2 *blur* budget because cards stacked vertically rarely co-occupy the viewport with the topbar AND each other — but to be safe the cards are `.glass--tile` static panels, the only fixed blur is the topbar). Inside each card, top-to-bottom: **model-chip** (leading) + **time** (inline-end, `<bdi class="num">16:00–17:30</bdi>`), then class + school line `--t-body`, then a one-line roster summary `--t-meta` muted, then the **monolith**.
- **The monolith** (`.hero-solid`, §9.1) is the card's primary action and the screen's anchor: opaque Blue-Jay gradient, `--radius-hero`, `--lg-shadow-hero` glow. Label `קח נוכחות` at `--t-hero-cta`/700 white (≈6.8:1 AA), sub-line `<bdi class="num">18</bdi> תלמידים · <bdi>16:00</bdi>` at `--t-label`/500 at 78% white. It is a real link/`<button form>` to the attendance screen. ~40% viewport height via `min-block-size`. Press → `scale(.97)` + glow tightens (existing `.hero-solid` has no press state yet, so §6 adds `.hero-cta` press CSS).
- **Date-scoping (workflow §B):** a future shift whose time hasn't arrived is shown but its monolith is **disabled** (still opaque, but `--brand-deep` flat + `cursor:not-allowed` + sub-line `נפתח ב־<bdi>16:00</bdi>`). Authorization is server-checked; the UI mirrors it. **Do not** hide future shifts — the instructor wants to see the day.

### Exact Hebrew copy
| Element | Hebrew |
|---|---|
| Topbar title | `מדריך` |
| Greeting | `שלום, {שם}` |
| Date line | `{יום בעברית} · <bdi class="num">{D ב{חודש}}</bdi>` (e.g. `יום שלישי · 24 ביוני`) |
| Section label | `המפגשים שלי היום` |
| Model chip prefix | `דגם:` then model name (e.g. `דגם: קופסת אוצרות`) |
| Class line | `{כיתה} · {בית ספר}` |
| Roster summary | `רשומים <bdi class="num">18</bdi> · נוכחות אחרונה: <bdi class="num">14</bdi> ✓` |
| Monolith label | `קח נוכחות` |
| Monolith sub-line | `<bdi class="num">18</bdi> תלמידים · <bdi>16:00</bdi>` |
| Monolith disabled sub-line | `נפתח ב־<bdi>16:00</bdi>` |
| "Only one shift" footer | `אין עוד מפגשים היום` |
| Empty (no shifts) | title `אין לך מפגשים היום` · hint `כשישובץ לך מפגש הוא יופיע כאן.` |
| No current model set on the shift's syllabus | chip reads `טרם הוגדר דגם נוכחי` in `--warn` tint (chip variant `.model-chip--warn`) |

### Markup skeleton (real classes)
```html
<main class="dash-body today-stage">
  <header class="today-head">
    <h1 class="today-head__greeting">שלום, רון</h1>
    <p class="today-head__date">יום שלישי · <bdi class="num">24 ביוני</bdi></p>
  </header>

  <div class="section-label">המפגשים שלי היום</div>

  <article class="glass glass--tile shift-card" style="--lg-tile-shadow: var(--lg-shadow);">
    <div class="shift-card__head">
      <span class="model-chip">
        <span class="model-chip__dot" aria-hidden="true"></span>
        דגם: קופסת אוצרות
      </span>
      <bdi class="shift-card__time num">16:00–17:30</bdi>
    </div>
    <p class="shift-card__class">כיתה ג׳2 · עירוני א׳</p>
    <p class="shift-card__meta">רשומים <bdi class="num">18</bdi> · נוכחות אחרונה: <bdi class="num">14</bdi> ✓</p>

    <a class="hero-solid hero-cta" href="/Attendance/12">
      <span class="hero-cta__label">קח נוכחות</span>
      <span class="hero-cta__sub"><bdi class="num">18</bdi> תלמידים · <bdi>16:00</bdi></span>
    </a>
  </article>

  <!-- later shift: same article; if locked add hero-cta--locked + aria-disabled -->
  <p class="today-foot">אין עוד מפגשים היום</p>
</main>
```

### Interaction / motion
Instant render — no entrance stagger (§8: instructor home opens many times/day). Model-chip dot is a static `--ok` dot (no pulse). Monolith press: `scale(.97)`, glow tightens, `--dur-instant`/`--ease-spring`. Tapping it navigates to `/Attendance/{shiftInstanceId}` (full nav, not HTMX — the attendance screen is its own page/sheet stage).

---

## 2. Attendance screen — THE signature interaction (most detail)

**Route:** `/Attendance/{shiftInstanceId}` (resource-based authz §B: openable only when `actual_instructor_id == me AND date == today`). Instructor scale, mobile-first. This is §9.2.
**Job:** the class roster as wide `--radius-pill` rows on `--lg-fill-strong`. **Tap the inline-end (trailing/left in RTL) half = present (`--ok`); tap the inline-start (leading/right in RTL) half = absent (`--absent`).** Fill animates from the **leading edge** with `--ease-spring`. Tryout students pinned in a `--lg-fill-tint` tray with a TRYOUT badge. The **list panel is the glass once**; rows are opaque (perf §10). Submit via thin API with an idempotency key (optimistic).

> **Tap-half semantics, stated precisely (this is the crux — get it right):**
> The fill **always grows from the leading edge** (inline-start = right in RTL) regardless of which state, because that mirrors reading direction (§7: progress fills right→left). Present and Absent differ by **color and direction of completeness**, NOT by which edge the fill starts from:
> - **Tap inline-end half → נוכח (Present):** row fills **fully** in `--ok` from leading edge to trailing edge. (You reached across the row → "fully here".)
> - **Tap inline-start half → נעדר (Absent):** row fills in `--absent`, but only a **short leading wedge** (~38%) — a deliberate "partial / stopped short" read — with the rest staying neutral. (Tap near where you start reading → "didn't get far / not here".)
> This gives two visually distinct, color-coded, RTL-correct states with one fill mechanism. The hit target is the whole row (≥56px tall, thumb-safe); the two halves are invisible hit zones, with a hairline center guide visible only in the **unmarked** state.

### Page wireframe (mobile)
```
┌─ attendance-stage (full-bleed; photo backdrop shows at edges) ──────────────┐
│                                                                             │
│   roster scrolls here (opaque rows, ONE glass list panel behind)            │
│   ┌─ glass attendance-panel (the ONLY scrolling glass; --lg-fill-strong) ─┐ │
│   │  ┌ att-row  (unmarked) ────────────────────────────────────────────┐  │ │
│   │  │ דנה כהן                                  │  ← hairline center guide│  │ │
│   │  │ 050-1234567                              │                        │  │ │
│   │  └──────────────────────────────────────────────────────────────────┘  │ │
│   │  ┌ att-row is-present (filled --ok, leading→trailing) ──────────────┐  │ │
│   │  │ יואב לוי                                              נוכח ✓      │  │ │
│   │  └──────────────────────────────────────────────────────────────────┘  │ │
│   │  ┌ att-row is-absent (short --absent wedge at leading edge) ────────┐  │ │
│   │  │▌נעדר ✕   רותם בר                                                  │  │ │
│   │  └──────────────────────────────────────────────────────────────────┘  │ │
│   │                                                                       │ │
│   │  ┌ att-tray (--lg-fill-tint) ─ על תנאי (ניסיון) ────────────────────┐ │ │
│   │  │ ┌ att-row is-present ─ TRYOUT  מאיה ד׳ ──────────────  נוכח ✓ ──┐ │ │ │
│   │  │ └────────────────────────────────────────────────────────────┘ │ │ │
│   │  └─────────────────────────────────────────────────────────────────┘ │ │
│   └───────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│ ┌═ glass glass--nav att-sheet (FIXED bottom sheet; .glass--lensed header) ═┐ │
│ │  ◳ model-chip · כיתה ג׳2 · 24 ביוני        נוכח 14 · נעדר 2 · נותרו 2    │ │
│ │  ┌══════════ btn-primary att-submit ══════════════════════════════════┐  │ │
│ │  ║                     שמור נוכחות (18)                                ║  │ │
│ │  └════════════════════════════════════════════════════════════════════┘  │ │
│ └═══════════════════════════════════════════════════════════════════════════┘ │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Blur budget on THIS screen (perf §10, strict)
Exactly **two** blur surfaces: (1) the **`att-sheet`** fixed bottom sheet (`glass--nav` blur) and (2) the **`attendance-panel`** static list wrapper (`glass--panel` blur, NOT re-blurred per row). The list **scrolls inside** its panel; the panel itself is not re-rendered on scroll. **Rows = zero `backdrop-filter`** (opaque `--lg-fill-strong` fill baked as a solid-ish rgba; on this screen rows use a dedicated `--att-row-fill` ≈ `rgba(255,255,255,.86)` so the colored fills read cleanly over them). The **sheet header strip** may carry `.glass--lensed` via the `.glass__lens` child — **desktop/hero only**; on mobile the existing `@media (max-width:767px){ .glass__lens{filter:none} }` already drops the SVG, leaving only the rim glow. That keeps mobile at the GPU-cheap 2-blur ceiling.

### Row anatomy & the three states
A row is a single `<button type="button" class="att-row" data-client-id aria-pressed>` (whole row is the control; the two halves are JS-computed from pointer x relative to row width, **mirrored for RTL in JS** — see motion notes). Visual layers, bottom→top:
1. **`.att-row__fill`** — absolutely-positioned, `inset:0`, `transform-origin: inline-start` (leading edge), `transform: scaleX(0)` at rest. Color set by state class. This is the animated layer (GPU `transform` only).
2. **Row content** (`z-index:2`): `.att-row__who` (name `--t-body`/600 + phone `<bdi class="num">` `--t-meta` beneath) at the leading side; a `.att-row__verdict` (`נוכח ✓` / `נעדר ✕`) that fades in at the trailing side when marked.
3. **`.att-row__guide`** — a 1px center hairline visible **only** in the unmarked state, signalling "two halves".

| State | Class | Fill | Verdict text | Reads as |
|---|---|---|---|---|
| Unmarked | `.att-row` (base) | none; center hairline shown | — | neutral, awaiting tap |
| Present | `.att-row.is-present` | `--ok-fill` (solid `--ok` at ~.16 over the row, with a `--ok` leading rail) `scaleX(1)` | `נוכח ✓` (in `--ok`, trailing) | fully marked here |
| Absent | `.att-row.is-absent` | `--absent` wedge `scaleX(.38)` from leading edge | `נעדר ✕` (in `--absent`, **leading**, on the wedge) | stopped short / not here |

Re-tapping the **same** half toggles back to unmarked (undo). Tapping the **other** half switches state (fill animates between scaleX targets, color crossfades). All transitions `--ease-spring`, ~180ms (`--dur-fast`).

### The fixed bottom sheet (header + submit)
- **Header strip** = the lesson context: **model-chip** (§9.4) + `· {כיתה} · <bdi class="num">{D ב{חודש}}</bdi>`, then a **live tally** `נוכח <bdi class="num">14</bdi> · נעדר <bdi class="num">2</bdi> · נותרו <bdi class="num">2</bdi>` (count-up animates on change, `--ease-spring`). This header is the one `.glass--lensed` surface (desktop only).
- **Submit** = `.btn-primary .att-submit`, full-width, label `שמור נוכחות (<bdi class="num">18</bdi>)` where the number is total roster. **Disabled until every row is marked** OR shows a confirm if some remain unmarked (copy below). On submit: optimistic — button → spinner → check, the tally count-ups settle, then a success state; the actual POST is async with the idempotency key.

### Submit / confirmation / API (thin REST, optimistic, idempotency)
- **Optimistic flow:** marks are applied to the DOM instantly on tap (no network per tap). **Submit** sends one `POST /api/attendance` with `{ lessonLogId|shiftInstanceId, marks:[{clientId,status}], idempotencyKey }`. The `idempotencyKey` is generated once per attendance session (e.g. `att-{shiftInstanceId}-{firstTapTimestamp}`) so a double-tap / flaky-mobile retry is safe (workflow: Attendance "submitted with a client-supplied idempotency key", spec §4/§5).
- **Success (200):** submit button morphs to a `--ok` check, a one-shot `.att-success` toast `הנוכחות נשמרה ✓`, then nav back to `/Dashboard/Instructor` after ~1.2s (or a `חזרה` link). Count-up plays once.
- **Conflict (already submitted, 409):** toast `הנוכחות כבר נשמרה` + offer `צפייה`/`חזרה` — never a duplicate write (the key dedupes server-side).
- **Network fail:** button returns to active, inline `--absent`-tinted line `השמירה נכשלה — נסו שוב`; marks stay in the DOM (nothing lost). Retry reuses the **same** idempotency key.
- **Reduced motion:** no fill sweep, no count-up — marks snap to color, tally updates instantly, submit is a plain enable→success swap (already gated globally + in the new keyframes).

### Exact Hebrew copy
| Element | Hebrew |
|---|---|
| Sheet header context | `{model-chip} · {כיתה} · <bdi class="num">24 ביוני</bdi>` |
| Live tally | `נוכח <bdi class="num">14</bdi> · נעדר <bdi class="num">2</bdi> · נותרו <bdi class="num">2</bdi>` |
| Present verdict / aria | `נוכח` (visible) · `aria-label="סמן נוכח: {שם}"` |
| Absent verdict / aria | `נעדר` (visible) · `aria-label="סמן נעדר: {שם}"` |
| Row (unmarked) aria | `aria-label="{שם} — טרם סומן"` |
| Submit button | `שמור נוכחות (<bdi class="num">18</bdi>)` |
| Submit, rows remaining (confirm) | `נותרו <bdi class="num">2</bdi> תלמידים ללא סימון — לשמור בכל זאת?` actions `שמירה` / `חזרה לסימון` |
| Success | `הנוכחות נשמרה ✓` |
| Already submitted (409) | `הנוכחות כבר נשמרה` |
| Fail | `השמירה נכשלה — נסו שוב` |
| Tryout tray header | `על תנאי (ניסיון)` |
| Tryout badge | `TRYOUT` (Latin/uppercase — the signature mark; `ניסיון` carries meaning on the tray header) |
| Empty roster | title `אין תלמידים רשומים לכיתה` · hint `שבצו תלמידים לכיתה לפני נטילת נוכחות.` |
| Back link | `חזרה` |

### Markup skeleton (real + new classes)
```html
<main class="attendance-stage" data-shift="12"
      data-idempotency-key="att-12-1719230000">
  <section class="glass glass--panel attendance-panel" aria-label="רשימת נוכחות"
           style="--lg-tile-shadow: var(--lg-shadow);">
    <ul class="att-list" role="list">
      <li>
        <button type="button" class="att-row" data-client-id="5"
                aria-pressed="false" aria-label="דנה כהן — טרם סומן">
          <span class="att-row__fill" aria-hidden="true"></span>
          <span class="att-row__who">
            <span class="att-row__name">דנה כהן</span>
            <bdi class="att-row__phone num">050-1234567</bdi>
          </span>
          <span class="att-row__verdict" aria-hidden="true"></span>
          <span class="att-row__guide" aria-hidden="true"></span>
        </button>
      </li>
      <!-- … more rows … -->
    </ul>

    <!-- tryout tray: rendered only if any tryout enrollment exists -->
    <div class="att-tray">
      <div class="section-label att-tray__label">על תנאי (ניסיון)</div>
      <ul class="att-list" role="list">
        <li>
          <button type="button" class="att-row att-row--tryout" data-client-id="9"
                  aria-pressed="false" aria-label="מאיה ד׳ — טרם סומן">
            <span class="att-row__fill" aria-hidden="true"></span>
            <span class="tryout-badge">TRYOUT</span>
            <span class="att-row__who">
              <span class="att-row__name">מאיה ד׳</span>
              <bdi class="att-row__phone num">053-7777777</bdi>
            </span>
            <span class="att-row__verdict" aria-hidden="true"></span>
            <span class="att-row__guide" aria-hidden="true"></span>
          </button>
        </li>
      </ul>
    </div>
  </section>

  <!-- FIXED bottom sheet: blur #2; header is the ONE lensed surface (desktop) -->
  <div class="glass glass--nav att-sheet">
    <div class="att-sheet__header glass--lensed">
      <div class="glass__lens" aria-hidden="true"></div>   <!-- text-immune refraction layer -->
      <span class="model-chip">
        <span class="model-chip__dot" aria-hidden="true"></span>
        דגם: קופסת אוצרות
      </span>
      <span class="att-sheet__ctx">· כיתה ג׳2 · <bdi class="num">24 ביוני</bdi></span>
      <span class="att-sheet__tally">
        נוכח <bdi class="num" data-count-present>14</bdi> ·
        נעדר <bdi class="num" data-count-absent>2</bdi> ·
        נותרו <bdi class="num" data-count-left>2</bdi>
      </span>
    </div>
    <button type="button" class="btn-primary att-submit">
      שמור נוכחות (<bdi class="num">18</bdi>)
    </button>
  </div>
</main>
```
> JS responsibilities (small Motion-One slice, progressive enhancement): compute tapped half from pointer x with **RTL mirroring** (`const leading = (dir==='rtl') ? (x > rect.width/2) : (x < rect.width/2)`); toggle state classes; keep the tally + submit-count in sync; build the POST body; manage the idempotency key; handle 200/409/fail. With JS off, fall back to a no-frills `<form>` of two radio buttons per row posting to a Razor handler (note for implementer; the *design* is the optimistic path).

### NEW CSS — attendance row + sheet (full, paste-ready, real tokens)
```css
/* ============================================================
   SLICE 2 — ATTENDANCE (signature interaction, §9.2)
   The list panel is the ONLY scrolling glass; rows are OPAQUE.
   Fill animates via transform (GPU); origin = inline-start (leading
   = right in RTL → mirrors reading direction, §7). All motion gated.
   ============================================================ */

/* Full-bleed stage: roster scrolls; sheet is fixed at block-end */
.attendance-stage {
  position: relative;
  min-block-size: 100dvh; min-block-size: 100vh;
  padding-block-start: var(--sp-5);
  /* leave room for the fixed sheet so the last row isn't covered */
  padding-block-end: calc(var(--sp-20) + var(--sp-16));
  padding-inline: var(--sp-4);
  max-inline-size: 640px; margin-inline: auto;
}

/* The ONE scrolling glass list panel */
.attendance-panel { padding: var(--sp-4); }
.att-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--sp-3); }
.att-list + .att-tray .att-list { margin-block-start: var(--sp-2); }

/* Per-screen opaque row fill (rows carry ZERO backdrop-filter, perf §10) */
.attendance-panel { --att-row-fill: rgba(255,255,255,.86); }
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) .attendance-panel { --att-row-fill: rgba(28,42,60,.88); }
}

/* The pill row — whole row is the tap target (thumb-safe height) */
.att-row {
  position: relative; isolation: isolate; overflow: hidden;
  inline-size: 100%; min-block-size: 56px;
  display: flex; align-items: center; gap: var(--sp-3);
  padding-block: var(--sp-3); padding-inline: var(--sp-5);
  background: var(--att-row-fill);
  border: 1px solid rgba(var(--brand-rgb), .10);
  border-radius: var(--radius-pill);
  box-shadow: var(--lg-shadow);
  font: inherit; text-align: start; cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  transition: box-shadow var(--dur-fast) var(--ease-glass);
}
.att-row:active { box-shadow: var(--lg-shadow-lifted); }

/* The animated fill — leading-anchored, transform only */
.att-row__fill {
  position: absolute; inset: 0; z-index: 0;
  border-radius: inherit; pointer-events: none;
  transform-origin: inline-start;      /* leading edge = right in RTL */
  transform: scaleX(0);
  transition: transform var(--dur-fast) var(--ease-spring),
              background-color var(--dur-fast) var(--ease-spring);
}
.att-row > :not(.att-row__fill) { position: relative; z-index: 2; }

/* Content */
.att-row__who { display: flex; flex-direction: column; gap: 2px; min-inline-size: 0; flex: 1; }
.att-row__name { font-size: var(--t-body); font-weight: var(--fw-semibold); color: var(--brand-ink); }
.att-row__phone { font-size: var(--t-meta); color: var(--brand-ink-muted); }
.att-row__verdict {
  flex: 0 0 auto; font-size: var(--t-label); font-weight: var(--fw-bold);
  opacity: 0; transition: opacity var(--dur-fast) var(--ease-glass);
}
/* center "two halves" guide — unmarked only */
.att-row__guide {
  position: absolute; z-index: 1; inset-block: 22%; inset-inline-start: 50%;
  inline-size: 1px; background: rgba(var(--brand-rgb), .18);
  pointer-events: none; transition: opacity var(--dur-fast) var(--ease-glass);
}

/* PRESENT — full --ok wash, leading rail, verdict trailing */
.att-row.is-present .att-row__fill {
  transform: scaleX(1);
  background: var(--ok-soft);
}
.att-row.is-present { border-color: rgba(46,125,91,.30); }
.att-row.is-present .att-row__verdict { opacity: 1; color: var(--ok); }
.att-row.is-present .att-row__verdict::after { content: "נוכח ✓"; }
.att-row.is-present .att-row__guide { opacity: 0; }

/* ABSENT — short leading wedge, verdict ON the wedge (leading) */
.att-row.is-absent .att-row__fill {
  transform: scaleX(.38);
  background: var(--absent-soft);
}
.att-row.is-absent { border-color: rgba(178,58,72,.28); }
.att-row.is-absent .att-row__verdict { opacity: 1; color: var(--absent); order: -1; }  /* move to leading */
.att-row.is-absent .att-row__verdict::after { content: "נעדר ✕"; }
.att-row.is-absent .att-row__guide { opacity: 0; }

/* Tryout rows + badge (reuse the Slice-1 .tryout-badge) */
.att-row--tryout { background: rgba(200,134,27,.08); border-color: rgba(200,134,27,.20); }
.att-tray {
  margin-block-start: var(--sp-5); padding: var(--sp-3);
  background: var(--lg-fill-tint);
  border: 1px solid rgba(var(--brand-rgb), .14);
  border-radius: var(--radius-card);
}
.att-tray__label { border-block-end: none; color: var(--warn); }

/* ── Fixed bottom sheet (blur surface #2) ───────────────────────────────── */
.att-sheet {
  position: fixed; inset-inline: 0; inset-block-end: 0; z-index: 120;
  display: flex; flex-direction: column; gap: var(--sp-3);
  padding-block: var(--sp-4) calc(var(--sp-4) + env(safe-area-inset-bottom, 0px));
  padding-inline: var(--sp-5);
  border-start-start-radius: var(--radius-sheet);
  border-start-end-radius: var(--radius-sheet);
  border-block-end: 0;
}
.att-sheet__header {
  position: relative; isolation: isolate; overflow: hidden;
  display: flex; flex-wrap: wrap; align-items: center; gap: var(--sp-2) var(--sp-3);
  padding-block: var(--sp-2); padding-inline: var(--sp-3);
  border-radius: var(--radius-card);
}
.att-sheet__ctx   { font-size: var(--t-label); color: var(--brand-ink-muted); }
.att-sheet__tally {
  margin-inline-start: auto;
  font-size: var(--t-label); font-weight: var(--fw-semibold); color: var(--brand-ink);
}
.att-submit { inline-size: 100%; font-size: var(--t-body); padding-block: calc(var(--sp-3) + 4px); }
.att-submit.is-busy { pointer-events: none; opacity: .85; }
.att-submit.is-done { background: var(--ok); }

/* sheet open = materialize; count-up + fill already covered by per-element transitions */
@media (prefers-reduced-motion: no-preference) {
  @keyframes att-sheet-rise {
    from { opacity: 0; transform: translateY(16px); }
    to   { opacity: 1; transform: translateY(0); }
  }
  .att-sheet { animation: att-sheet-rise var(--dur-slow) var(--ease-glass) both; }
}

/* one-shot success toast (reuses .toast tokens) */
.att-success { /* uses .toast base; just a semantic alias */ }

/* Desktop: the lensed sheet header is allowed (SVG dropped on mobile by base.css) */
@media (max-width: 767px) {
  .att-sheet__tally { margin-inline-start: 0; inline-size: 100%; }
}
```
> **RTL fill direction note for the implementer:** `transform-origin: inline-start` makes `scaleX` grow from the **right** in RTL automatically — no JS flip needed for the *animation*. Only the **tap-half detection** needs the JS mirror (above). Re-confirm `--ok`, `--ok-soft`, `--absent`, `--absent-soft`, `--lg-fill-tint`, `--radius-pill`, `--radius-sheet`, `env(safe-area-inset-bottom)` usage is acceptable before shipping.

---

## 3. Lesson-log surface (HTMX)

**Route:** `/Attendance/{shiftInstanceId}/Log` or a `Lesson` tab on the attendance flow (role `InstructorOrAdmin`, same date-scope). Instructor scale. Shows the **"X of N" pacing**, **model selector**, **In_Progress/Completed** status, **instructor notes**. The "X of N" + status are **HTMX partial swaps** (no full reload). Reuses/extends the lesson-model chip (§9.4).

> Data note (spec §4): `Lesson_Log` has `model_id`, `status`, `instructor_notes`, `expected_lessons_snapshot`. The "X of N" is **lessons-spent-on-this-model X of expected N** (`expected_lessons_to_complete`); X is computed server-side from prior logs. The snapshot exists so later syllabus edits don't corrupt history — the chip shows the **live** pacing while editing (PRD §6C), and the snapshot is captured on save.

### Wireframe
```
┌─ glass glass--nav dash-topbar ─ עורקבי · מדריך · יומן שיעור ─────────────┐
  dash-body (today-stage scale)

  ┌─ glass glass--tile lesson-card ─────────────────────────────────────┐
  │  כיתה ג׳2 · עירוני א׳ · יום שלישי 24 ביוני                          │
  │                                                                     │
  │  ── דגם נוכחי ─────────────────────────  (section-label)            │
  │  ┌ #lesson-pacing  (HTMX target) ─────────────────────────────────┐ │
  │  │  ┌ model-chip --lensed? no ─┐    שיעור 3 מתוך 8                  │ │
  │  │  │ ● קופסת אוצרות           │    ▓▓▓░░░░░  (pace-bar 3/8)        │ │
  │  │  └──────────────────────────┘                                   │ │
  │  └──────────────────────────────────────────────────────────────────┘ │
  │                                                                     │
  │  ── דגם השיעור ──                                                    │
  │  [ select model  ▾ ]   ← hx-post on change → swaps #lesson-pacing    │
  │                                                                     │
  │  ┌ #lesson-status (HTMX target) ──────────────────────────────────┐ │
  │  │  ── סטטוס ──   [ בתהליך | הושלם ]  (segment)                     │ │
  │  │  ── הערות מדריך ──                                               │ │
  │  │  [ textarea …………………………… ]                                       │ │
  │  │                              [ שמור יומן ]                        │ │
  │  └──────────────────────────────────────────────────────────────────┘ │
  └─────────────────────────────────────────────────────────────────────┘
```

### How it reads / HTMX boundaries
- **`#lesson-pacing`** (partial `_LessonPacing.cshtml`): the **model-chip** + the **"שיעור X מתוך N"** line at `--t-title`/600 with the numerals in `<bdi class="num">`, plus a thin **`pace-bar`** (fill = X/N, fills leading→trailing = right→left, §7). When the instructor changes the **model `<select>`**, `hx-post` recomputes X-of-N for that model server-side and **swaps this fragment** — the chip name + the "X of N" + the bar update with a quick flip; no page reload. If X **>** N (overrun → triggers the gap monitor later in Slice 3), the bar turns `--warn` and the line appends ` (חריגה)`.
- **Model selector**: a recessed-well `<select class="form-select">` (reuse Slice-1 form-field recipe). `hx-post="?handler=Pace" hx-target="#lesson-pacing" hx-swap="outerHTML" hx-trigger="change"`.
- **`#lesson-status`** (partial `_LessonStatus.cshtml`): a **segment** (`בתהליך`/`הושלם`, reuse `.segment`) + a **notes** `textarea` (reuse `.form-field textarea`) + a `שמור יומן` primary. Saving the status/notes `hx-post`s and swaps this fragment back with a saved confirmation (`נשמר ✓` inline, fades). Marking `הושלם` is the moment `expected_lessons_snapshot` is captured (server concern; UI just posts).

### Exact Hebrew copy
| Element | Hebrew |
|---|---|
| Topbar title | `יומן שיעור` |
| Context line | `{כיתה} · {בית ספר} · {יום} <bdi class="num">{D ב{חודש}}</bdi>` |
| Section: current model | `דגם נוכחי` |
| Pacing line | `שיעור <bdi class="num">3</bdi> מתוך <bdi class="num">8</bdi>` |
| Pacing overrun suffix | ` (חריגה)` (bar + text `--warn`) |
| Section: model select | `דגם השיעור` |
| Select placeholder | `בחרו דגם…` |
| Section: status | `סטטוס` |
| Segment options | `בתהליך` · `הושלם` |
| Section: notes | `הערות מדריך` |
| Notes placeholder | `מה נעשה בשיעור, קשיים, חומרים שחסרו…` |
| Save button | `שמור יומן` |
| Saved confirmation | `נשמר ✓` |
| No model on syllabus yet | pacing area shows `טרם שובץ דגם לשיעור` + the select; chip `.model-chip--warn` |

### Markup skeleton (HTMX targets marked)
```html
<main class="dash-body today-stage">
  <article class="glass glass--tile lesson-card" style="--lg-tile-shadow: var(--lg-shadow);">
    <p class="lesson-card__ctx">כיתה ג׳2 · עירוני א׳ · יום שלישי <bdi class="num">24 ביוני</bdi></p>

    <div class="section-label">דגם נוכחי</div>
    <!-- ↓ HTMX-swappable fragment -->
    <div id="lesson-pacing" class="lesson-pacing">
      <span class="model-chip"><span class="model-chip__dot" aria-hidden="true"></span>קופסת אוצרות</span>
      <p class="lesson-pacing__count">שיעור <bdi class="num">3</bdi> מתוך <bdi class="num">8</bdi></p>
      <div class="pace-bar" role="progressbar" aria-valuenow="3" aria-valuemin="0" aria-valuemax="8">
        <span class="pace-bar__fill" style="--pace: 37.5%;"></span>
      </div>
    </div>

    <div class="form-field">
      <label class="form-field__label" for="Model">דגם השיעור</label>
      <select id="Model" name="ModelId" class="form-select"
              hx-post="?handler=Pace" hx-target="#lesson-pacing"
              hx-swap="outerHTML" hx-trigger="change">
        <option value="" disabled>בחרו דגם…</option>
        <option value="7" selected>קופסת אוצרות</option>
      </select>
    </div>

    <!-- ↓ HTMX-swappable fragment -->
    <div id="lesson-status" class="lesson-status">
      <span class="section-label">סטטוס</span>
      <form hx-post="?handler=SaveLog" hx-target="#lesson-status" hx-swap="outerHTML">
        <div class="segment" role="radiogroup" aria-label="סטטוס שיעור">
          <label class="segment__opt"><input type="radio" name="Status" value="InProgress" checked><span>בתהליך</span></label>
          <label class="segment__opt"><input type="radio" name="Status" value="Completed"><span>הושלם</span></label>
        </div>
        <div class="form-field">
          <label class="form-field__label" for="Notes">הערות מדריך</label>
          <textarea id="Notes" name="Notes" placeholder="מה נעשה בשיעור, קשיים, חומרים שחסרו…"></textarea>
        </div>
        <div class="form-actions"><button type="submit" class="btn-primary">שמור יומן</button></div>
      </form>
    </div>
  </article>
</main>
```

### Motion
HTMX swaps use a quick `lg-fade-up` (already in base.css, gated) on the new fragment — `--dur-fast`. The pace-bar fill animates `--pace` width change with `--ease-spring`. Saved `נשמר ✓` fades in/out. Reduced-motion: instant swaps.

---

## 4. Syllabus management (admin/CS, dense)

**Routes:** `/Curriculum` (overview/tabs), `/Curriculum/Syllabi` (list), `/Curriculum/Syllabi/Create|Edit/{id}` (edit + ordered models), `/Curriculum/Models` (catalog CRUD). Role `CsOrAdmin`/`AdminOnly`. **Dense admin scale.** Reuses `.subnav`, `.data-table`, `.form-panel`, `.form-field`, `.segment`, `.empty-state`.
**Data (spec §4):** `Syllabus` (`name`, `start_date`, `end_date`, `status`); `Syllabus_Models` junction (`syllabus_id`, `model_id`, `order_index` — the ordered list); `Model` (`name`, `expected_lessons_to_complete`, `material_link`, `video_link`).

A new **Curriculum section** with its own subnav (mirrors People's pattern): `סקירה · סילבוסים · דגמים`.

### 4a. Syllabus edit — the ordered model list (the focal admin surface here)
```
┌ glass glass--nav dash-topbar · עורקבי · תכנית לימודים ───────────────────┐
  dash-body
  ┌ subnav ─ סקירה · סילבוסים · דגמים ──────────────────────────────────┐
  ┌ breadcrumb ─ סילבוסים ‹ סילבוס כיתות ג׳ ──────────────────────────────┐

  ┌ page-head ─ סילבוס כיתות ג׳   ●פעיל · 01.09.25–20.06.26   [+ הוסף דגם] ┐

  ┌ glass glass--tile curriculum-panel ──────────────────────────────────┐
  │  ── דגמים בסדר ההוראה ──   (8 דגמים · ~32 שיעורים)                     │
  │  ┌ #syllabus-models (HTMX target) ─────────────────────────────────┐ │
  │  │ ┌ syl-model-row ─────────────────────────────────────────────┐  │ │
  │  │ │ ⟨1⟩  קופסת אוצרות      8 שיעורים   [חומר][וידאו]   ▲ ▼  ✕   │  │ │
  │  │ └────────────────────────────────────────────────────────────┘  │ │
  │  │ ┌ syl-model-row ─────────────────────────────────────────────┐  │ │
  │  │ │ ⟨2⟩  מסגרת פסיפס       6 שיעורים   [חומר][וידאו]   ▲ ▼  ✕   │  │ │
  │  │ └────────────────────────────────────────────────────────────┘  │ │
  │  └──────────────────────────────────────────────────────────────────┘ │
  └───────────────────────────────────────────────────────────────────────┘
```

- **`#syllabus-models`** (partial `_SyllabusModelList.cshtml`) is the HTMX swap target. Each `syl-model-row` is an **opaque tinted row** (reuse the `.roster-row` look via a new `.syl-model-row` that shares its fill recipe) showing: an **order index badge** `⟨n⟩` (leading), the **model name** `--t-admin-body`/600, **`<bdi class="num">{N}</bdi> שיעורים`** (expected_lessons), small **material/video link chips**, and **reorder controls** `▲ ▼` + remove `✕` (inline-end).
- **Ordering (at minimum up/down; drag is nice-to-have):** the `▲`/`▼` buttons `hx-post` a reorder and **swap the whole list fragment** (server recomputes `order_index`). `▲` disabled on first row, `▼` on last. The reorder arrows are **vertical glyphs — NOT mirrored** (they imply up/down, not reading flow). Drag-to-reorder is an optional enhancement (HTML5 DnD or a tiny lib) that posts the same reorder endpoint; **if not built, up/down is the shipped baseline — flag it, don't fake it.**
- **Material/video** render as `.link-chip` (a small ghost link chip, new): `חומר ↗` / `וידאו ↗` opening in a new tab; `↗` gets `.icon-directional`. If a link is null, omit that chip (don't show a dead chip).
- **`+ הוסף דגם`** opens an inline add (a `<select>` of catalog models not yet in this syllabus) that `hx-post`s and appends a row to the list fragment.

### 4b. Syllabus list + 4c. Models catalog
- **Syllabus list** (`/Curriculum/Syllabi`): standard `.data-table` panel — columns `שם · תאריכים · דגמים · סטטוס · פעולות`; status `.status-chip` (`פעיל`/`בארכיון`); primary row action `דגמים` (`btn-ghost--accent`, opens the ordered editor) + `עריכה`. Create/edit form = `.form-panel`: name, start/end `type="date"` (`<bdi class="num">`), status `.segment`.
- **Models catalog** (`/Curriculum/Models`): `.data-table` — `שם הדגם · שיעורים צפויים · חומר · וידאו · פעולות`. Create/edit `.form-panel`: `שם הדגם *`, `שיעורים צפויים *` (number, `min=1`), `קישור לחומר` (url, LTR), `קישור לוידאו` (url, LTR). Reuse the Slice-1 recessed-well form fields verbatim.

### Exact Hebrew copy
| Element | Hebrew |
|---|---|
| Section subnav | `סקירה` · `סילבוסים` · `דגמים` |
| Topbar title | `תכנית לימודים` |
| Syllabus edit title / status / dates | `{שם הסילבוס}` · `פעיל`/`בארכיון` · `<bdi class="num">01.09.25–20.06.26</bdi>` |
| Add model button | `+ הוסף דגם` |
| Ordered-list label | `דגמים בסדר ההוראה` |
| List summary | `<bdi class="num">8</bdi> דגמים · כ־<bdi class="num">32</bdi> שיעורים` |
| Order badge | `⟨<bdi class="num">{n}</bdi>⟩` |
| Lessons count (per row) | `<bdi class="num">{N}</bdi> שיעורים` |
| Link chips | `חומר ↗` · `וידאו ↗` |
| Reorder / remove aria | `aria-label="העבר מעלה"` · `aria-label="העבר מטה"` · `aria-label="הסר דגם מהסילבוס"` |
| Add-model select | `בחרו דגם להוספה…` |
| Syllabus list columns | `שם` · `תאריכים` · `דגמים` · `סטטוס` · `פעולות` |
| Syllabus row actions | `דגמים` (accent) · `עריכה` |
| Models columns | `שם הדגם` · `שיעורים צפויים` · `חומר` · `וידאו` · `פעולות` |
| Model form labels | `שם הדגם *` · `שיעורים צפויים *` · `קישור לחומר (לא חובה)` · `קישור לוידאו (לא חובה)` |
| Validation: lessons | `מספר שיעורים חייב להיות 1 ומעלה` |
| Empty syllabus (no models) | title `אין דגמים בסילבוס` · hint `הוסיפו דגמים וקבעו את סדר ההוראה.` |
| Empty models catalog | title `אין דגמים עדיין` · hint `צרו דגם כדי לשבץ אותו בסילבוסים.` |
| Save success | `הסילבוס נשמר` / `הדגם נשמר` |

### Markup skeleton (ordered list fragment)
```html
<div class="glass glass--tile curriculum-panel" style="--lg-tile-shadow: var(--lg-shadow);">
  <div class="section-label">דגמים בסדר ההוראה
    <span class="list-summary"><bdi class="num">8</bdi> דגמים · כ־<bdi class="num">32</bdi> שיעורים</span>
  </div>

  <ul id="syllabus-models" class="syl-model-list" role="list">
    <li class="syl-model-row">
      <span class="syl-model-row__order" aria-hidden="true">⟨<bdi class="num">1</bdi>⟩</span>
      <span class="syl-model-row__name">קופסת אוצרות</span>
      <span class="syl-model-row__lessons"><bdi class="num">8</bdi> שיעורים</span>
      <span class="syl-model-row__links">
        <a class="link-chip" href="https://…" target="_blank" rel="noopener">חומר <span class="icon-directional" aria-hidden="true">↗</span></a>
        <a class="link-chip" href="https://…" target="_blank" rel="noopener">וידאו <span class="icon-directional" aria-hidden="true">↗</span></a>
      </span>
      <span class="syl-model-row__ctl">
        <button class="row-ctl" disabled aria-label="העבר מעלה"
                hx-post="?handler=MoveUp&modelId=7" hx-target="#syllabus-models" hx-swap="outerHTML">▲</button>
        <button class="row-ctl" aria-label="העבר מטה"
                hx-post="?handler=MoveDown&modelId=7" hx-target="#syllabus-models" hx-swap="outerHTML">▼</button>
        <button class="row-ctl row-ctl--remove" aria-label="הסר דגם מהסילבוס"
                hx-post="?handler=Remove&modelId=7" hx-target="#syllabus-models" hx-swap="outerHTML">✕</button>
      </span>
    </li>
    <!-- … -->
  </ul>
</div>
```

---

## 5. Shift schedule view (admin/CS, dense) + substitution approval

**Routes:** `/Scheduling` (overview), `/Scheduling/Templates` (shift templates per class), `/Scheduling/Instances` (date view + generate), `/Scheduling/Substitutions` (request/approve). Role `CsOrAdmin`/`AdminOnly`. Dense admin scale. Reuses `.subnav`, `.data-table`, `.form-panel`, `.status-chip`, `.empty-state`, `.btn-ghost--accent`.
**Data (spec §4):** `Shift_Template` (`class_id`, `default_instructor_id`, `day_of_week`, `start_time`, `end_time`, `academic_year_id`, `status`); `Shift_Instance` (`template_id`, `actual_instructor_id`, `date`, `status`); `Substitution_Request` (`shift_instance_id`, `requesting_instructor_id`, `substitute_instructor_id`, `status`, `approved_by_user_id`, `approved_at`).

Section subnav: `סקירה · תבניות · מופעים · החלפות`.

### 5a. Templates — per class (day-of-week + time + default instructor)
A `.data-table` panel. Columns: `כיתה · יום · שעות · מדריך קבוע · שנה · סטטוס · פעולות`. **Day in Hebrew**, **times in one `<bdi>` range**. Create/edit `.form-panel`: class `<select>`, **day-of-week** as a 7-option `<select>` (or a horizontal `.segment`-style day picker if it fits) with Hebrew day labels, start/end `type="time"` (`<bdi>`), default-instructor `<select>`, academic-year `<select>`, status `.segment`.

```
┌ glass--tile (data-table panel) ───────────────────────────────────────────┐
│ כיתה   │ יום    │ שעות         │ מדריך קבוע │ שנה   │ סטטוס │ פעולות         │
│ ג׳2    │ שלישי  │ 16:00–17:30  │ רון א׳     │ תשפ״ו │ ●פעיל │ עריכה          │
└────────────────────────────────────────────────────────────────────────────┘
```

### 5b. Instances — date view + "generate instances" affordance
A date-scoped list (default: a horizon window, e.g. next ~30 days per `ShiftInstanceGenerator`). Group by date; each instance row shows class, time, the **actual instructor** (with a swap indicator if it differs from the template default), and status. A **`צור מופעים`** button (top, primary) triggers generation; it `hx-post`s and **swaps `#instances-panel`** with the regenerated list + an `hx-indicator` spinner. Already-edited instances are preserved (server concern; UI shows a `נערך ידנית` chip on those so the admin understands they won't be overwritten).

```
┌ page-head ─ מופעי משמרת          [ טווח: 30 ימים ▾ ]      [ צור מופעים ] ┐
┌ #instances-panel (HTMX target) glass--tile ──────────────────────────────┐
│  ── יום שלישי, 24 ביוני ──                                                │
│  ┌ inst-row ─ ג׳2 · 16:00–17:30 · רון א׳ ────────────── ●מתוכנן ─────┐    │
│  ┌ inst-row ─ ד׳1 · 17:45–19:00 · ⇄ מאיה (החלפה) ─── ●מוחלף · נערך ──┐    │
│  ── יום רביעי, 25 ביוני ──                                                │
└───────────────────────────────────────────────────────────────────────────┘
```

### 5c. Substitution request → approval (the workflow UI, §B)
Two faces of the same `Substitution_Request`:
- **Instructor requests** (from an instance they own — a `בקש החלפה` action on the instance, or on the instructor's own schedule): a small `.form-panel`/modal — choose the **substitute instructor** `<select>` + optional reason, submit. Creates a `Pending` request. Copy below.
- **Admin approves** (`/Scheduling/Substitutions`, role `AdminOnly`): a `.data-table` of `Pending` requests — columns `מופע · תאריך · מבקש/ת · מחליף/ה · סטטוס · פעולות`, row actions **`אישור`** (`btn-ghost--accent`) / **`דחייה`** (ghost). Approve `hx-post`s and **swaps that `.sub-row`** to an approved state (sets `actual_instructor_id` server-side, records `approved_by`/`approved_at`); the row shows `●אושר` + who/when. Reject swaps to `●נדחה`.

```
┌ glass--tile (data-table) ─ בקשות החלפה ───────────────────────────────────┐
│ מופע          │ תאריך       │ מבקש/ת │ מחליף/ה │ סטטוס   │ פעולות           │
│ ג׳2 · 16:00   │ 24 ביוני    │ רון א׳ │ מאיה ד׳ │ ●ממתין  │ [אישור][דחייה]   │
└────────────────────────────────────────────────────────────────────────────┘
```

### Exact Hebrew copy
| Element | Hebrew |
|---|---|
| Section subnav | `סקירה` · `תבניות` · `מופעים` · `החלפות` |
| Topbar title | `שיבוץ משמרות` |
| Days of week (select/labels) | `ראשון` · `שני` · `שלישי` · `רביעי` · `חמישי` · `שישי` · `שבת` |
| Templates columns | `כיתה` · `יום` · `שעות` · `מדריך קבוע` · `שנה` · `סטטוס` · `פעולות` |
| Template form labels | `כיתה *` · `יום בשבוע *` · `שעת התחלה *` · `שעת סיום *` · `מדריך קבוע *` · `שנת לימודים *` · `סטטוס` |
| Template validation: end ≤ start | `שעת הסיום חייבת להיות אחרי שעת ההתחלה` |
| New template / save | `+ תבנית חדשה` · `שמירת תבנית` · success `התבנית נשמרה` |
| Instances page title | `מופעי משמרת` |
| Range selector | `טווח:` · options `<bdi class="num">7</bdi> ימים` / `<bdi class="num">30</bdi> ימים` / `החודש` |
| Generate button | `צור מופעים` |
| Generating (indicator) | `יוצר מופעים…` |
| Generate result toast | `נוצרו <bdi class="num">{k}</bdi> מופעים חדשים` (or `לא נוצרו מופעים חדשים`) |
| Date group header | `{יום מלא}, <bdi class="num">{D ב{חודש}}</bdi>` (e.g. `יום שלישי, 24 ביוני`) |
| Instance row | `{כיתה} · <bdi class="num">{16:00–17:30}</bdi> · {מדריך}` |
| Swap indicator | `⇄ {מחליף} (החלפה)` |
| Instance status chips | `מתוכנן` · `מוחלף` · `בוטל` · `הושלם` |
| Manually-edited chip | `נערך ידנית` (muted) |
| Request-sub action | `בקש החלפה` |
| Request-sub form | title `בקשת החלפה` · labels `מחליף/ה *` (`בחרו מדריך מחליף…`) · `סיבה (לא חובה)` · submit `שליחת בקשה` |
| Request success | `בקשת ההחלפה נשלחה` |
| Substitutions page | `בקשות החלפה` |
| Sub-request columns | `מופע` · `תאריך` · `מבקש/ת` · `מחליף/ה` · `סטטוס` · `פעולות` |
| Sub-request status chips | `ממתין` (`--warn`) · `אושר` (`--ok`) · `נדחה` (`--absent`) |
| Approve / reject | `אישור` (accent) · `דחייה` (ghost) |
| Approved meta (after) | `אושר ע״י {שם} · <bdi class="num">{D ב{חודש}}</bdi>` |
| Empty templates | title `אין תבניות משמרת` · hint `צרו תבנית כדי לייצר ממנה מופעים שבועיים.` |
| Empty instances | title `אין מופעים בטווח` · hint `צרו מופעים מהתבניות הקיימות.` |
| No pending subs | title `אין בקשות החלפה ממתינות` · hint `בקשות חדשות יופיעו כאן לאישור.` |

### Markup skeleton (substitution row fragment + generate)
```html
<header class="page-head">
  <h1 class="page-head__title">מופעי משמרת</h1>
  <button class="btn-primary" hx-post="?handler=Generate"
          hx-target="#instances-panel" hx-swap="outerHTML"
          hx-indicator="#gen-spin">צור מופעים <span id="gen-spin" class="htmx-indicator">…</span></button>
</header>

<div id="instances-panel" class="glass glass--tile people-panel" style="--lg-tile-shadow: var(--lg-shadow);">
  <div class="date-group-head">יום שלישי, <bdi class="num">24 ביוני</bdi></div>
  <ul class="inst-list" role="list">
    <li class="inst-row">
      <span class="inst-row__class">ג׳2</span>
      <bdi class="inst-row__time num">16:00–17:30</bdi>
      <span class="inst-row__who">רון א׳</span>
      <span class="status-chip status-chip--active">מתוכנן</span>
    </li>
  </ul>
</div>

<!-- Substitutions table -->
<div class="glass glass--tile people-panel" style="--lg-tile-shadow: var(--lg-shadow);">
  <table class="data-table">
    <thead><tr>
      <th>מופע</th><th>תאריך</th><th>מבקש/ת</th><th>מחליף/ה</th><th>סטטוס</th><th class="data-table__actions-col">פעולות</th>
    </tr></thead>
    <tbody>
      <tr class="data-row sub-row" id="sub-row-3">
        <td class="data-cell data-cell--primary">ג׳2 · <bdi class="num">16:00</bdi></td>
        <td class="data-cell"><bdi class="num">24 ביוני</bdi></td>
        <td class="data-cell">רון א׳</td>
        <td class="data-cell">מאיה ד׳</td>
        <td class="data-cell"><span class="status-chip status-chip--pending">ממתין</span></td>
        <td class="data-cell data-cell--actions">
          <button class="btn-ghost btn-ghost--sm btn-ghost--accent"
                  hx-post="?handler=Approve&id=3" hx-target="#sub-row-3" hx-swap="outerHTML">אישור</button>
          <button class="btn-ghost btn-ghost--sm"
                  hx-post="?handler=Reject&id=3" hx-target="#sub-row-3" hx-swap="outerHTML">דחייה</button>
        </td>
      </tr>
    </tbody>
  </table>
</div>
```

### Motion
Generate: `hx-indicator` spinner on the button; the swapped `#instances-panel` does a single `lg-fade-up` (gated). Sub-row approve/reject: the row swaps with a quick fade; the status chip color change is the confirmation. No per-row entrance. Reduced-motion: instant.

---

## 6. Shared additions to `base.css` (one task — paste-ready)

All logical-property-native, token-driven. Append after the Slice-1 blocks, **before** the `@supports not` fallback (and add the noted lines *into* that fallback). **No new tokens required** beyond what's in `tokens.css` — implementer must verify each (`--ok`, `--ok-soft`, `--absent`, `--absent-soft`, `--warn`, `--warn-soft`, `--lg-fill-tint`, `--lg-fill-strong`, `--radius-pill`, `--radius-hero`, `--radius-sheet`, `--lg-shadow*`, `--t-*`, `--sp-*`, `--dur-*`, `--ease-*`). The attendance row/sheet CSS in §2 is included here in consolidated form.

```css
/* ============================================================
   SLICE 2 — Curriculum + Scheduling + Instructor surfaces
   Reuses: .glass/.glass--tile/.glass--nav/.glass--lensed(+.glass__lens),
   .hero-solid, .btn-primary/.btn-ghost(+--sm/--accent), .section-label,
   .form-field/.form-select/.segment, .data-table, .status-chip,
   .empty-state, .tryout-badge, .toast, .num.
   Perf: topbar/sheet are the only blur; lists scroll opaque; rows zero blur.
   ============================================================ */

/* ---- The lesson-model chip (§9.4) — recurring brand signature ---- */
.model-chip {
  display: inline-flex; align-items: center; gap: var(--sp-2);
  padding-block: var(--sp-2); padding-inline: var(--sp-4);
  background: var(--lg-fill-tint);
  border: 1px solid rgba(var(--brand-rgb), .20);
  border-radius: var(--radius-pill);
  font-size: var(--t-label); font-weight: var(--fw-semibold); color: var(--brand);
  /* inline tier blur is OK on the chip ONLY when on fixed chrome (sheet header);
     in scrolling contexts it inherits no backdrop-filter — keep it flat-tinted. */
}
.model-chip__dot {
  inline-size: 7px; block-size: 7px; border-radius: 50%;
  background: var(--ok); flex: 0 0 auto;
}
.model-chip--warn { color: var(--warn); border-color: rgba(200,134,27,.30); background: var(--warn-soft); }
.model-chip--warn .model-chip__dot { background: var(--warn); }

/* ============================================================
   §1 Instructor "today" home
   ============================================================ */
.today-stage { max-inline-size: 560px; margin-inline: auto; }
.today-head { margin-block-end: var(--sp-5); }
.today-head__greeting { margin: 0; font-size: var(--t-title); font-weight: var(--fw-bold);
  letter-spacing: var(--ls-tight); color: var(--brand-ink); }
.today-head__date { margin: var(--sp-1) 0 0; font-size: var(--t-label); color: var(--brand-ink-muted); }
.today-foot { text-align: center; font-size: var(--t-meta); color: var(--brand-ink-muted); margin-block: var(--sp-5); }

.shift-card { display: flex; flex-direction: column; gap: var(--sp-3);
  padding: var(--sp-6); margin-block-end: var(--sp-5); }
.shift-card__head { display: flex; align-items: center; justify-content: space-between; gap: var(--sp-3); }
.shift-card__time { font-size: var(--t-title); font-weight: var(--fw-semibold); color: var(--brand-ink); }
.shift-card__class { margin: 0; font-size: var(--t-body); font-weight: var(--fw-medium); color: var(--brand-ink); }
.shift-card__meta { margin: 0; font-size: var(--t-meta); color: var(--brand-ink-muted); }

/* The "קח נוכחות" MONOLITH — uses existing .hero-solid; this adds layout + press */
.hero-cta {
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: var(--sp-2); min-block-size: 38vh;
  padding-block: var(--sp-8); padding-inline: var(--sp-6);
  margin-block-start: var(--sp-2);
  text-decoration: none; text-align: center;
  transition: transform var(--dur-instant) var(--ease-spring), box-shadow var(--dur-fast) var(--ease-glass);
}
.hero-cta:active { transform: scale(.97); box-shadow: 0 12px 40px -14px rgba(var(--brand-rgb), .60) inset, var(--lg-shadow-hero); }
.hero-cta__label { font-size: var(--t-hero-cta); font-weight: var(--fw-bold); color: #fff; letter-spacing: var(--ls-tight); }
.hero-cta__sub { font-size: var(--t-label); font-weight: var(--fw-medium); color: rgba(255,255,255,.78); }
/* locked future shift: opaque but inert */
.hero-cta--locked { background: var(--brand-deep); cursor: not-allowed; }
.hero-cta--locked:active { transform: none; }
.hero-cta--locked .hero-cta__label { color: rgba(255,255,255,.62); }

/* ============================================================
   §2 ATTENDANCE — (full block; see §2 spec for rationale)
   ============================================================ */
.attendance-stage {
  position: relative; min-block-size: 100dvh; min-block-size: 100vh;
  padding-block-start: var(--sp-5);
  padding-block-end: calc(var(--sp-20) + var(--sp-16));
  padding-inline: var(--sp-4); max-inline-size: 640px; margin-inline: auto;
}
.attendance-panel { padding: var(--sp-4); --att-row-fill: rgba(255,255,255,.86); }
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) .attendance-panel { --att-row-fill: rgba(28,42,60,.88); }
}
.att-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--sp-3); }

.att-row {
  position: relative; isolation: isolate; overflow: hidden;
  inline-size: 100%; min-block-size: 56px;
  display: flex; align-items: center; gap: var(--sp-3);
  padding-block: var(--sp-3); padding-inline: var(--sp-5);
  background: var(--att-row-fill);
  border: 1px solid rgba(var(--brand-rgb), .10);
  border-radius: var(--radius-pill); box-shadow: var(--lg-shadow);
  font: inherit; text-align: start; cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  transition: box-shadow var(--dur-fast) var(--ease-glass);
}
.att-row:active { box-shadow: var(--lg-shadow-lifted); }
.att-row__fill {
  position: absolute; inset: 0; z-index: 0; border-radius: inherit; pointer-events: none;
  transform-origin: inline-start; transform: scaleX(0);
  transition: transform var(--dur-fast) var(--ease-spring), background-color var(--dur-fast) var(--ease-spring);
}
.att-row > :not(.att-row__fill) { position: relative; z-index: 2; }
.att-row__who { display: flex; flex-direction: column; gap: 2px; min-inline-size: 0; flex: 1; }
.att-row__name { font-size: var(--t-body); font-weight: var(--fw-semibold); color: var(--brand-ink); }
.att-row__phone { font-size: var(--t-meta); color: var(--brand-ink-muted); }
.att-row__verdict { flex: 0 0 auto; font-size: var(--t-label); font-weight: var(--fw-bold);
  opacity: 0; transition: opacity var(--dur-fast) var(--ease-glass); }
.att-row__guide { position: absolute; z-index: 1; inset-block: 22%; inset-inline-start: 50%;
  inline-size: 1px; background: rgba(var(--brand-rgb), .18); pointer-events: none;
  transition: opacity var(--dur-fast) var(--ease-glass); }

.att-row.is-present .att-row__fill { transform: scaleX(1); background: var(--ok-soft); }
.att-row.is-present { border-color: rgba(46,125,91,.30); }
.att-row.is-present .att-row__verdict { opacity: 1; color: var(--ok); }
.att-row.is-present .att-row__verdict::after { content: "נוכח ✓"; }
.att-row.is-present .att-row__guide { opacity: 0; }

.att-row.is-absent .att-row__fill { transform: scaleX(.38); background: var(--absent-soft); }
.att-row.is-absent { border-color: rgba(178,58,72,.28); }
.att-row.is-absent .att-row__verdict { opacity: 1; color: var(--absent); order: -1; }
.att-row.is-absent .att-row__verdict::after { content: "נעדר ✕"; }
.att-row.is-absent .att-row__guide { opacity: 0; }

.att-row--tryout { background: rgba(200,134,27,.08); border-color: rgba(200,134,27,.20); }
.att-tray { margin-block-start: var(--sp-5); padding: var(--sp-3);
  background: var(--lg-fill-tint); border: 1px solid rgba(var(--brand-rgb), .14); border-radius: var(--radius-card); }
.att-tray__label { border-block-end: none; color: var(--warn); }

.att-sheet {
  position: fixed; inset-inline: 0; inset-block-end: 0; z-index: 120;
  display: flex; flex-direction: column; gap: var(--sp-3);
  padding-block: var(--sp-4) calc(var(--sp-4) + env(safe-area-inset-bottom, 0px));
  padding-inline: var(--sp-5);
  border-start-start-radius: var(--radius-sheet); border-start-end-radius: var(--radius-sheet);
  border-block-end: 0;
}
.att-sheet__header { position: relative; isolation: isolate; overflow: hidden;
  display: flex; flex-wrap: wrap; align-items: center; gap: var(--sp-2) var(--sp-3);
  padding-block: var(--sp-2); padding-inline: var(--sp-3); border-radius: var(--radius-card); }
.att-sheet__ctx { font-size: var(--t-label); color: var(--brand-ink-muted); }
.att-sheet__tally { margin-inline-start: auto; font-size: var(--t-label); font-weight: var(--fw-semibold); color: var(--brand-ink); }
.att-submit { inline-size: 100%; font-size: var(--t-body); padding-block: calc(var(--sp-3) + 4px); }
.att-submit.is-busy { pointer-events: none; opacity: .85; }
.att-submit.is-done { background: var(--ok); }
@media (max-width: 767px) { .att-sheet__tally { margin-inline-start: 0; inline-size: 100%; } }

@media (prefers-reduced-motion: no-preference) {
  @keyframes att-sheet-rise { from { opacity: 0; transform: translateY(16px); } to { opacity: 1; transform: translateY(0); } }
  .att-sheet { animation: att-sheet-rise var(--dur-slow) var(--ease-glass) both; }
}

/* ============================================================
   §3 Lesson-log — pacing + bar
   ============================================================ */
.lesson-card { display: flex; flex-direction: column; gap: var(--sp-4); padding: var(--sp-6); }
.lesson-card__ctx { margin: 0; font-size: var(--t-label); color: var(--brand-ink-muted); }
.lesson-pacing { display: flex; flex-direction: column; gap: var(--sp-3); }
.lesson-pacing__count { margin: 0; font-size: var(--t-title); font-weight: var(--fw-semibold); color: var(--brand-ink); }
.pace-bar { block-size: 8px; border-radius: var(--radius-pill);
  background: rgba(var(--brand-rgb), .10); overflow: hidden; }
.pace-bar__fill { display: block; block-size: 100%; inline-size: var(--pace, 0%);
  background: var(--brand); border-radius: inherit;
  transition: inline-size var(--dur-mid) var(--ease-spring); }
.pace-bar--over .pace-bar__fill { background: var(--warn); }
.lesson-status { display: flex; flex-direction: column; gap: var(--sp-3); margin-block-start: var(--sp-2); }
.lesson-status .form-actions { border-block-start: none; padding-block-start: 0; }

/* ============================================================
   §4 Syllabus — ordered model rows + link chips + row controls
   ============================================================ */
.curriculum-panel { padding: var(--sp-6); }
.list-summary { font-weight: var(--fw-regular); text-transform: none; letter-spacing: 0;
  color: var(--brand-ink-muted); margin-inline-start: var(--sp-2); }
.syl-model-list { list-style: none; margin: var(--sp-3) 0 0; padding: 0;
  display: flex; flex-direction: column; gap: var(--sp-2); }
.syl-model-row { display: flex; align-items: center; gap: var(--sp-3);
  padding-block: var(--sp-3); padding-inline: var(--sp-4);
  background: rgba(var(--brand-rgb), .04); border: 1px solid rgba(var(--brand-rgb), .09);
  border-radius: var(--radius-chip); }
.syl-model-row__order { flex: 0 0 auto; font-family: var(--font-num); font-weight: var(--fw-bold);
  color: var(--brand); font-size: var(--t-admin-body); }
.syl-model-row__name { flex: 1; min-inline-size: 0; font-size: var(--t-admin-body);
  font-weight: var(--fw-semibold); color: var(--brand-ink); }
.syl-model-row__lessons { flex: 0 0 auto; font-size: var(--t-admin-meta); color: var(--brand-ink-muted); }
.syl-model-row__links { display: flex; gap: var(--sp-1); flex: 0 0 auto; }
.syl-model-row__ctl { display: flex; gap: var(--sp-1); flex: 0 0 auto; }

.link-chip { display: inline-flex; align-items: center; gap: 4px;
  font-size: var(--t-admin-meta); font-weight: var(--fw-medium); color: var(--brand);
  background: var(--lg-fill-tint); border: 1px solid rgba(var(--brand-rgb), .16);
  border-radius: var(--radius-pill); padding-block: 2px; padding-inline: var(--sp-2);
  text-decoration: none; }
.link-chip:hover { background: rgba(var(--brand-rgb), .12); }

.row-ctl { inline-size: 28px; block-size: 28px; border-radius: 50%;
  border: 1px solid rgba(var(--brand-rgb), .14); background: transparent;
  color: var(--brand-ink-muted); cursor: pointer; line-height: 1; font-size: 12px;
  transition: background var(--dur-fast) var(--ease-glass), color var(--dur-fast) var(--ease-glass); }
.row-ctl:hover:not(:disabled) { background: rgba(var(--brand-rgb), .10); color: var(--brand); }
.row-ctl:disabled { opacity: .35; cursor: default; }
.row-ctl--remove:hover { background: var(--absent-soft); color: var(--absent); border-color: rgba(178,58,72,.3); }
/* reorder arrows are vertical — NOT mirrored in RTL (no .icon-directional) */

@media (prefers-reduced-motion: no-preference) {
  /* reorder settle: the swapped fragment fades up (reuse lg-fade-up) */
  #syllabus-models.htmx-settling { animation: lg-fade-up var(--dur-fast) var(--ease-glass) both; }
}

/* ============================================================
   §5 Scheduling — instance rows + date groups + pending chip
   ============================================================ */
.date-group-head { font-size: var(--t-admin-label); font-weight: var(--fw-semibold);
  letter-spacing: var(--ls-wide); color: var(--brand-ink-muted);
  padding-block: var(--sp-3) var(--sp-2); margin-block-start: var(--sp-3);
  border-block-end: 1px solid rgba(var(--brand-rgb), .10); }
.date-group-head:first-child { margin-block-start: 0; }
.inst-list { list-style: none; margin: var(--sp-2) 0 var(--sp-2); padding: 0;
  display: flex; flex-direction: column; gap: var(--sp-2); }
.inst-row { display: flex; align-items: center; gap: var(--sp-3);
  padding-block: var(--sp-3); padding-inline: var(--sp-4);
  background: rgba(var(--brand-rgb), .04); border: 1px solid rgba(var(--brand-rgb), .09);
  border-radius: var(--radius-chip); }
.inst-row__class { flex: 0 0 auto; font-weight: var(--fw-semibold); color: var(--brand-ink); font-size: var(--t-admin-body); }
.inst-row__time { flex: 0 0 auto; font-size: var(--t-admin-body); color: var(--brand-ink); }
.inst-row__who { flex: 1; min-inline-size: 0; font-size: var(--t-admin-body); color: var(--brand-ink-muted); }
.inst-row__who .icon-directional { color: var(--warn); }   /* the ⇄ swap glyph */

/* pending sub-request chip (extends .status-chip family) */
.status-chip--pending { background: var(--warn-soft); color: var(--warn); }
.status-chip--pending::before { content: "●"; }
.status-chip--approved { background: var(--ok-soft); color: var(--ok); }
.status-chip--rejected { background: var(--absent-soft); color: var(--absent); }
.status-chip--rejected::before { content: "✕"; font-size: 9px; }

/* HTMX indicator (generate, etc.) */
.htmx-indicator { opacity: 0; transition: opacity var(--dur-fast) var(--ease-glass); }
.htmx-request .htmx-indicator, .htmx-request.htmx-indicator { opacity: 1; }

/* ---- Responsive: instructor + admin scheduling surfaces ---- */
@media (max-width: 767px) {
  .shift-card { padding: var(--sp-5); }
  .hero-cta { min-block-size: 40vh; }
  .syl-model-row { flex-wrap: wrap; }
  .syl-model-row__links, .syl-model-row__ctl { margin-inline-start: auto; }
  .inst-row { flex-wrap: wrap; }
}
```

**Add into the existing `@supports not (...)` fallback block:**
```css
@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .att-sheet, .attendance-panel { background: var(--lg-fill-solid); }
  .att-sheet__header { background: transparent; }   /* lens/glint already inert without filter support */
}
```

---

## 7. Legibility & perf self-check (contract §5 / budget §10)
- **Attendance:** rows opaque (`--att-row-fill` ~.86), name `--t-body`/600 on near-solid + colored soft fills (`--ok-soft`/`--absent-soft`) → AA clears; the **monolith CTA is solid** Blue-Jay (white ≈6.8:1). ✔ §5.1/§5.3
- **Blur budget on attendance:** exactly 2 — fixed `att-sheet` + static `attendance-panel`; rows/chips/badges/pills zero `backdrop-filter`. ✔ §10.1/10.2
- **`.glass--lensed`:** one instance only — the attendance sheet **header**, via the text-immune `.glass__lens` child; mobile drops the SVG (existing media query), so ≤1 lensed surface and never on mobile GPU. ✔ §10.3 / §2.3 text-immunity rule
- **Other surfaces:** topbar (1 blur) + one panel (2) max; lists scroll opaque; no lensing on lesson-log/syllabus/schedule. ✔
- **Color-coded states are not color-only:** Present/Absent also carry `✓`/`✕` glyphs + verdict text + distinct fill geometry (full vs short wedge) → not reliant on hue alone. ✔ accessibility
- **RTL:** logical properties only; attendance fill grows from leading edge via `transform-origin: inline-start` (no JS flip for animation); tap-half detection mirrored in JS; numerals/times in `<bdi class="num">`; days of week in Hebrew; only `.icon-directional` (chevrons, ↗, ⇄) mirror — reorder ▲▼, checkmarks, status dots never. ✔ §7
- **Idempotency:** attendance POST carries a session idempotency key; 409 handled as a non-duplicate "already saved". ✔ spec §4/§5
- **Fallbacks:** `-webkit-` first + `@supports not` solid for new glass surfaces; reduced-motion/transparency gates inherited + new keyframes wrapped. ✔ §10.6

---

## 8. Deferred / out-of-scope (designer-flagged)

Per global rule §5 (Deferment Transparency), these are explicit, not silent:

- **Drag-to-reorder syllabus models** — designed as a **nice-to-have**; the **shipped baseline is up/down `▲▼` HTMX reorder**. If drag is wanted, it posts the same endpoint. Flagging so the implementer ships up/down and does not fake drag.
- **Instances calendar/grid view** — the instances surface is a **grouped-by-date list**, not a month calendar grid. A true calendar is a later polish; the list satisfies "generated shift instances on a date view." Flagged.
- **Substitution as modal vs page** — the request form is specced as a small `.form-panel` (works as a route or a dialog); I did not pin one. The approval table is fully specced. Implementer's choice on the request container; copy/fields are fixed.
- **Attendance offline queue / service-worker** — out of scope. Resilience here is the **idempotency key + marks-stay-in-DOM-on-fail** (no data loss on a failed submit, retry reuses the key). True offline-first is not designed.
- **Lesson-log "X of N" exact computation** (which prior logs count toward X) — a **server/architecture** concern (uses `expected_lessons_snapshot` for history, live syllabus for current pacing per PRD §6C). The UI only renders X, N, and the overrun state; I did not design the query.
- **Motion-One wiring** for the attendance fill/count-up — the CSS transitions specced here cover the visuals with zero JS deps; Motion-One is optional polish for spring count-ups. The optimistic tap/submit JS itself (half-detection, idempotency, API) **is required** and is described, not coded (this is a design spec).

**Everything else in the brief is specified.** No other items deferred.
