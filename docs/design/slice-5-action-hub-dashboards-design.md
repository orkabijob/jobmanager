# Orkabi — Slice 5 "Action Hub + Real Dashboards" Liquid-Glass Design Spec (the capstone)

> **For:** the implementer subagents. **Status:** design spec, not production code. Every CSS block is a paste-ready *proposal* — verify each token against the live `tokens.css` before pasting (I verified them this session; see §0.5).
> **Binding above this doc:** `docs/design/liquid-glass-design-system.md` (tiers, legibility §5, perf §10, RTL §7, motion §8), plus the **actual** `wwwroot/css/tokens.css` + `wwwroot/css/base.css` — both read in full this session. Every class/token below was confirmed **present** in the live files unless flagged **NEW**.
> **Continuity:** This slice follows the Slice-1..4 spec conventions verbatim (two scales, one shell; one glass panel + the fixed topbar; opaque rows/cards; chips zero `backdrop-filter`; confirmations-only motion; `<bdi class="num">` for every number/date; role-aware subnav; HTMX `_X.cshtml` `outerHTML` row/card swaps). It is the **most design-heavy** slice since Slice 2 because it delivers the *promised* command-center.

---

## 0. What this slice actually is (and what it replaces)

Slice 5 turns four placeholders + one minimal page into the real product surface:

1. **Action Hub** — generalizes the minimal `Pages/Operations/ActionItems/Index.cshtml` (today `AdminOnly`, `ListOpenForRoleAsync(Admin)`, no resolve, no polling) into the **one canonical role-aware hub** with a **Resolve** action and **polling refresh**. Linked from the topbar and every role dashboard.
2. **Admin dashboard bento** — replaces the mock bento in `Pages/Dashboard/Admin.cshtml` (hardcoded `847` clients, fake "גבייה/תשלומים" billing feed that isn't even in the domain) with **real metrics** + an Action-Hub preview in the focal tile.
3. **CS / Logistics / Instructor dashboards** — CS & Logistics are today bare `.dash-stub` "הממשק בפיתוח" cards; Instructor is already real (shift cards) and only gains a "my open tickets" strip. Each becomes a real role landing with metrics + its Action-Hub queue preview + quick links.
4. **Logistics packing list** — the consolidated print-friendly "master packing list" (orders grouped for a packing run), new to this slice.
5. **Carryover polish** — (a) a shared `_PageShell` partial extracted from the topbar+subnav markup repeated **verbatim in 36 pages**; (b) a real **save-success toast** component (the `.toast` class exists and is already used as a bare static div via `TempData["SuccessMessage"]` in 3 Operations pages — we promote it to a motion + auto-dismiss component reusable across all CRUD).

### 0.5 Verification ledger (what I checked in the live repo this session)

**Confirmed PRESENT in `base.css` (reuse verbatim — no redefinition):**
`.dash-shell` · `.dash-topbar`(+`__wordmark`/`__title`/`__user`) · `.dash-body` · `.bento`(+`__metric`/`__feed`/`__tasks`/`__alerts`/`__focal`) · `.bento__tile` · `.tile-head`(+`__label`/`__action`) · `.metric-value` · `.metric-delta`(+`--down`) · `.metric-sublabel` · `.task-list`/`.task-item`(+`__check`/`__text`/`__badge`/`__badge--ok`) · `.feed-list`/`.feed-item`(+`__content`/`__text`/`__time`)/`.feed-dot`(+`--warn`/`--ok`/`--alert`) · `.focal-tile`(+`__empty`/`__icon`/`__hint`) · `.section-label` · `.subnav`/`.subnav__item`(+`.is-active`) · `.page-head`/`.list-head`(+`__title`/`__sub`) · `.data-table`/`.data-row`/`.data-cell`(+`--primary`/`--actions`)/`.data-link` · `.status-chip`(+`--active`/`--off`/`--archived`/`--pending`/`--approved`/`--rejected`/`--muted`/`--packed`) · `.action-card`(+`__top`/`__due`/`__desc`/`__actions`/`__resolved-meta`/`__class`/`.is-resolved`) · `.action-type`(+`__dot`/`--gap`/`--absence`/`--dispute`/`--tryout`/`--birthday`) · `.scope-note` · `.btn-primary`/`.btn-ghost`(+`--sm`/`--accent`) · `.btn-google` · `.empty-state`(+`__icon`/`__title`/`__hint`) · `.count-chip`(+`--tryout`) · `.nav-card`(+`__icon`/`__title`/`__desc`/`__count`)/`.hub-grid` · `.model-chip`(+`__dot`/`--warn`) · `.hours-chip` · `.severity-chip`(+`--low`/`--medium`/`--high`) · `.dispute-note` · `.year-chip`(+`__dot`) · `.form-panel`(+`--instructor`)/`.form-field`/`.form-select` · `.shift-card`(+`__head`/`__time`/`__class`/`__meta`) · `.hero-solid`/`.hero-cta` · `.htmx-indicator` · `.toast`(+`__action`) · `.num` · `.glass`(+`--nav`/`--tile`/`--strong`/`--lifted`/`--lensed`/`__lens`) · `.dash-stub`(+`__title`/`__text`) · the gated keyframes `lg-materialize`/`lg-fade-up`.

**Confirmed in `tokens.css` (all tokens I use exist):** `--brand`/`--brand-rgb`/`--brand-tint`/`--brand-deep`/`--brand-ink`/`--brand-ink-muted`/`--brand-ink-faint` · `--lg-fill`/`--lg-fill-strong`/`--lg-fill-tint`/`--lg-fill-deep`/`--lg-fill-solid` · `--lg-shadow`/`--lg-shadow-lifted`/`--lg-shadow-tile` · `--info` (`rgba(brand,.12)`) · `--ok`/`--ok-soft` · `--warn`/`--warn-soft` · `--absent`/`--absent-soft` · `--radius-pill`/`--radius-chip`/`--radius-card`/`--radius-panel` · `--t-metric`/`--t-metric-label`/`--t-admin-display`/`--t-admin-title`/`--t-admin-body`/`--t-admin-label`/`--t-admin-meta` · `--t-title`/`--t-body`/`--t-label`/`--t-meta` · `--sp-1..--sp-20` · `--dur-instant`/`--dur-fast`/`--dur-mid`/`--dur-slow` · `--ease-glass`/`--ease-spring`/`--ease-out-expo` · `--fw-*` · `--ls-wide`/`--ls-widest`/`--ls-tight` · `--lh-tight`/`--lh-snug`/`--lh-normal` · `--bento-gap`/`--bento-padding`.

**Confirmed DOMAIN (so the spec matches the data exactly):**
- `ActionItemType` (int-backed): `Absence=0, Gap=1, Dispute=2, Task=3, Birthday=4, TryoutFollowup=5`. `ActionItemStatus`: `Open=0, Resolved=1`.
- `ActionItem` fields: `Type, Status, AssignedToRole (string?), AssignedToUserId (int?), RelatedEntityId (int?), Description (Hebrew, server-generated), DueDate (DateOnly?), DeduplicationKey (string?)` + `BaseEntity` audit (`CreatedAt`, …). **There is NO `ResolvedAt`/`ResolvedByUserId` field** (see §1.7 — affects the "✓ טופל · ע״י {name}" meta).
- `AppRoles`: `Admin, CustomerService, Logistics, Instructor` (+ combined `CsOrAdmin`/`InstructorOrAdmin`/`LogisticsOrAdmin`).
- `ActionItemService` today exposes only `ListOpenForRoleAsync(role)` + the `Ensure*` creators. **No Resolve method and no role-OR-user query yet** (server work, §1.7). Routing in code: Gap→Admin, double-Absence→CustomerService, mass-dropout-Absence→Admin, Dispute→Admin, TryoutFollowup→CustomerService, Birthday→ **both** instructor-`AssignedToUserId` **and** Admin-role. Birthday items carry `DueDate`; Gap/Absence/Dispute/Tryout currently don't.
- `_Layout.cshtml` is doc-chrome only (head, the hidden `#lg-refraction` SVG, the `htmx:configRequest` CSRF header on `<body>`, scripts). Every page renders its own `dash-shell`/`dash-topbar`/`subnav` inline → the 36-page repetition the §5 shell fixes. **The HTMX anti-forgery header is already global**, so polling/resolve fragments inherit CSRF for free.
- **No `hx-trigger="every…"` anywhere** → polling is genuinely new this slice.
- `.toast` exists; it's used as a bare `<div class="toast" role="status">@TempData["SuccessMessage"]</div>` in `Operations/ExtraHours`, `Incidents`, `Vacations` — **no motion, no auto-dismiss, no icon**. People/Curriculum/Scheduling CRUD show **no** success feedback today.

**Subnav sets (confirmed, exact Hebrew + href) — used by the §5 shell:**

| Section | Topbar title | Subnav items (order: label → href) |
|---|---|---|
| People | `אנשים` | `סקירה`→`/People` · `בתי ספר`→`/People/Schools` · `כיתות`→`/People/Classes` · `לקוחות`→`/People/Clients` |
| Curriculum | `תכנים` | `סקירה`→`/Curriculum` · `סילבוסים`→`/Curriculum/Syllabi` · `דגמים`→`/Curriculum/Models` |
| Scheduling | `שיבוץ` | `סקירה`→`/Scheduling` · `תבניות`→`/Scheduling/Templates` · `מופעים`→`/Scheduling/Instances` · `החלפות`→`/Scheduling/Substitutions` |
| Operations | `תפעול` | `סקירה`→`/Operations` · `אישור שעות`→`/Operations/ExtraHours` · `דיווחי אירוע`→`/Operations/Incidents` · `אישור חופשות`→`/Operations/Vacations` · `משימות פתוחות`→`/Operations/ActionItems` |
| Logistics | `לוגיסטיקה` | `הזמנות`→`/Logistics/Orders` · `ההזמנות של הכיתה שלי`→`/Logistics/MyOrders` (role-gated) · **NEW** `רשימת אריזה`→`/Logistics/PackingList` (§4) |

> The People/Curriculum/Scheduling topbar titles above are the spec's canonical values; if a live page uses a different exact string, the **shell** centralizes it (one place to set). I did not over-fetch every section's current title string — the shell makes it a single parameter, not 36 edits.

---

## 1. SURFACE 1 — The Action Hub (the headline)

**Route (canonical):** `/Operations/ActionItems` stays the URL (the topbar + every dashboard links here), but it is **re-titled and re-scoped** as *the* Action Hub. The minimal Slice-3 page is absorbed: same route, generalized model.

**Authorization & scope change (from `AdminOnly` → role-aware):** drop `[Authorize(Roles = AppRoles.Admin)]` to `[Authorize]` (any authenticated user) and switch the query from `ListOpenForRoleAsync(Admin)` to a **role-OR-user** query (§1.7). Each user sees:
- items where `AssignedToRole == {their role}`, **plus**
- items where `AssignedToUserId == {their user id}`.
Admin additionally sees all admin-role items (already covered by role match). This is the natural reading of the existing data (birthday items are dual-written to an instructor *user* and to Admin *role*; double-absence/tryout go to the CS *role*; gap/dispute/dropout go to the Admin *role*).

### 1.1 ASCII wireframe (Admin view; other roles identical chrome, their own queue)

```
┌─ glass glass--nav dash-topbar ─ עורקבי · מרכז הפעולות · {שלום, מנהל} ─────────────┐
  dash-body
  ┌ subnav (Operations, role-aware) — [משימות פתוחות] is-active ───────────────────┐

  ┌ page-head ─────────────────────────────────────────────────────────────────────┐
  │  מרכז הפעולות                                            ┌ hub-poll ───────────┐ │
  │  כל המשימות הפתוחות שדורשות טיפול                         │ ● מתעדכן אוטומטית   │ │
  │                                                          └─────────────────────┘ │
  │  ┌ filter rail: status pills (GET) ────────────────────────────────────────────┐│
  │  │ [פתוחות] [הכול]              סינון לפי סוג: [ כל הסוגים ▾ ]                   ││
  │  └──────────────────────────────────────────────────────────────────────────────┘│
  └───────────────────────────────────────────────────────────────────────────────────┘

  ┌ glass glass--tile people-panel  (the ONE glass surface; cards scroll opaque) ─────┐
  │  ┌ hub-list  (hx-trigger="every 30s" polling target) ─────────────────────────┐  │
  │  │  ┌ action-card ─ type=Dispute (urgent) ─────────────────────────────────┐  │  │
  │  │  │ [⬤ מחלוקת]                                              ●פתוח          │  │  │
  │  │  │ מחלוקת על הזמנה לוגיסטית: כיתה ג׳1 · דגם "קופסת אוצרות".               │  │  │
  │  │  │                                                  [ סמן כטופל ]         │  │  │
  │  │  └─────────────────────────────────────────────────────────────────────────┘  │  │
  │  │  ┌ action-card ─ type=Gap ──────────────────────────────────────────────┐  │  │
  │  │  │ [⬤ חריגת קצב]                                          ●פתוח          │  │  │
  │  │  │ חריגת קצב: כיתה "ג׳2" · דגם "קופסת אוצרות" — בוצעו 9 שיעורים מתוך 8.   │  │  │
  │  │  │                                                  [ סמן כטופל ]         │  │  │
  │  │  └─────────────────────────────────────────────────────────────────────────┘  │  │
  │  │  ┌ action-card ─ type=Birthday ─────────────────────────────────────────┐  │  │
  │  │  │ [⬤ יום הולדת]                          יעד: 26 ביוני     ●פתוח        │  │  │
  │  │  │ יום הולדת מחר: דנה לוי.                                                │  │  │
  │  │  │                                                  [ סמן כטופל ]         │  │  │
  │  │  └─────────────────────────────────────────────────────────────────────────┘  │  │
  │  └──────────────────────────────────────────────────────────────────────────────┘  │
  └─────────────────────────────────────────────────────────────────────────────────────┘
```

Empty state (no open items in the user's queue):
```
  ┌ glass glass--tile people-panel ───────────────────────────────────────────────────┐
  │            (empty-state)                                                            │
  │                 ✓                                                                    │
  │            אין משימות פתוחות                                                         │
  │      כל המשימות טופלו. התראות חדשות יופיעו כאן אוטומטית.                              │
  └─────────────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Markup skeleton (page) — reuses the Slice-3 page almost verbatim; adds the poll wrapper + resolve

```html
@* /Operations/ActionItems/Index.cshtml — the canonical Action Hub *@
@{ ViewData["Title"] = "מרכז הפעולות"; }

@* SHELL via the new partial (§5). Title + active subnav key are parameters. *@
<partial name="_PageShell" model="@(new PageShellVm {
    Section = NavSection.Operations, ActiveKey = "/Operations/ActionItems",
    Title = "מרכז הפעולות", Greeting = Model.Greeting })" view-data="..." />
@* (or keep the inline shell until §5 lands; the hub does not depend on the shell) *@

<main class="dash-body">
  <nav class="subnav" aria-label="ניווט תפעול"> … role-aware items, [משימות פתוחות] is-active … </nav>

  <div class="page-head">
    <div>
      <h1 class="page-head__title">מרכז הפעולות</h1>
      <p class="page-head__sub">כל המשימות הפתוחות שדורשות טיפול</p>
    </div>
    <span class="hub-poll" aria-live="off">
      <span class="hub-poll__dot" aria-hidden="true"></span>מתעדכן אוטומטית
    </span>
  </div>

  @* status + type filters — plain GET, no HTMX (Classes-filter precedent) *@
  <form method="get" class="classes-filter-rail" aria-label="סינון משימות">
    <div class="status-pill-rail" role="tablist" aria-label="סינון לפי סטטוס">
      <button class="status-pill status-pill--on" name="status" value="Open">פתוחות</button>
      <button class="status-pill" name="status" value="">הכול</button>
    </div>
    <div class="filter-group">
      <label class="form-field__label" for="typeFilter">סינון לפי סוג:</label>
      <select id="typeFilter" name="type" class="form-select filter-select" onchange="this.form.submit()">
        <option value="">כל הסוגים</option>
        <option value="Gap">חריגת קצב</option>
        <option value="Absence">היעדרות</option>
        <option value="Dispute">מחלוקת</option>
        <option value="Task">משימה</option>
        <option value="Birthday">יום הולדת</option>
        <option value="TryoutFollowup">מעקב ניסיון</option>
      </select>
    </div>
  </form>

  <div class="glass glass--tile people-panel" style="--lg-tile-shadow: var(--lg-shadow);">
    @* THE POLLING TARGET — a partial that re-fetches itself every 30s *@
    <div id="hub-list" class="action-list"
         hx-get="?handler=OpenList&status=@Model.StatusFilter&type=@Model.TypeFilter"
         hx-trigger="every 30s"
         hx-swap="outerHTML"
         hx-sync="this:replace">
      <partial name="_ActionHubList" model="Model.Items" />
    </div>
  </div>
</main>
```

`_ActionHubList.cshtml` (the **polling fragment** — one swappable node, id `hub-list`):
```html
@model IReadOnlyList<ActionItem>
<div id="hub-list" class="action-list"
     hx-get="?handler=OpenList&status=@(ViewBag.StatusFilter)&type=@(ViewBag.TypeFilter)"
     hx-trigger="every 30s" hx-swap="outerHTML" hx-sync="this:replace">
  @if (!Model.Any())
  {
    <div class="empty-state">
      <span class="empty-state__icon" aria-hidden="true">✓</span>
      <p class="empty-state__title">אין משימות פתוחות</p>
      <p class="empty-state__hint">כל המשימות טופלו. התראות חדשות יופיעו כאן אוטומטית.</p>
    </div>
  }
  else
  {
    @foreach (var item in Model)
    {
      <partial name="_ActionCard" model="item" />
    }
  }
</div>
```

`_ActionCard.cshtml` (the **resolve fragment** — one swappable node, id `action-{id}`; the `@functions` Type→Hebrew/CSS maps already live in the Slice-3 page, move them into the partial or a helper):
```html
@model ActionItem
<article class="action-card" id="action-@Model.Id" data-type="@Model.Type">
  <div class="action-card__top">
    <span class="action-type @TypeToCssModifier(Model.Type)">
      <span class="action-type__dot" aria-hidden="true"></span>@TypeToHebrew(Model.Type)
    </span>
    @if (Model.DueDate.HasValue)
    {
      <span class="action-card__due">יעד:
        <bdi class="num">@Model.DueDate.Value.ToString("d בMMMM", new System.Globalization.CultureInfo("he-IL"))</bdi>
      </span>
    }
    <span class="status-chip status-chip--active">פתוח</span>
  </div>
  <p class="action-card__desc">@Model.Description</p>
  <div class="action-card__actions">
    <button class="btn-ghost btn-ghost--sm btn-ghost--accent"
            hx-post="?handler=Resolve&id=@Model.Id"
            hx-target="#action-@Model.Id" hx-swap="outerHTML">סמן כטופל</button>
  </div>
</article>
```

After **Resolve** (server returns the resolved fragment when filter = "הכול", or an **empty body** when filter = "פתוחות" so the card vanishes):
```html
<article class="action-card is-resolved" id="action-15" data-type="Gap">
  <div class="action-card__top">
    <span class="action-type action-type--gap"><span class="action-type__dot" aria-hidden="true"></span>חריגת קצב</span>
    <span class="status-chip status-chip--muted">טופל</span>
  </div>
  <p class="action-card__desc">חריגת קצב: כיתה "ג׳2" · דגם "קופסת אוצרות" — בוצעו <bdi class="num">9</bdi> שיעורים מתוך <bdi class="num">8</bdi> צפויים.</p>
  <span class="action-card__resolved-meta">✓ טופל</span>
</article>
```

### 1.3 Type → Hebrew → badge mapping (the FULL enum; already in the Slice-3 page + base.css)

| `ActionItemType` | Hebrew label | Badge class | Dot/accent |
|---|---|---|---|
| `Gap` | `חריגת קצב` | `.action-type--gap` | `--warn` |
| `Absence` | `היעדרות` | `.action-type--absence` | `--absent` |
| `Dispute` | `מחלוקת` | `.action-type--dispute` | `--absent` |
| `Task` | `משימה` | `.action-type` (base) | `--brand` |
| `Birthday` | `יום הולדת` | `.action-type--birthday` | `--brand-tint` |
| `TryoutFollowup` | `מעקב ניסיון` | `.action-type--tryout` | `--warn` |

> These six rows already render correctly today — the `@functions TypeToHebrew/TypeToCssModifier` block in the Slice-3 page and the `.action-type--*` CSS both cover the full enum. Slice 5 changes **nothing** about the badge; it only adds the resolve button, polling, filters, role-scope, and the empty-state icon.

### 1.4 Status chip semantics (reuse existing — no new chip)

`פתוח` (Open) → `.status-chip--active` (its `--ok` dot reads as "live/open"; established in Slice 3). `טופל` (Resolved) → `.status-chip--muted` (exists). No new status-chip variant. **Do not** use `--pending` for Open — "Open" ≠ "awaiting approval".

### 1.5 Exact Hebrew copy

| Element | Hebrew |
|---|---|
| Topbar title | `מרכז הפעולות` |
| Page title | `מרכז הפעולות` |
| Page sub | `כל המשימות הפתוחות שדורשות טיפול` |
| Polling indicator | `מתעדכן אוטומטית` |
| Status filter pills | `פתוחות` · `הכול` |
| Type filter label / all-option | `סינון לפי סוג:` · `כל הסוגים` |
| Type filter options | `חריגת קצב` · `היעדרות` · `מחלוקת` · `משימה` · `יום הולדת` · `מעקב ניסיון` |
| Type badges | (same six as the filter) |
| Due date | `יעד: <bdi class="num">{D ב{חודש}}</bdi>` |
| Status (open) | `פתוח` |
| Status (resolved) | `טופל` |
| Resolve action | `סמן כטופל` |
| Resolved meta (after) | `✓ טופל` (or `✓ טופל · ע״י {שם}` only if the server stores the resolver — §1.7) |
| Empty (queue clear) | title `אין משימות פתוחות` · hint `כל המשימות טופלו. התראות חדשות יופיעו כאן אוטומטית.` |
| Empty (Admin "all" filter, none ever) | title `אין משימות` · hint `כשתיווצר התראה היא תופיע כאן.` |

> Descriptions are **server-generated** and already correct in `ActionItemService` (e.g. `חריגת קצב: כיתה "{name}" · דגם "{name}" — בוצעו {spent} שיעורים מתוך {expected} צפויים.`, `מחלוקת על הזמנה לוגיסטית: כיתה {name} · דגם {name}.`, `יום הולדת מחר: {name}.`). The card renders whatever Hebrew the server wrote — **no UI copy work for descriptions.** One server nicety (flagged §1.7): wrap the numerals in the gap/absence descriptions in `<bdi class="num">` at generation time, since they currently emit bare digits inside a Hebrew string.

### 1.6 Interaction / motion + polling map

| Trigger | `hx-*` | Target | Result |
|---|---|---|---|
| **Auto-poll** | `hx-get="?handler=OpenList&status=&type="` `hx-trigger="every 30s"` `hx-swap="outerHTML"` `hx-sync="this:replace"` | `#hub-list` | re-renders the open list; new items appear, resolved-elsewhere items drop |
| **Resolve** | `hx-post="?handler=Resolve&id={id}"` `hx-target="#action-{id}"` `hx-swap="outerHTML"` | `#action-{id}` | card → resolved fragment (filter=הכול) **or** empty (filter=פתוחות) → card animates out |
| Status/type filter | plain GET `?status=&type=` (full nav) | — | server re-queries; no HTMX |

**Motion (§8 — confirmations only):**
- **Resolve confirmation:** the card swaps; on settle it plays the **gated** `lg-fade-up`. When the open-only filter drops the card, animate it out with a short collapse (`hub-card-leave`, NEW, gated) so it doesn't pop. The status-chip recolor + dim-to-`.is-resolved` *is* the confirmation. Reduced-motion: instant.
- **Polling is silent and must not thrash layout (perf §10).** Three rules: (1) `hx-sync="this:replace"` cancels an in-flight poll if a resolve is mid-swap (no double-swap jank); (2) the swap is `outerHTML` of the **list container only**, never the panel/topbar (no blur re-paint); (3) **newly-arrived** cards (present in the new fragment, absent before) get a one-time `hub-card-enter` fade (NEW, gated) — the server marks them by ordering newest-by-`CreatedAt` and the CSS targets `[data-new]` (server stamps `data-new` on cards created since the last poll cursor, OR simpler: skip per-card entrance entirely and let the whole list cross-fade once via `.htmx-settling` — **preferred**, zero per-row work, matches the Slice-4 generate-body precedent). **Default: list-level settle fade, no per-card entrance.** The `[data-new]` path is offered only if the team wants the subtler "just this one slid in" feel; it is **flagged optional**, not required.
- **The polling indicator** is a small dot + label in the page-head. It is NOT a spinner that pulses every 30s (that's noise). It pulses **once per actual swap** via `.htmx-request` on the list (reuse the existing `.htmx-indicator` opacity convention): at rest a steady `--ok` dot; while a poll/resolve request is in flight, the dot brightens (opacity ramp). Under reduced-motion it's a static dot only. `aria-live="off"` (silent — the list isn't an announce surface; a screen-reader user resolving an item hears the button, and the 30s churn must not spam them).

### 1.7 Server work this surface needs (flagged — NOT designed here, but the UI assumes it)

These are **server/architecture** concerns the implementer must wire; the UI is designed against them:
1. **`ListOpenForUserOrRoleAsync(userId, role, status?, type?)`** — the role-OR-user query + optional status/type filters. (Today only `ListOpenForRoleAsync(role)` exists.)
2. **`ResolveAsync(id, resolvingUser)`** — sets `Status = Resolved` and, per the existing `ActionItemService` dedup-invariant comment, **MUST null `DeduplicationKey`** so a future recurrence can re-open. Resource-check that the item belongs to the caller's role/user before resolving.
3. **`OnGetOpenListAsync` / `OnPostResolveAsync` handlers** returning the `_ActionHubList` / `_ActionCard` partials.
4. **Optional `ResolvedByUserId`/`ResolvedAt`** — *if* the team wants `✓ טופל · ע״י {שם}` (and a future "resolved" history), add these two fields. **Without them the UI shows plain `✓ טופל`** (the design degrades cleanly). Flagged as a small schema add, the team's call.
5. **`<bdi>`-wrap numerals in server descriptions** (cosmetic RTL nicety for the existing gap/absence strings).

> **The hub does not depend on the §5 shell** — it can ship with the inline shell and adopt `_PageShell` when that lands. Decoupled on purpose.

---

## 2. SURFACE 2 — Admin dashboard bento (REAL data)

**File:** `Pages/Dashboard/Admin.cshtml`. **Keep the exact bento geometry** (`.bento` + the five named areas `__metric`/`__focal`/`__feed`/`__tasks`/`__alerts` — all confirmed in base.css, with the responsive collapse already handled). We only **replace the contents** of each tile with real metrics and **repoint** the focal tile at the Action Hub. This is a content + small-CSS change, not a layout rewrite.

### 2.1 The five tiles: mock → real (tile-by-tile)

| Bento area | Today (MOCK) | Slice-5 REAL content | Source / link |
|---|---|---|---|
| `bento__metric` (tall hero, lensed) | `847 לקוחות פעילים · +12 החודש` | **Active clients** count (real) — `<bdi class="num">{count}</bdi>` + sublabel. Keep `.glass--lensed` (the ONE allowed lensed surface on this page, §9.3 / perf §10.3). | `→ /People/Clients` |
| `bento__focal` (center, all rows, lensed) | empty "מרכז פעילות" placeholder with `⌘` | **Action-Hub preview** — the top N open admin-queue items as compact rows + a count header + "פתח את מרכז הפעולות" link. This is the command-center the bento promised. | `→ /Operations/ActionItems` |
| `bento__feed` (start col, rows 2–3) | mock task list (`עדכון תוכנית לימודים`, `בדיקת דוח גבייה` …) | **Pending approvals queue** — real counts/rows: extra-hours awaiting approval + vacation requests awaiting approval, each a row linking to its approval list. | `→ /Operations/ExtraHours`, `→ /Operations/Vacations` |
| `bento__tasks` (end col, rows 1–2) | mock "התראות" feed (`דוד כהן`, `תשלום ממתין` — billing isn't in the domain) | **Real alerts feed** — recent system events that exist: new open action-items by type (gap/dispute/absence), recent incidents. Each `.feed-item` links to source. | `→ /Operations/ActionItems`, `→ /Operations/Incidents` |
| `bento__alerts` (end col, row 3) | mock quick-stats `6 / 34 / 3` | **Real quick-stats** — today's shifts, active classes, open logistics orders (incl. disputed). | `→ /Scheduling/Instances`, `→ /People/Classes`, `→ /Logistics/Orders` |

> **Why these metrics:** they are all queryable from the real domain (People/Scheduling/Operations/Logistics/ActionHub) and avoid the mock's invented "גבייה/תשלומים" billing concept, which has no entity. The focal tile becomes the Action-Hub preview because §9.3 names the ticketing hub as the focal point — and Slice 5 finally has real tickets to show.

### 2.2 ASCII wireframe (real bento)

```
┌ bento ───────────────────────────────────────────────────────────────────────────────┐
│ ┌ __metric (lensed) ─┐ ┌ __focal (lensed) — ACTION-HUB PREVIEW ─────┐ ┌ __tasks ─────┐ │
│ │ לקוחות פעילים       │ │ מרכז הפעולות            פתח את המרכז → │ │ התראות       │ │
│ │                     │ │ ┌ hub-mini ──────────────────────────────┐ │ │ ⬤ מחלוקת חדשה │ │
│ │   847               │ │ │ [⬤מחלוקת] מחלוקת על הזמנה · כיתה ג׳1   │ │ │   כיתה ג׳1    │ │
│ │   .num metric       │ │ │ [⬤חריגת קצב] כיתה ג׳2 · דגם קופסת…     │ │ │ ⬤ חריגת קצב   │ │
│ │  +12 החודש          │ │ │ [⬤היעדרות] היעדרות כפולה · נועם…       │ │ │   כיתה ה׳3    │ │
│ │ סך הלקוחות הרשומים   │ │ │ [⬤יום הולדת] יום הולדת מחר · דנה לוי   │ │ │ ⬤ אירוע (גבוה)│ │
│ │                     │ │ └────────────────────────────────────────┘ │ │   ה׳3 · אתמול │ │
│ └─────────────────────┘ │ ┌ hub-mini__more ─ עוד 6 משימות פתוחות → ┐ │ │ כל ההתראות → │ │
│                         │ └────────────────────────────────────────┘ │ └──────────────┘ │
│ ┌ __feed — APPROVALS QUEUE ───────────────┐ │  (focal spans rows 1–3)    │ ┌ __alerts ───┐ │
│ │ אישורים ממתינים                          │ │                            │ │ סטטוס מהיר   │ │
│ │ ⏱ שעות נוספות               3  →         │ │                            │ │ מפגשים היום 6│ │
│ │ 🏖 בקשות חופשה               1  →         │ │                            │ │ כיתות פעילות 12│
│ │                                          │ │                            │ │ הזמנות פתוחות 4│
│ │ + כל האישורים →                          │ │                            │ │ במחלוקת      1│ │
│ └──────────────────────────────────────────┘ │                            │ └─────────────┘ │
└────────────────────────────────────────────────────────────────────────────────────────────┘
```

### 2.3 Markup skeletons (only the changed tile bodies; geometry/classes unchanged)

**Metric tile (real count; keep lensed):**
```html
<div class="glass glass--tile glass--lensed bento__tile bento__metric" data-glint>
  <div class="glass__lens" aria-hidden="true"></div>
  <div class="tile-head"><span class="tile-head__label">לקוחות פעילים</span></div>
  <div>
    <div class="metric-value num">@Model.ActiveClients</div>
    @if (Model.ClientsDeltaThisMonth != 0)
    {
      <div style="display:flex;align-items:center;gap:var(--sp-2);margin-block-start:var(--sp-2);">
        <span class="metric-delta @(Model.ClientsDeltaThisMonth < 0 ? "metric-delta--down" : "")">
          @(Model.ClientsDeltaThisMonth >= 0 ? "+" : "")<bdi class="num">@Model.ClientsDeltaThisMonth</bdi> החודש
        </span>
      </div>
    }
    <p class="metric-sublabel">סך הלקוחות הפעילים במערכת</p>
  </div>
</div>
```

**Focal tile = Action-Hub preview** (uses the NEW `.hub-mini` micro-list — opaque, zero blur, lives inside the lensed tile but is text so it sits at z-index:2 above the lens; legibility on `--lg-fill-strong`):
```html
<div class="glass glass--tile glass--lensed bento__tile bento__focal focal-tile" data-glint>
  <div class="glass__lens" aria-hidden="true"></div>
  <div class="tile-head">
    <span class="tile-head__label">מרכז הפעולות</span>
    <a href="/Operations/ActionItems" class="tile-head__action">פתח את המרכז</a>
  </div>
  @if (Model.HubPreview.Count == 0)
  {
    <div class="focal-tile__empty">
      <div class="focal-tile__icon" aria-hidden="true">✓</div>
      <p class="focal-tile__hint">אין משימות פתוחות. התראות חדשות יופיעו כאן.</p>
    </div>
  }
  else
  {
    <ul class="hub-mini" role="list">
      @foreach (var it in Model.HubPreview) // top ~5 by CreatedAt
      {
        <li class="hub-mini__item">
          <span class="action-type @TypeToCssModifier(it.Type)">
            <span class="action-type__dot" aria-hidden="true"></span>@TypeToHebrew(it.Type)
          </span>
          <a href="/Operations/ActionItems" class="hub-mini__desc">@it.Description</a>
        </li>
      }
    </ul>
    @if (Model.OpenCount > Model.HubPreview.Count)
    {
      <a href="/Operations/ActionItems" class="hub-mini__more">
        עוד <bdi class="num">@(Model.OpenCount - Model.HubPreview.Count)</bdi> משימות פתוחות ←
      </a>
    }
  }
</div>
```

**Approvals-queue tile** (`bento__feed`) — reuses `.task-list`/`.task-item` shape but as count rows (NEW tiny `.stat-row` is cleaner than abusing `.task-item` which has a checkbox; see §2.4 — but **prefer reusing `.feed-item` minus the dot** OR the NEW `.stat-row`; I specify `.stat-row` because it's a clean label↔count↔link primitive reused by `bento__alerts` too):
```html
<div class="glass glass--tile bento__tile bento__feed" style="--lg-tile-shadow: var(--lg-shadow);">
  <div class="tile-head"><span class="tile-head__label">אישורים ממתינים</span></div>
  <ul class="stat-list" role="list">
    <li class="stat-row">
      <span class="stat-row__label">שעות נוספות</span>
      <a class="stat-row__value" href="/Operations/ExtraHours"><bdi class="num">@Model.PendingExtraHours</bdi></a>
    </li>
    <li class="stat-row">
      <span class="stat-row__label">בקשות חופשה</span>
      <a class="stat-row__value" href="/Operations/Vacations"><bdi class="num">@Model.PendingVacations</bdi></a>
    </li>
  </ul>
  <a href="/Operations" class="tile-head__action" style="margin-block-start:auto;">כל האישורים ←</a>
</div>
```

**Alerts feed** (`bento__tasks`) — reuse `.feed-list`/`.feed-item`/`.feed-dot` verbatim; map dot color to type (dispute/absence = `--alert`, gap = `--warn`, resolved/info = `--ok`/default), make each item a link:
```html
<ul class="feed-list" role="list">
  <li class="feed-item">
    <span class="feed-dot feed-dot--alert" aria-hidden="true"></span>
    <div class="feed-item__content">
      <a class="feed-item__text" href="/Operations/ActionItems">מחלוקת חדשה · כיתה ג׳1</a>
      <bdi class="feed-item__time num">09:14</bdi>
    </div>
  </li>
  …
</ul>
```

**Quick-stats** (`bento__alerts`) — replace the inline-styled divs with the same `.stat-row` primitive (removes the inline-style cruft the mock used):
```html
<ul class="stat-list" role="list">
  <li class="stat-row"><span class="stat-row__label">מפגשים היום</span>
    <a class="stat-row__value" href="/Scheduling/Instances"><bdi class="num">@Model.ShiftsToday</bdi></a></li>
  <li class="stat-row"><span class="stat-row__label">כיתות פעילות</span>
    <a class="stat-row__value" href="/People/Classes"><bdi class="num">@Model.ActiveClasses</bdi></a></li>
  <li class="stat-row"><span class="stat-row__label">הזמנות פתוחות</span>
    <a class="stat-row__value" href="/Logistics/Orders"><bdi class="num">@Model.OpenOrders</bdi></a></li>
  <li class="stat-row stat-row--alert"><span class="stat-row__label">במחלוקת</span>
    <a class="stat-row__value" href="/Logistics/Orders?status=Disputed"><bdi class="num">@Model.DisputedOrders</bdi></a></li>
</ul>
```

### 2.4 Exact Hebrew copy (Admin bento)

| Tile | Element | Hebrew |
|---|---|---|
| metric | label / sublabel | `לקוחות פעילים` / `סך הלקוחות הפעילים במערכת` |
| metric | delta | `+<bdi class="num">{N}</bdi> החודש` (down: no `+`, `.metric-delta--down`) |
| focal | label / action | `מרכז הפעולות` / `פתח את המרכז` |
| focal | more-link | `עוד <bdi class="num">{N}</bdi> משימות פתוחות ←` |
| focal | empty | `אין משימות פתוחות. התראות חדשות יופיעו כאן.` |
| feed (approvals) | label | `אישורים ממתינים` |
| feed | rows | `שעות נוספות` · `בקשות חופשה` |
| feed | footer link | `כל האישורים ←` |
| tasks (alerts) | label / action | `התראות` / `כל ההתראות` |
| alerts (quick) | label | `סטטוס מהיר` |
| alerts | rows | `מפגשים היום` · `כיתות פעילות` · `הזמנות פתוחות` · `במחלוקת` |

### 2.5 Motion / perf for the bento

- The bento already has a **gated** staggered tile entrance (`.bento__tile:nth-child(n)` keyframes in base.css) — keep it; it's once-per-load and gated. **No new entrance motion.**
- **Lensing budget:** exactly **two** `.glass--lensed` tiles (metric + focal) as today — within §10.3 ("≤1 instance on screen" is the *mobile* rule; desktop bento allows the two hero tiles, and base.css already drops `.glass__lens` filter < 768px). On mobile the lens filter is auto-dropped (confirmed `@media (max-width:767px){ .glass__lens{filter:none} }`), so the mobile bento has **zero** active lenses — compliant.
- **No metric count-up animation** by default (the doc allows it §8, but it's a per-load flourish on an Admin surface opened often; keep instant). If wanted later it's a JS add, flagged not built.
- `.hub-mini`/`.stat-list` are **plain text/links, zero `backdrop-filter`** — they sit on the tile's `--lg-fill-strong`, AA-safe.

---

## 3. SURFACE 3 — CS / Logistics / Instructor dashboards (real surfaces)

Each role gets a real landing built from the **same vocabulary** (bento tiles or a simpler stack + the `.hub-mini` Action-Hub preview + `.stat-list` metrics + `.nav-card` quick links). CS and Logistics replace their `.dash-stub`. Instructor keeps its shift cards and gains a tickets strip.

### 3.1 Shared pattern: every role dashboard has

1. a **metrics strip** (`.stat-list` or 1–2 `bento__tile` metric tiles) of that role's real numbers,
2. an **Action-Hub queue preview** (`.hub-mini`, same component as the Admin focal tile) showing *their* open items, linking to `/Operations/ActionItems`,
3. **quick links** (`.hub-grid` of `.nav-card`s) to that role's primary surfaces.

For CS & Logistics I use a **simpler responsive layout than the Admin bento** — a `.dash-grid` (NEW, a plain auto-fit grid) of glass tiles, because these roles have fewer focal metrics than Admin and the asymmetric 3-col bento would feel empty. The Admin bento stays the one true bento (§9.3).

### 3.2 CS dashboard (`Pages/Dashboard/Cs.cshtml`) — replaces the stub

**Role focus (from the domain):** CS owns **tryout follow-ups** (`TryoutFollowup`→CS) and **double-absence** alerts (`Absence`→CS), plus client/enrollment stats.

```
┌ dash-shell · topbar: שירות לקוחות · {שלום, {name}} ──────────────────────────────────┐
  dash-body
  ┌ dash-grid ─────────────────────────────────────────────────────────────────────────┐
  │ ┌ glass--tile — TICKETS (wide) ───────────────┐ ┌ glass--tile — STATS ─────────────┐ │
  │ │ המשימות שלי            פתח את המרכז → │ │ נתוני לקוחות                       │ │
  │ │ ┌ hub-mini ─────────────────────────────────┐ │ │ לקוחות פעילים            847       │ │
  │ │ │ [⬤מעקב ניסיון] יש ליצור קשר · נועם (ג׳2)  │ │ │ ניסיונות החודש          5         │ │
  │ │ │ [⬤היעדרות] היעדרות כפולה · דנה (ה׳3)       │ │ │ לקוחות חדשים החודש      12        │ │
  │ │ └────────────────────────────────────────────┘ │ └───────────────────────────────────┘ │
  │ └──────────────────────────────────────────────┘                                       │
  │ ┌ hub-grid — quick links ─────────────────────────────────────────────────────────────┐│
  │ │ [👥 לקוחות] [🏫 כיתות] [✅ מרכז הפעולות]                                              ││
  │ └──────────────────────────────────────────────────────────────────────────────────────┘│
  └─────────────────────────────────────────────────────────────────────────────────────────┘
```

| Element | Hebrew |
|---|---|
| Topbar title | `שירות לקוחות` |
| Tickets tile label / action | `המשימות שלי` / `פתח את המרכז` |
| Stats tile label | `נתוני לקוחות` |
| Stat rows | `לקוחות פעילים` · `ניסיונות החודש` · `לקוחות חדשים החודש` |
| Quick links | `לקוחות`→`/People/Clients` · `כיתות`→`/People/Classes` · `מרכז הפעולות`→`/Operations/ActionItems` |
| Empty tickets | `אין משימות פתוחות. מעקבי ניסיון והתראות יופיעו כאן.` |

### 3.3 Logistics dashboard (`Pages/Dashboard/Logistics.cshtml`) — replaces the stub

**Role focus:** orders to **pack** (Pending), **disputes** (Dispute→Admin, but Logistics sees the order side), order throughput. Quick link to the new **packing list** (§4).

```
┌ dash-shell · topbar: לוגיסטיקה · {שלום, {name}} ─────────────────────────────────────┐
  dash-body
  ┌ dash-grid ─────────────────────────────────────────────────────────────────────────┐
  │ ┌ glass--tile metric ─────────┐ ┌ glass--tile — STATS ───────────────────────────┐ │
  │ │ הזמנות לאריזה                │ │ סטטוס הזמנות                                    │ │
  │ │      8                        │ │ ממתין לאריזה          8                         │ │
  │ │ .num metric                   │ │ נארז                  14                        │ │
  │ │ ממתינות לאריזה כעת            │ │ במחלוקת               1   (alert)               │ │
  │ └───────────────────────────────┘ └─────────────────────────────────────────────────┘ │
  │ ┌ glass--tile — TICKETS (my queue) ─────────────┐ ┌ hub-grid — quick links ──────────┐ │
  │ │ המשימות שלי          פתח → │ │ [📦 הזמנות] [🖨 רשימת אריזה]      │ │
  │ │ ┌ hub-mini ── (empty or dispute items) ───────┐ │ │ [✅ מרכז הפעולות]                 │ │
  │ │ └──────────────────────────────────────────────┘ │ └───────────────────────────────────┘ │
  │ └────────────────────────────────────────────────┘                                       │
  └─────────────────────────────────────────────────────────────────────────────────────────┘
```

| Element | Hebrew |
|---|---|
| Topbar title | `לוגיסטיקה` |
| Metric tile label / sublabel | `הזמנות לאריזה` / `ממתינות לאריזה כעת` |
| Stats tile label | `סטטוס הזמנות` |
| Stat rows | `ממתין לאריזה` · `נארז` · `במחלוקת` |
| Tickets tile label / action | `המשימות שלי` / `פתח` |
| Quick links | `הזמנות`→`/Logistics/Orders` · `רשימת אריזה`→`/Logistics/PackingList` · `מרכז הפעולות`→`/Operations/ActionItems` |
| Empty tickets | `אין משימות פתוחות.` |

### 3.4 Instructor dashboard (`Pages/Dashboard/Instructor.cshtml`) — augment, don't replace

The instructor home is **already real** (greeting, today's shift cards, the `קח נוכחות` monolith). **Keep all of it.** Add **one** thing: a compact "my open tickets" strip **below** the shift cards (instructors get `Birthday` user-assigned items and any user-assigned tasks). Reuse `.section-label` + `.hub-mini`, instructor-scaled.

```
  … existing today-head + shift-card(s) + monolith (UNCHANGED) …

  ── המשימות שלי ──                                       (section-label)
  ┌ glass glass--tile (form-panel--instructor scale) ─────────────────────────┐
  │ ┌ hub-mini ─────────────────────────────────────────────────────────────┐ │
  │ │ [⬤יום הולדת] יום הולדת מחר: דנה לוי          יעד: 26 ביוני             │ │
  │ └───────────────────────────────────────────────────────────────────────┘ │
  │                                              פתח את מרכז הפעולות →         │
  └─────────────────────────────────────────────────────────────────────────────┘
```

> Only render the strip if the instructor has ≥1 open ticket (instructors open this 20×/day — an empty tickets box every time is noise; per §8 "instructor home opened ~20×/day, keep it functional"). **No empty-state box for the instructor tickets strip** — when empty, omit the whole section. Flagged as a deliberate choice.

| Element | Hebrew |
|---|---|
| Section label | `המשימות שלי` |
| Open-hub link | `פתח את מרכז הפעולות ←` |
| (tickets use the same `.hub-mini` + `.action-type` badges as everywhere) | |

### 3.5 Motion / perf (all three dashboards)

- Topbar is the **only** blur (`glass--nav`). Tiles are `.glass--tile` (one blur each, but they're static, not scrolling — within budget; on mobile the `dash-grid`/bento collapses to one column so ≤2 in viewport).
- **No `.glass--lensed`** on CS/Logistics/Instructor dashboards (the lensed budget is spent on the Admin metric + focal only; these roles' tiles are plain `.glass--tile`).
- `.hub-mini` and `.stat-list` are opaque text/links. Instructor strip uses `.form-panel--instructor` scale (reused verbatim from Slice 3).
- Dashboards do **not** poll (they're landings, not the hub; the hub is one click away and polls). Flagged: if a live tickets badge on the dashboard is wanted later, the `.hub-mini` could gain the same `hx-trigger="every 60s"` — **not built here** to keep these surfaces cheap.

---

## 4. SURFACE 4 — Logistics master packing list (`/Logistics/PackingList`)

The consolidated print-friendly list for a packing run: orders **grouped** (default by **model**, toggle by **class**) so the packer assembles all of one model at once, with total quantities. New route, role `LogisticsOrAdmin`, dense scale, print-affordance.

### 4.1 ASCII wireframe

```
┌ dash-shell · topbar: לוגיסטיקה · רשימת אריזה ───────────────────────────────────────┐
  dash-body
  ┌ subnav ─ [רשימת אריזה] is-active ─────────────────────────────────────────────────┐

  ┌ page-head ─ רשימת אריזה                          [קיבוץ: דגם|כיתה]  [🖨 הדפסה] ─────┐
  │  כל ההזמנות הממתינות לאריזה, מקובצות לאיסוף                                          │

  ┌ glass glass--tile people-panel  (print: panel→plain, no glass) ───────────────────┐
  │  ┌ pack-group ─ דגם "קופסת אוצרות" ─────────────────  סה״כ: 48 ערכות ──────────┐  │
  │  │  כיתה   כמות                                                                  │  │
  │  │  ג׳2     24                                                                   │  │
  │  │  ג׳1     24                                                                   │  │
  │  └────────────────────────────────────────────────────────────────────────────┘  │
  │  ┌ pack-group ─ דגם "מסגרת פסיפס" ──────────────────  סה״כ: 18 ערכות ──────────┐  │
  │  │  כיתה   כמות                                                                  │  │
  │  │  ד׳1     18                                                                   │  │
  │  └────────────────────────────────────────────────────────────────────────────┘  │
  └─────────────────────────────────────────────────────────────────────────────────────┘
  (empty) אין הזמנות ממתינות לאריזה · כל ההזמנות נארזו או טרם נוצרו.
```

### 4.2 Markup skeleton

```html
<main class="dash-body">
  <nav class="subnav" aria-label="ניווט לוגיסטיקה"> … [רשימת אריזה] is-active … </nav>

  <div class="page-head">
    <div>
      <h1 class="page-head__title">רשימת אריזה</h1>
      <p class="page-head__sub">כל ההזמנות הממתינות לאריזה, מקובצות לאיסוף</p>
    </div>
    <div style="display:flex;gap:var(--sp-3);align-items:center;">
      <form method="get" class="segment" role="radiogroup" aria-label="קיבוץ לפי">
        <label class="segment__opt"><input type="radio" name="groupBy" value="model" checked
                onchange="this.form.submit()"><span>דגם</span></label>
        <label class="segment__opt"><input type="radio" name="groupBy" value="class"
                onchange="this.form.submit()"><span>כיתה</span></label>
      </form>
      <button type="button" class="btn-primary" onclick="window.print()">
        <span aria-hidden="true">🖨</span> הדפסה
      </button>
    </div>
  </div>

  <div class="glass glass--tile people-panel pack-sheet" style="--lg-tile-shadow: var(--lg-shadow);">
    @if (!Model.Groups.Any())
    {
      <div class="empty-state">
        <span class="empty-state__icon" aria-hidden="true">📦</span>
        <p class="empty-state__title">אין הזמנות ממתינות לאריזה</p>
        <p class="empty-state__hint">כל ההזמנות נארזו או טרם נוצרו.</p>
      </div>
    }
    else
    {
      @foreach (var g in Model.Groups)
      {
        <section class="pack-group">
          <header class="pack-group__head">
            <h2 class="pack-group__title">
              @(Model.GroupBy == "model" ? "דגם" : "כיתה") "@g.Name"
            </h2>
            <span class="pack-group__total">סה״כ: <bdi class="num">@g.TotalQuantity</bdi> ערכות</span>
          </header>
          <table class="data-table pack-table">
            <thead><tr><th>@(Model.GroupBy == "model" ? "כיתה" : "דגם")</th><th>כמות</th></tr></thead>
            <tbody>
              @foreach (var line in g.Lines)
              {
                <tr class="data-row">
                  <td class="data-cell data-cell--primary">@line.Label</td>
                  <td class="data-cell"><bdi class="num">@line.Quantity</bdi></td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      }
    }
  </div>
</main>
```

### 4.3 Exact Hebrew copy

| Element | Hebrew |
|---|---|
| Topbar / page title | `רשימת אריזה` |
| Page sub | `כל ההזמנות הממתינות לאריזה, מקובצות לאיסוף` |
| Group-by segment | `קיבוץ:` (aria) · options `דגם` / `כיתה` |
| Print button | `הדפסה` |
| Group title | `דגם "{name}"` (or `כיתה "{name}"` when grouped by class) |
| Group total | `סה״כ: <bdi class="num">{N}</bdi> ערכות` |
| Columns | `כיתה` · `כמות` (or `דגם` · `כמות` when grouped by class) |
| Empty | title `אין הזמנות ממתינות לאריזה` · hint `כל ההזמנות נארזו או טרם נוצרו.` |

### 4.4 Interaction / print / motion

- **Group-by** is a plain GET segment (`?groupBy=model|class`, full nav, server re-groups) — no HTMX.
- **Print** = `window.print()`. The **NEW `@media print`** block (in §6) strips the glass/blur/shadows, the topbar, the subnav, the page-head controls, and the mesh, leaving a clean black-on-white sheet: panel → plain border, `data-table` rows visible, groups page-break-avoided. **This is the one place a print stylesheet is justified** — a packer prints and walks the warehouse.
- **No motion** beyond the global page settle; print is the affordance, not animation.
- **Perf:** the sheet is one `.glass--tile` + the topbar (2 blurs on screen); on print, zero blur. Tables/groups opaque.

> **Server scope (flagged):** the grouping query (sum `quantity` over `Logistics_Order WHERE status = Pending` grouped by `model_id` or `class_id`, joined to `Class.Name`/`Model.Name`) is a server concern. Default status filter = `Pending` ("ממתינות לאריזה"); whether to include `Packed` is a business call — flagged, default Pending-only. (Domain note: `LogisticsOrder.DeliveredAt` is a `DateTime?` set on **Accepted**, not Packed — irrelevant to a Pending packing run, but the packing list deliberately shows no delivered date.)

---

## 5. SURFACE 5 — Carryover polish

### 5.1 (a) Shared page-shell partial `_PageShell.cshtml`

**Problem (verified):** the topbar (`dash-topbar` + `__wordmark` "עורקבי" + `__title` + `__user` with `שלום, {name}` + logout) is repeated **verbatim in 36 pages**, and the `subnav` in 31. `_Layout.cshtml` centralizes only document chrome. This is the single biggest DRY win in the app.

**Proposal:** one partial that renders the topbar + (optional) subnav, parameterized. **Visual output is byte-identical to today** — this is pure extraction, no restyle.

**View-model (implementer creates; shown for the contract):**
```csharp
public enum NavSection { None, People, Curriculum, Scheduling, Operations, Logistics, Dashboard }
public sealed class PageShellVm
{
    public NavSection Section { get; init; } = NavSection.None;
    public string Title { get; init; } = "";        // dash-topbar__title text
    public string? ActiveKey { get; init; }          // href of the active subnav item
    public string Greeting { get; init; } = "";      // "מנהל" / instructor name — shown as שלום, {Greeting}
    public bool ShowSubnav { get; init; } = true;
    public IReadOnlyList<(string Label, string Href)>? SubnavOverride { get; init; } // role-aware Operations/Logistics
}
```

**Partial skeleton:**
```html
@model PageShellVm
<header class="glass glass--nav dash-topbar">
  <a href="/" class="dash-topbar__wordmark">עורקבי</a>
  <span class="dash-topbar__title">@Model.Title</span>
  <nav class="dash-topbar__user">
    @if (!string.IsNullOrEmpty(Model.Greeting)) { <span>שלום, @Model.Greeting</span> }
    <a href="/Account/Logout" class="btn-ghost" style="font-size:var(--t-admin-meta);">יציאה</a>
  </nav>
</header>

@if (Model.ShowSubnav && Model.Section != NavSection.None)
{
  var items = Model.SubnavOverride ?? SubnavFor(Model.Section); // static map of the §0.5 table
  <nav class="subnav" aria-label="ניווט @SectionAria(Model.Section)">
    @foreach (var (label, href) in items)
    {
      <a class="subnav__item @(href == Model.ActiveKey ? "is-active" : "")" href="@href">@label</a>
    }
  </nav>
}
```

> The subnav lists per section come from the **§0.5 verified table** (a static `SubnavFor(section)` helper). The two **role-aware** sections (Operations, Logistics) pass `SubnavOverride` (the caller already knows the user's role) — matching today's role-gated subnav rendering. The greeting source stays per-page (`@Model.Greeting` / `"מנהל"` / `User.Identity?.Name?.Split('@')[0]`) — the shell just renders `שלום, {Greeting}`.
>
> **The subnav sits inside `<main class="dash-body">` today** (it's the first child after the topbar in the page body), so the partial renders the topbar; the page still opens `<main class="dash-body">` and drops `<partial name="_Subnav"…/>` OR the shell renders both and the page renders only its content. **Decision: keep it simple — `_PageShell` renders the topbar only inside `dash-shell`'s header slot; a sibling `_Subnav` partial (or the same partial's subnav branch) renders inside `dash-body`.** Either is fine; the constraint is visual identity. Flagged as an implementer ergonomics choice, not a design one.

**NO new CSS** — the shell reuses `dash-topbar`/`subnav` exactly. The only "new" artifacts are the `.cshtml` partial + the VM + the static subnav map (all code, not CSS).

**Migration:** convert pages incrementally (each page swaps its inline topbar/subnav for the partial). Because output is identical, this is safe page-by-page; no big-bang. Flagged: the **36-page migration is mechanical server work**, not design — I designed the partial contract, not the 36 edits.

### 5.2 (b) Save-success toast component

**Today:** `.toast` exists and is rendered as a bare static `<div class="toast" role="status">…</div>` from `TempData["SuccessMessage"]` in ExtraHours/Incidents/Vacations — it appears and never leaves, no motion, no icon. People/Curriculum/Scheduling CRUD give no feedback. **Goal:** one toast that all CRUD reuses, with §8 motion (enter/exit), auto-dismiss, a success glyph, and a manual dismiss, RTL-correct.

**Decision: keep the `.toast` base (position/material) and ADD** an icon slot, a `--success` accent, entrance/exit keyframes, auto-dismiss, and a tiny progressive-enhancement script. The existing `.toast__action` (undo button) stays for the Slice-1 un-enroll case. This is an **enhancement of an existing class**, not a new component name — minimizing churn.

**Markup (the canonical success toast):**
```html
@* render when TempData["SuccessMessage"] is set — house pattern, now styled *@
@if (TempData["SuccessMessage"] is string ok)
{
  <div class="toast toast--success" role="status" data-toast-autodismiss="3200">
    <span class="toast__icon" aria-hidden="true">✓</span>
    <span class="toast__msg">@ok</span>
    <button type="button" class="toast__close" aria-label="סגירה" data-toast-close>×</button>
  </div>
}
```

**Hebrew copy (the success strings — extend the established `נשלח`/`נשמר` family):**

| CRUD action | Toast Hebrew (`TempData["SuccessMessage"]`) |
|---|---|
| Create (generic) | `נוצר בהצלחה` |
| Update (generic) | `הפרטים נשמרו` |
| Delete / archive | `הפריט הוסר` / `הפריט הועבר לארכיון` |
| School create/update | `בית הספר נשמר` |
| Class create/update | `הכיתה נשמרה` |
| Client create/update | `הלקוח נשמר` |
| Model/Syllabus save | `הדגם נשמר` / `הסילבוס נשמר` |
| Template/Instance save | `התבנית נשמרה` / `המופע נשמר` |
| (existing, keep) | `דיווח השעות נשלח לאישור` · `בקשת החופשה נשלחה לאישור` · `הדיווח נשלח` |
| Close button aria | `סגירה` |

**Motion (§8 — a confirmation, so it animates):** rise + fade in on `--ease-spring`, hold, fade + drop out; auto-dismiss ~3.2s; both gated under reduced-motion (instant appear/disappear, still auto-dismisses). Stacking: if two fire, the second offsets up by its height (the PE script manages a stack). Position uses the existing logical `inset-block-end`/`margin-inline:auto` so it's centered and RTL-neutral.

**Progressive-enhancement script** (~15 lines, vanilla, gated; place in a small `toast.js` or inline): on DOM ready, for each `.toast[data-toast-autodismiss]`, start a timer to add `.toast--leaving` then remove; wire `[data-toast-close]` to dismiss immediately; respect `prefers-reduced-motion` (skip the leave animation, just remove). **Flagged:** the script is the only JS this slice adds; it's tiny and gated. If the team prefers zero JS, a pure-CSS auto-hide via a long `animation` that ends in `opacity:0;visibility:hidden` also works (I provide the CSS path; the JS path gives manual-dismiss + stacking, which CSS-only can't). **Default: the small JS** for dismiss + stacking.

---

## 6. NEW CSS — consolidated, paste-ready (append to `base.css`, before the `@supports not (...)` fallback)

> Verified against the live `tokens.css`/`base.css` this session: every token used below exists (§0.5); every class introduced below is **absent** today (checked: `.hub-poll`, `.hub-mini`, `.stat-list`/`.stat-row`, `.dash-grid`, `.pack-*`, `.toast--success`/`__icon`/`__msg`/`__close`, `hub-card-enter`/`hub-card-leave`, the `@media print` block — none exist). Classes I call "reuse" were all confirmed present. **No new tokens required.** If any token is missing at paste time, STOP and flag — do not invent.
> Footprint: **one polling indicator, one mini-list, one stat-list primitive, one simple dash-grid, the packing-list layout, the toast upgrade, two tiny gated keyframes, and one print block.** Everything else is reuse.

```css
/* ============================================================
   SLICE 5 — Action Hub + real dashboards (capstone)
   Reuses verbatim: .dash-shell/.dash-topbar/.dash-body, .subnav,
   .page-head, .bento(+areas)/.bento__tile/.tile-head, .metric-value/
   .metric-delta/.metric-sublabel, .feed-list/.feed-item/.feed-dot,
   .focal-tile, .action-card(+__top/__desc/__actions/__resolved-meta/
   .is-resolved), .action-type(+full enum), .status-chip(+--active/--muted),
   .empty-state, .nav-card/.hub-grid, .model-chip, .data-table, .segment,
   .status-pill-rail/.status-pill, .filter-select, .form-panel--instructor,
   .btn-primary/.btn-ghost(+--sm/--accent), .toast(+__action), .num.
   Perf: topbar is the only blur; tiles are static glass; lists/cards opaque;
   chips/mini-lists zero backdrop-filter. Lensed budget = Admin metric+focal
   ONLY (auto-dropped <768px). NO .glass--lensed on data lists. ============ */

/* ---- §1 Action-Hub polling indicator (page-head, steady dot + label) ---- */
.hub-poll {
  display: inline-flex; align-items: center; gap: var(--sp-2);
  padding-block: var(--sp-1); padding-inline: var(--sp-3);
  border-radius: var(--radius-pill);
  background: var(--lg-fill-tint); border: 1px solid rgba(var(--brand-rgb), .16);
  font-size: var(--t-admin-meta); font-weight: var(--fw-medium); color: var(--brand-ink-muted);
}
.hub-poll__dot {
  inline-size: 7px; block-size: 7px; border-radius: 50%;
  background: var(--ok); flex: 0 0 auto;
  box-shadow: 0 0 0 0 rgba(46,125,91,.40);
}
/* Brighten the dot only while a poll/resolve request is in flight (reuse htmx-request).
   Not a per-30s pulse — the list element carries .htmx-request during the swap. */
#hub-list.htmx-request ~ * .hub-poll__dot,           /* if indicator follows in DOM */
.hub-poll.is-polling .hub-poll__dot { background: var(--brand-tint); }
@media (prefers-reduced-motion: no-preference) {
  @keyframes hub-poll-ping { 0% { box-shadow: 0 0 0 0 rgba(46,125,91,.40); }
                             100% { box-shadow: 0 0 0 6px rgba(46,125,91,0); } }
  /* one ping per swap, triggered by adding .is-polling in htmx:beforeRequest (optional) */
  .hub-poll.is-polling .hub-poll__dot { animation: hub-poll-ping var(--dur-slow) var(--ease-out-expo); }
}

/* ---- §1 Action-Hub list entrance/leave (confirmations only, gated) ---- */
@media (prefers-reduced-motion: no-preference) {
  /* whole-list settle cross-fade after a poll/resolve swap (preferred; mirrors
     #syllabus-models.htmx-settling). Zero per-row work. */
  #hub-list.htmx-settling { animation: lg-fade-up var(--dur-fast) var(--ease-glass) both; }
  /* optional per-card entrance for a freshly-arrived item (server stamps [data-new]) */
  .action-card[data-new] { animation: hub-card-enter var(--dur-mid) var(--ease-spring) both; }
  @keyframes hub-card-enter { from { opacity: 0; transform: translateY(8px) scale(.99); }
                              to   { opacity: 1; transform: translateY(0)   scale(1);  } }
  /* card leaving (resolved under open-only filter) — short collapse, not a pop */
  .action-card.is-leaving { animation: hub-card-leave var(--dur-fast) var(--ease-glass) both; }
  @keyframes hub-card-leave { from { opacity: 1; transform: none; }
                              to   { opacity: 0; transform: translateY(-4px) scale(.98); } }
}

/* ---- §2/§3 Action-Hub mini-list (dashboard tile preview; opaque, zero blur) ---- */
.hub-mini { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--sp-2); }
.hub-mini__item {
  display: flex; align-items: center; gap: var(--sp-3);
  padding-block: var(--sp-2); padding-inline: var(--sp-3);
  border-radius: var(--radius-chip);
  background: rgba(var(--brand-rgb), .04);
  border: 1px solid rgba(var(--brand-rgb), .08);
}
.hub-mini__desc {
  flex: 1; min-inline-size: 0;
  font-size: var(--t-admin-body); color: var(--brand-ink); text-decoration: none;
  line-height: var(--lh-snug);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;  /* one line in a tile */
}
.hub-mini__desc:hover { color: var(--brand); }
.hub-mini__more {
  display: inline-block; margin-block-start: var(--sp-3);
  font-size: var(--t-admin-meta); font-weight: var(--fw-semibold);
  color: var(--brand); text-decoration: none;
}
.hub-mini__more:hover { text-decoration: underline; }
/* instructor scale lift inside .form-panel--instructor (reused modifier) */
.form-panel--instructor .hub-mini__desc { font-size: var(--t-body); }

/* ---- §2/§3 Stat-list primitive (label ↔ value-link rows in dashboard tiles).
        Replaces the Admin mock's inline-styled flex divs. Opaque, zero blur. ---- */
.stat-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--sp-3); }
.stat-row { display: flex; align-items: center; justify-content: space-between; gap: var(--sp-3); }
.stat-row__label { font-size: var(--t-admin-body); color: var(--brand-ink); }
.stat-row__value {
  font-family: var(--font-num); font-size: var(--t-admin-title); font-weight: var(--fw-bold);
  color: var(--brand); text-decoration: none;
  font-feature-settings: "tnum" 1; font-variant-numeric: tabular-nums;
}
.stat-row__value:hover { color: var(--brand-tint); text-decoration: underline; }
.stat-row--alert .stat-row__value { color: var(--absent); }
.stat-row--alert .stat-row__value:hover { color: var(--absent); }

/* ---- §3 Simple role-dashboard grid (CS/Logistics — lighter than the Admin bento).
        Auto-fit tiles; collapses naturally; NOT the asymmetric bento. ---- */
.dash-grid {
  display: grid; gap: var(--bento-gap);
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  align-items: start;
}
.dash-grid__wide { grid-column: 1 / -1; }   /* a tile that should span the row */

/* ---- §4 Packing list — grouped print-friendly sheet ---- */
.pack-group { margin-block-end: var(--sp-6); }
.pack-group:last-child { margin-block-end: 0; }
.pack-group__head {
  display: flex; align-items: baseline; justify-content: space-between; gap: var(--sp-3);
  padding-block-end: var(--sp-2); margin-block-end: var(--sp-3);
  border-block-end: 1px solid rgba(var(--brand-rgb), .14);
}
.pack-group__title { margin: 0; font-size: var(--t-admin-title); font-weight: var(--fw-bold); color: var(--brand-ink); }
.pack-group__total {
  font-size: var(--t-admin-label); font-weight: var(--fw-semibold); color: var(--brand);
  white-space: nowrap;
}
.pack-table { max-inline-size: 480px; }   /* a packing table is narrow (label + qty) */

/* ---- §5b Save-success toast — upgrades the existing .toast (kept) with
        icon + accent + motion + auto-dismiss + manual close. RTL-neutral. ---- */
.toast--success { border-inline-start: 3px solid var(--ok); }
.toast__icon {
  flex: 0 0 auto; inline-size: 22px; block-size: 22px; border-radius: 50%;
  display: inline-flex; align-items: center; justify-content: center;
  background: var(--ok-soft); color: var(--ok);
  font-weight: var(--fw-bold); font-size: 13px; line-height: 1;
}
.toast__msg { flex: 1; min-inline-size: 0; }
.toast__close {
  flex: 0 0 auto; background: none; border: 0; cursor: pointer;
  color: var(--brand-ink-faint); font-size: 20px; line-height: 1;
  padding: 0 var(--sp-1); transition: color var(--dur-fast) var(--ease-glass);
}
.toast__close:hover { color: var(--brand-ink-muted); }
@media (prefers-reduced-motion: no-preference) {
  @keyframes toast-in  { from { opacity: 0; transform: translateY(12px) scale(.98); }
                         to   { opacity: 1; transform: translateY(0)    scale(1);  } }
  @keyframes toast-out { from { opacity: 1; transform: translateY(0); }
                         to   { opacity: 0; transform: translateY(8px); } }
  .toast { animation: toast-in var(--dur-mid) var(--ease-spring) both; }
  .toast.toast--leaving { animation: toast-out var(--dur-fast) var(--ease-glass) both; }
}

/* ---- §4 Print stylesheet — the packing list prints clean (the one justified
        print block in the app). Strips glass/blur/chrome → black-on-white. ---- */
@media print {
  body { background: #fff !important; }
  body::before { display: none !important; }                 /* kill the photo/mesh scrim */
  .dash-topbar, .subnav, .page-head .segment, .page-head .btn-primary,
  .hub-poll, .htmx-indicator { display: none !important; }   /* chrome + controls off */
  .glass, .glass--tile, .pack-sheet {
    background: #fff !important; box-shadow: none !important; border: 0 !important;
    -webkit-backdrop-filter: none !important; backdrop-filter: none !important;
  }
  .glass::before, .glass__lens, .glass--lensed::after { display: none !important; }
  .page-head__title { color: #000 !important; }
  .pack-group { break-inside: avoid; page-break-inside: avoid; }
  .pack-group__head { border-block-end: 1px solid #000 !important; }
  .pack-group__title, .pack-group__total { color: #000 !important; }
  .data-table thead th { background: #fff !important; color: #000 !important; border-block-end: 1px solid #000 !important; }
  .data-cell { color: #000 !important; }
  .data-row + .data-row .data-cell { border-block-start: 1px solid #ccc !important; }
}
```

**Add into the existing `@supports not (...)` fallback block** (these new surfaces carry no required `backdrop-filter` — mini-lists, stat-lists, dash-grid, pack groups, the toast are all opaque/tinted; the toast already uses `--lg-fill-solid`). **Nothing new is required** — the `.glass`/`.glass--tile`/`.glass--nav` line already covers the dashboard/hub/packing panels. Flagged for the implementer to confirm rather than blindly append.

---

## 7. Legibility & perf self-check (contract §5 / budget §10)

- **Blur budget:** Action Hub = topbar (1) + one `.glass--tile` panel (2); cards/lists/chips opaque, zero blur. Admin bento = topbar (1) + tiles, but only **2 lensed** (metric+focal, desktop; **0** on mobile — `.glass__lens` filter auto-dropped <768px). CS/Logistics `dash-grid` + Instructor: topbar + tiles, **no lensing**, collapse to 1 col on mobile (≤2 blurs in viewport). Packing list = topbar + sheet (2); **0 on print.** ✔ §10.1/10.2/10.3
- **Polling does not thrash layout:** swaps the **list container only** (`outerHTML` of `#hub-list`), never the panel/topbar; `hx-sync="this:replace"` prevents double-swap; whole-list settle fade (preferred) over per-row entrance. ✔ §10
- **Data/metrics on safe fill:** every metric/stat/mini-list/card sits on `--lg-fill-strong` (.80) tiles/panels; the hub list scrolls **opaque** inside its glass panel. **No `.glass--lensed` on any data list; no `.glass--clear` under text.** ✔ §5.1/§10
- **Lensed tiles hold no displaced text:** the metric/focal tiles keep the `.glass__lens` background-only layer (text at z-index:2, immune) — the established pattern, unchanged. ✔ §2.3
- **Not color-alone:** action-type carries label + dot; status chips carry label + glyph; severity (carried over) label + dot; the polling dot pairs with the `מתעדכן אוטומטית` label; toast carries `✓` icon + text. ✔ accessibility
- **RTL:** logical properties only; counts/dates/quantities/totals in `<bdi class="num">`; Hebrew months; toast/poll/grid all logical-prop positioned (no physical L/R); `<bdi>` never inside `<option>` (the type/group-by `<option>`s use plain Hebrew, numerals only in the rendered cells). ✔ §7
- **Motion = confirmations only:** resolve swap + card leave/enter (gated), toast in/out (gated), poll dot ping once-per-swap (gated); bento's existing gated load stagger kept; **no** per-poll animation, **no** dashboard entrance churn, **no** metric count-up. Reduced-motion inherited everywhere. ✔ §8
- **Reuse vs invention:** new CSS = `.hub-poll`(+dot), `.hub-mini`(+item/desc/more), `.stat-list`/`.stat-row`, `.dash-grid`, `.pack-group`(+head/title/total)/`.pack-table`, `.toast--success`(+icon/msg/close) upgrade, 3 tiny gated keyframes, 1 print block. Everything else — bento, feed, action-card, action-type, status-chip, data-table, segment, status-pill rail, model-chip, nav-card, empty-state, form-panel--instructor, btn-*, the `.toast` base — **reused verbatim.** ✔ restraint

---

## 8. HTMX fragment map (consolidated)

| Surface | Trigger | `hx-*` | Fragment |
|---|---|---|---|
| Action Hub — poll | every 30s | `hx-get="?handler=OpenList&status=&type="` `hx-trigger="every 30s"` `hx-target/swap=#hub-list outerHTML` `hx-sync="this:replace"` | `_ActionHubList.cshtml` |
| Action Hub — resolve | `סמן כטופל` | `hx-post="?handler=Resolve&id={id}"` `hx-target="#action-{id}"` `hx-swap="outerHTML"` | `_ActionCard.cshtml` (resolved) **or** empty (open-only filter) |
| Action Hub — filter | status pill / type select | plain GET `?status=&type=` | full page (no HTMX) |
| Packing list — group-by | segment radio | plain GET `?groupBy=model\|class` | full page (no HTMX) |
| Dashboards | — | none (landings don't poll; flagged optional `every 60s` on `.hub-mini` later) | — |
| Toast | — | not HTMX (server `TempData` → rendered toast + PE auto-dismiss JS) | — |

> Anti-forgery: the global `htmx:configRequest` CSRF header on `<body>` (in `_Layout`) already covers the resolve POST and the polling GET — **no per-fragment token wiring.** (Note: some existing pages additionally use `hx-include="#csrf-anchor"` against a hidden antiforgery form; the resolve button may match whichever convention its page already uses — both deliver the token. The polling **GET** needs no token regardless.) Partials are `_X.cshtml`, each a single swappable node with a stable id (`hub-list`, `action-{id}`), matching `_SubRow`/`_OrderRow`/`_OrderCard` precedent.

---

## 9. Deferred / out-of-scope (designer-flagged, per global rule §5)

These are explicit, not silent:

- **Server methods the UI assumes (NOT designed here, flagged §1.7):** `ListOpenForUserOrRoleAsync(userId, role, status?, type?)`, `ResolveAsync(id, user)` (must null `DeduplicationKey` per the existing invariant), the `OnGetOpenList`/`OnPostResolve` handlers, and the dashboard metric queries (active clients, pending approvals, today's shifts, order statuses, hub-preview top-N). I designed the surfaces *against* these; the queries themselves are server work.
- **`ResolvedByUserId`/`ResolvedAt` schema fields are OPTIONAL.** Without them the resolve meta is plain `✓ טופל`; with them it can read `✓ טופל · ע״י {שם}`. The design degrades cleanly — the team decides if the two fields are worth adding. Not built here.
- **`<bdi>`-wrapping numerals inside server-generated descriptions** (gap/absence strings currently emit bare digits) — a cosmetic server tweak; flagged, not a UI change.
- **SignalR / true real-time** — the hub is **polling-first** per spec §3/§12 (deliberately phased, not skipped). 30s `hx-trigger="every 30s"` is the baseline; SignalR is the later upgrade with no UI change (the same `#hub-list` would swap on push instead of poll).
- **Per-card "just arrived" entrance (`[data-new]`)** is offered but **defaulted OFF** in favor of the cheaper whole-list settle fade. Turning it on needs the server to stamp `data-new` on items newer than a client cursor — flagged optional.
- **Dashboard live-refresh** — CS/Logistics/Instructor dashboards do **not** poll (cheap landings); a later `every 60s` on `.hub-mini` is a one-attribute add, flagged not built.
- **The `_PageShell` 36-page migration** — I designed the partial + VM + subnav-map contract; the mechanical conversion of 36 pages is server/refactor work, done incrementally (output is identical, so it's safe page-by-page). Not performed here.
- **Toast auto-dismiss JS** — ~15 lines of gated vanilla JS (auto-dismiss + manual close + stacking). It is the only JS this slice adds; a pure-CSS auto-hide fallback exists but loses manual-dismiss/stacking. Default = the small JS; flagged.
- **Packing-list status scope** — defaults to `Pending`-only ("ממתינות לאריזה"); whether to include `Packed` is a business rule, flagged. The grouping query (sum by model/class) is server work.
- **Admin "Generate New Task" / manual task creation** (the §9.3 bento mentions a "Generate New Task" affordance) — the Action Hub shows/resolves auto-generated tickets; a **manual** task-create form is **not** designed this slice (no `Task`-creation UI exists and the brief centers on the auto-pipeline + resolve). If manual task creation is wanted, it's a small `.form-panel` (reusing `.form-field`/`.btn-primary`) writing an `AssignedToUser`/`Role` `Task` item — flag to add it.
- **Metric count-up animation** — allowed by §8 but kept instant on these often-opened admin surfaces; a JS flourish if wanted later, not built.

**Deferred Items (summary checklist):**
1. All server query/handler methods for the hub + dashboards (§1.7) — flagged, not built (server scope).
2. Optional `ResolvedByUserId`/`ResolvedAt` schema fields (resolve-by-name meta) — flagged optional.
3. `<bdi>`-wrap numerals in server descriptions — flagged cosmetic server tweak.
4. SignalR real-time — deliberately phased (polling-first); no UI change later.
5. Per-card `[data-new]` entrance — offered, defaulted OFF (cheaper list settle used).
6. Dashboard polling — not built (landings stay cheap); one-attribute add later.
7. `_PageShell` 36-page mechanical migration — contract designed, conversion is incremental server work.
8. Toast auto-dismiss JS (~15 lines, gated) — the only JS added; default on, flagged.
9. Packing-list `Packed`-inclusion + grouping query — server/business scope.
10. Manual Task-creation UI — not designed (auto-pipeline + resolve is the brief); flagged to add if wanted.
11. Metric count-up — kept instant; optional later.

**Everything else in the brief is specified.**
```