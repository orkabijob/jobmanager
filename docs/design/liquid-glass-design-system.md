# Orkabi — Liquid Glass Design System (Canonical)

> **Status:** Source of truth for all Orkabi visual design. Supersedes the placeholder tokens in
> `docs/superpowers/plans/2026-06-23-orkabi-slice-0-walking-skeleton.md` Task 10, Step 2–3.
> **Owner:** Liquid Glass designer. Every design decision routes through this file.
> **Scope:** Plain CSS for an ASP.NET Core 8 Razor Pages app. No WebGL, no heavy runtime.
> Progressive-enhancement vanilla JS only.

---

## 0. Provenance & honesty note

Apple's two `technologyoverviews` pages (`/liquid-glass`, `/adopting-liquid-glass`) are
client-rendered SPAs and return only their `<title>` to any server-side fetcher, so I could not
quote them directly. This document is grounded in: the WWDC25 "Meet Liquid Glass" framing, Apple's
updated **Human Interface Guidelines → Materials** section, and the community
**LiquidGlassReference** (conorluddy) which distills the SwiftUI `glassEffect` API. Where a claim
about Apple's *native* behavior is load-bearing it is marked **[Apple]**; where a value is **our
CSS approximation** it is marked **[Orkabi-CSS]**. I do not present invented Apple numbers as
quoted fact — the few hard numbers Apple-side that are corroborated (specular highlight amplitude
≤ ~6px, motion-reactive, disabled in battery-saver) are marked **[Apple, corroborated]**.

The single most important truth for this project: **browser CSS cannot do real-time lensing.**
Apple's material *refracts* the live backdrop (bends and concentrates light through a simulated
curved surface). `backdrop-filter: blur()` *scatters* light — it is diffusion, not refraction. We
get genuinely close with a layered recipe (blur + saturation + brightness + an SVG displacement
edge + specular gradients + concentric geometry), but we must spend the realism budget on the 2–3
surfaces that matter and stay flat-but-clean everywhere else. Restraint is the strategy, not a
compromise.

---

## 1. What makes Liquid Glass distinct from ordinary glassmorphism

Ordinary glassmorphism = one translucent fill + one `backdrop-filter: blur()` + a 1px white
border. It looks the same regardless of what's behind it, in any lighting, at rest or in motion.
Liquid Glass is a **material**, not a fill. Seven properties separate them:

1. **Lensing / refraction (not blur). [Apple]** The edges of a Liquid Glass element bend the
   backdrop inward like the rim of a real lens; the center is clearer, the edge distorts and
   concentrates light. Ordinary blur is uniform and edge-blind. *CSS reach: partial* — we fake the
   edge bend with an SVG `feDisplacementMap` ring and an edge-weighted highlight; we cannot bend the
   live center.
2. **Specular highlights that react to motion. [Apple, corroborated]** A bright moving glint runs
   along the top/leading edge as the device tilts or content scrolls beneath; amplitude is small
   (≤ ~6px) and it's disabled under battery-saver / Reduced Motion. *CSS reach: partial* — a static
   specular gradient always; an optional pointer-driven glint via a CSS variable updated in JS.
3. **Dynamic adaptivity to the content behind. [Apple]** The material samples the brightness of
   what's underneath and shifts its own tint/contrast so foreground text stays legible over both
   light and dark backdrops. *CSS reach: weak* — `backdrop-filter` has no readback. We approximate
   with `brightness()`/`contrast()` in the filter stack and, critically, by **never letting body
   text sit on the most-translucent tier** (we raise the fill instead). This is the biggest gap and
   the reason the legibility contract (§5) is mandatory, not optional.
4. **Depth layering / floating. [Apple]** Glass elements float *above* content on a soft, colored,
   contact-plus-ambient shadow — they are clearly a separate plane, never inline with content.
   *CSS reach: full* — a multi-stop shadow stack reproduces this faithfully.
5. **Concentric geometry. [Apple]** Nested rounded elements share a common center of curvature:
   child radius = parent radius − padding, so corners stay visually parallel. Controls are pill /
   continuous-rounded, echoing device hardware curves. *CSS reach: full* — pure math, see §6.
6. **Light / dark adaptation. [Apple]** The same material reads as frosted-light over bright
   scenes and smoked-dark over dark scenes, with the hairline edge flipping from a light highlight
   to a darker rim. *CSS reach: full* — two token sets behind a `prefers-color-scheme` / theme attr.
7. **Materialization. [Apple]** Glass doesn't fade in; it "modulates light bending" as it appears
   (a reveal that grows blur + scale + opacity together). *CSS reach: good* — a keyframe that ramps
   `backdrop-filter` blur, `scale`, and `opacity` together, gated behind reduced-motion.

**Three native variants [Apple / LiquidGlassReference] — we mirror them as three CSS tiers:**

| Apple variant | Behavior | Orkabi CSS tier | Used for |
|---|---|---|---|
| **Regular** | Medium transparency, full adaptivity | `.glass` (panel) | cards, sheets, sidebars, main surfaces |
| **Clear** | High transparency, limited adaptivity, **needs a dimming layer** | `.glass--clear` (+ scrim) | nav/topbar over busy backdrops, chips |
| **Identity** | Effect disabled / opaque | `.glass--solid` fallback | reduced-transparency, the Blue-Jay hero |

---

## 2. The CSS recipe (production-ready, layered)

A single Liquid-Glass surface in Orkabi is built from **five stacked contributions**, applied to
one element via tokens (not five DOM nodes — we use pseudo-elements so the markup stays clean):

```
┌─ specular highlight (::before, edge-weighted light gradient)        ← top glint
├─ refraction edge    (SVG feDisplacementMap on a ::after ring) [opt] ← lens rim
├─ hairline edge      (border + inset highlight in box-shadow)        ← light edge
├─ material fill      (translucent rgba)                              ← the "glass"
└─ backdrop filter    (blur + saturate + brightness)                 ← what bends/scatters
        ↑ floats on the multi-layer shadow stack (contact + ambient + inset)
```

The Figma recipe from the Medium reference corroborates the same anatomy with concrete moves we
translate to CSS: a near-invisible white fill (**1% opacity**) doing the "glass" job, **6px**
background blur, a surface **texture/noise** pass, **two opposed inner shadows** (`+2,+2` and
`−2,−2`, blur 4, ~60%) for the bevel, and a **Plus-Lighter ~15%** top layer for edge brightening.
Our CSS maps: white-fill→`--lg-*-fill`, background blur→`--lg-blur-*`, inner-shadow bevel→the
`inset` legs of `--lg-shadow-*`, Plus-Lighter glint→the `::before` specular gradient with
`mix-blend-mode: plus-lighter`, noise→an optional fixed grain overlay (§2.4).

### 2.1 The base utility (this is the whole material in ~15 lines)

```css
.glass {
  position: relative;
  background: var(--lg-fill);
  -webkit-backdrop-filter: var(--lg-blur-panel);
          backdrop-filter: var(--lg-blur-panel);
  border: var(--lg-hairline);
  border-radius: var(--radius-card);
  box-shadow: var(--lg-shadow);
  isolation: isolate;            /* contain the plus-lighter blend to this surface */
}
/* Specular highlight — the moving glint lives here. Static by default; JS can animate --lg-glint-x. */
.glass::before {
  content: ""; position: absolute; inset: 0; border-radius: inherit;
  pointer-events: none; z-index: 1;
  background: radial-gradient(
     120% 80% at var(--lg-glint-x, 30%) 0%,
     rgba(255,255,255,.55), rgba(255,255,255,.06) 38%, transparent 62%);
  mix-blend-mode: plus-lighter;
  opacity: .9;
}
.glass > * { position: relative; z-index: 2; }  /* content above the glint */
```

That alone is a strong, honest glass. The SVG refraction edge (§2.3) is the optional upgrade for
the 2–3 hero surfaces only.

### 2.2 The backdrop-filter stack — blur + saturation + brightness

Saturation is what makes color *bleed through* the glass (the Apple "alive" quality); brightness
nudges the material toward light/dark adaptivity that `backdrop-filter` otherwise can't do.

```css
/* tokens — light env */
--lg-blur-nav:    saturate(180%) brightness(1.06) blur(20px);  /* fixed chrome */
--lg-blur-panel:  saturate(160%) brightness(1.03) blur(14px);  /* cards/sheets */
--lg-blur-inline: saturate(140%) blur(8px);                    /* chips/badges */
```

**Hard performance rule [Orkabi-CSS]:** never put `brightness()` in the inline tier (cheapest
path), and never raise blur above 24px — past ~24px the GPU cost climbs with no perceptual gain.

### 2.3 The refraction edge — SVG `feDisplacementMap` (hero surfaces only)

This is the one technique that pushes past "frosted blur" toward "lens." A low-frequency turbulence
drives a displacement map that warps the backdrop **only at the rim**, reading as the glass bending
light inward. It is the single most expensive effect here, so it is opt-in (`.glass--lensed`) and
reserved for the attendance sheet header and the Admin hero metric tile — **never** on lists.

```html
<!-- once, in _Layout, visually hidden -->
<svg width="0" height="0" aria-hidden="true" focusable="false">
  <filter id="lg-refraction" x="-20%" y="-20%" width="140%" height="140%"
          color-interpolation-filters="sRGB">
    <feTurbulence type="fractalNoise" baseFrequency="0.012 0.016"
                  numOctaves="2" seed="7" result="noise"/>
    <feGaussianBlur in="noise" stdDeviation="2" result="softNoise"/>
    <feDisplacementMap in="SourceGraphic" in2="softNoise"
                       scale="14" xChannelSelector="R" yChannelSelector="G"/>
  </filter>
</svg>
```

```css
.glass--lensed { filter: url(#lg-refraction); }      /* applied to the GLASS layer, not its text */
```

**Caveats [Orkabi-CSS]:**
- `feDisplacementMap` + `backdrop-filter` interaction is uneven across engines; on Safari prefer
  applying the displacement to a `::after` ring that overlays the rim rather than the whole element,
  so body text is never displaced.
- It is GPU-heavy on mobile. Guard it: only render on `(min-width: 768px)` **or** the single hero
  surface, and drop it entirely under Reduced Motion / Reduced Transparency.
- `scale` is the refraction strength. 10–16 reads as glass; >20 looks like a melting bug.

### 2.4 Optional surface grain (kills the "sterile CSS" tell)

```css
/* fixed, pointer-events:none, ONE instance for the whole app — never per-card (perf) */
.lg-grain::after {
  content:""; position:fixed; inset:0; z-index:0; pointer-events:none; opacity:.025;
  background-image: url("data:image/svg+xml,...feTurbulence noise tile...");
  mix-blend-mode: overlay;
}
```

### 2.5 Where CSS **can** and **cannot** match Liquid Glass — and the fallback

| Property | CSS reach | Fallback when unsupported |
|---|---|---|
| Frosted translucency | **Full** (`backdrop-filter`) | solid `--lg-fill-solid` via `@supports not` |
| Color bleed / vibrancy | **Full** (`saturate()`) | none needed |
| Floating depth shadow | **Full** | none needed |
| Concentric geometry | **Full** (math) | none needed |
| Light/dark adaptation | **Full** (token sets) | none needed |
| Edge lensing/refraction | **Partial** (SVG rim) | drop `.glass--lensed`; plain `.glass` |
| Motion-reactive glint | **Partial** (JS var) | static `::before` gradient |
| Adapt to backdrop brightness | **Weak** (no readback) | **legibility contract §5** (raise fill) |
| "Materialize" reveal | **Good** (keyframes) | instant under reduced-motion |

**Universal fallback** (Firefox historically gated `backdrop-filter`, and any GPU-blocked client):

```css
@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .glass, .glass--clear, .glass--nav { background: var(--lg-fill-solid); }
}
```

---

## 3. `tokens.css` — refined, canonical (supersedes Task 10 Step 2)

> Drop-in replacement for the plan's placeholder `:root`. Logical-property-friendly, Blue Jay
> locked, Assistant + Heebo locked. Light is default; dark + accessibility overrides included.

```css
/* ============================================================
   Orkabi tokens.css — Liquid Glass system (canonical)
   Brand: Blue Jay #2B547E · UI: Assistant · Numerals: Heebo
   ============================================================ */
:root {
  /* ---- Brand ---- */
  --brand:            #2B547E;
  --brand-rgb:        43, 84, 126;
  --brand-tint:       #3E6FA0;   /* hovers, mesh, lighter accents */
  --brand-deep:       #1E3D5C;   /* pressed, hero gradient floor */
  --brand-ink:        #16263A;   /* default text on light */

  /* ---- Liquid Glass: backdrop-filter stacks (3 tiers = Apple Regular/Clear/inline) ---- */
  --lg-blur-nav:      saturate(180%) brightness(1.06) blur(20px);
  --lg-blur-panel:    saturate(160%) brightness(1.03) blur(14px);
  --lg-blur-inline:   saturate(140%) blur(8px);

  /* ---- Material fills (translucent). NEVER put body text on --lg-fill; use --lg-fill-strong. ---- */
  --lg-fill:          rgba(255, 255, 255, .55);   /* Regular */
  --lg-fill-strong:   rgba(255, 255, 255, .72);   /* under dense text (legibility contract) */
  --lg-fill-clear:    rgba(255, 255, 255, .38);   /* Clear — REQUIRES a scrim behind text */
  --lg-fill-tint:     rgba(var(--brand-rgb), .06);/* brand-tinted glass (lesson-model chip) */
  --lg-fill-solid:    rgba(255, 255, 255, .92);   /* Identity / no-backdrop-filter fallback */

  /* ---- Hairline edges (the "light rim"). Two-part: visible border + inset highlight in shadow. */
  --lg-hairline:      1px solid rgba(255, 255, 255, .55);
  --lg-hairline-rgb:  255, 255, 255;

  /* ---- Shadow stacks: contact + ambient + inset bevel (the Figma double-inner-shadow, in CSS) */
  --lg-shadow:
     0 1px 1px  rgba(var(--brand-rgb), .04),
     0 8px 24px -8px rgba(var(--brand-rgb), .18),
     inset 0 1px 0 rgba(255,255,255,.55),
     inset 0 -1px 0 rgba(var(--brand-rgb),.05);
  --lg-shadow-lifted:
     0 2px 4px  rgba(var(--brand-rgb), .06),
     0 16px 48px -12px rgba(var(--brand-rgb), .28),
     inset 0 1px 0 rgba(255,255,255,.65),
     inset 0 -1px 0 rgba(var(--brand-rgb),.06);
  --lg-shadow-hero:                 /* solid Blue-Jay monolith glow */
     0 1px 0   rgba(255,255,255,.18) inset,
     0 20px 60px -16px rgba(var(--brand-rgb), .50);

  /* ---- Specular glint default position (JS may animate on pointer/scroll) ---- */
  --lg-glint-x: 30%;

  /* ---- Radii (continuous-rounded, concentric-capable). Bigger than ShiftManager's flat 8px. */
  --radius-chip:   12px;
  --radius-card:   20px;
  --radius-panel:  28px;
  --radius-hero:   32px;
  --radius-sheet:  28px;   /* top corners of the attendance bottom-sheet */
  --radius-pill:   9999px;
  --pad-concentric: 8px;   /* default inset → child radius = parent − this (see §6) */

  /* ---- Mesh backdrop base (the glass needs something colorful to refract) ---- */
  --bg-base: #EEF3F8;      /* cool off-white */

  /* ---- Type ---- */
  --font-ui:  'Assistant', system-ui, -apple-system, 'Segoe UI', sans-serif;
  --font-num: 'Heebo', 'Assistant', ui-monospace, monospace; /* tabular numerals */
  --fw-regular: 400; --fw-medium: 500; --fw-semibold: 600; --fw-bold: 700;

  /* Instructor (mobile-first, base 17px) */
  --t-hero-cta: 28px; --t-display: 24px; --t-title: 20px;
  --t-body: 17px;     --t-label: 15px;   --t-meta: 13px;
  /* Admin (dense, base 15px) */
  --t-metric: 40px;   --t-admin-display: 22px; --t-admin-title: 17px;
  --t-admin-body: 15px; --t-admin-label: 13px; --t-admin-meta: 12px;
  --lh-tight: 1.15; --lh-snug: 1.3; --lh-normal: 1.5;

  /* ---- Spacing (block/inline neutral; logical props consume these) ---- */
  --sp-1: 4px; --sp-2: 8px; --sp-3: 12px; --sp-4: 16px;
  --sp-5: 20px; --sp-6: 24px; --sp-8: 32px; --sp-10: 40px; --sp-12: 48px;

  /* ---- Motion (springs; see §8) ---- */
  --ease-glass:  cubic-bezier(0.32, 0.72, 0, 1);     /* sheets, cards */
  --ease-spring: cubic-bezier(0.34, 1.56, 0.64, 1);  /* confirm overshoot */
  --dur-fast: 180ms; --dur-mid: 280ms; --dur-slow: 420ms;

  /* ---- Semantic (attendance, tasks) ---- */
  --ok:     #2E7D5B; --ok-soft:  rgba(46,125,91,.16);
  --absent: #B23A48; --absent-soft: rgba(178,58,72,.14);
  --warn:   #C8861B;
}

/* ============================================================
   Dark environment — same material, smoked instead of frosted
   ============================================================ */
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --brand-ink: #E8EEF5;
    --bg-base:   #0E1722;
    --lg-fill:        rgba(28, 42, 60, .55);
    --lg-fill-strong: rgba(28, 42, 60, .74);
    --lg-fill-clear:  rgba(28, 42, 60, .40);
    --lg-fill-solid:  rgba(22, 34, 50, .92);
    --lg-hairline:    1px solid rgba(255,255,255,.16);
    --lg-shadow:
       0 1px 1px rgba(0,0,0,.30),
       0 8px 24px -8px rgba(0,0,0,.55),
       inset 0 1px 0 rgba(255,255,255,.10);
    --lg-shadow-lifted:
       0 2px 4px rgba(0,0,0,.35),
       0 16px 48px -12px rgba(0,0,0,.65),
       inset 0 1px 0 rgba(255,255,255,.12);
  }
}

/* ============================================================
   Accessibility — honor system settings, like Apple does [Apple]
   ============================================================ */
@media (prefers-reduced-transparency: reduce) {
  :root {
    --lg-fill: var(--lg-fill-solid);
    --lg-fill-strong: var(--lg-fill-solid);
    --lg-fill-clear: var(--lg-fill-solid);
    --lg-blur-nav: none; --lg-blur-panel: none; --lg-blur-inline: none;
  }
  .glass--lensed { filter: none; }              /* drop refraction */
  .glass::before { display: none; }             /* drop glint */
}
@media (prefers-contrast: more) {
  :root {
    --lg-fill: var(--lg-fill-solid);
    --lg-fill-strong: var(--lg-fill-solid);
    --lg-hairline: 1px solid rgba(var(--brand-rgb), .55);
  }
}
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: .01ms !important; animation-iteration-count: 1 !important;
    transition-duration: .01ms !important; scroll-behavior: auto !important;
  }
  .glass--lensed { filter: none; }              /* no motion-reactive lens */
}
```

### 3.1 Mesh backdrop (supersedes Task 10's `body::before` — keep this exact block)

```css
body { background-color: var(--bg-base); color: var(--brand-ink); font-family: var(--font-ui); }
body::before {
  content: ""; position: fixed; inset: 0; z-index: -1; pointer-events: none;
  background:
    radial-gradient(42% 52% at 18% 20%, rgba(var(--brand-rgb), .26), transparent 70%),
    radial-gradient(46% 56% at 84% 16%, rgba(120,160,210,.22), transparent 70%),
    radial-gradient(52% 60% at 76% 88%, rgba(var(--brand-rgb), .20), transparent 75%),
    radial-gradient(40% 50% at 8% 92%, rgba(150,180,220,.16), transparent 70%);
}
```

Keep mesh in the **blue family** (Blue Jay tints + one cool lavender-grey). Rainbow orbs are the
generic-glassmorphism tell — at most one warm accent orb if a screen feels cold.

---

## 4. base.css essentials (component utilities, logical-property-native)

```css
@font-face{font-family:'Assistant';src:url('/fonts/assistant-400.woff2') format('woff2');font-weight:400;font-display:swap;}
@font-face{font-family:'Assistant';src:url('/fonts/assistant-500.woff2') format('woff2');font-weight:500;font-display:swap;}
@font-face{font-family:'Assistant';src:url('/fonts/assistant-600.woff2') format('woff2');font-weight:600;font-display:swap;}
@font-face{font-family:'Assistant';src:url('/fonts/assistant-700.woff2') format('woff2');font-weight:700;font-display:swap;}
@font-face{font-family:'Heebo';   src:url('/fonts/heebo-500.woff2') format('woff2');font-weight:500;font-display:swap;}

* { box-sizing: border-box; }
body { margin: 0; line-height: var(--lh-normal); }

/* numerals: tabular + LTR isolation handled at markup level via <bdi> */
.num { font-family: var(--font-num); font-feature-settings: "tnum" 1; font-variant-numeric: tabular-nums; }

/* --- glass tiers (Apple Regular / Clear / nav) --- */
.glass       { background: var(--lg-fill); -webkit-backdrop-filter: var(--lg-blur-panel); backdrop-filter: var(--lg-blur-panel);
               border: var(--lg-hairline); border-radius: var(--radius-card); box-shadow: var(--lg-shadow); isolation: isolate; position: relative; }
.glass--strong { background: var(--lg-fill-strong); }     /* under dense text */
.glass--clear  { background: var(--lg-fill-clear); }      /* MUST wrap text in .glass-scrim */
.glass--nav  { -webkit-backdrop-filter: var(--lg-blur-nav); backdrop-filter: var(--lg-blur-nav); background: var(--lg-fill); }
.glass--panel{ border-radius: var(--radius-panel); padding-block: var(--sp-6); padding-inline: var(--sp-6); }

/* scrim: the legibility insurance for Clear glass (§5) */
.glass-scrim { background: linear-gradient(rgba(255,255,255,.0), rgba(255,255,255,.0)),
               radial-gradient(120% 120% at 50% 0%, rgba(255,255,255,.35), transparent 70%); }

/* solid Blue-Jay hero monolith — the deliberate opaque anchor among glass */
.hero-solid { background: linear-gradient(160deg, var(--brand-tint), var(--brand) 55%, var(--brand-deep));
              color: #fff; border-radius: var(--radius-hero); box-shadow: var(--lg-shadow-hero);
              border: 1px solid rgba(255,255,255,.12); }

/* primary pill button */
.btn-primary { background: var(--brand); color: #fff; border: 0; border-radius: var(--radius-pill);
               padding-block: var(--sp-3); padding-inline: var(--sp-6); font-weight: var(--fw-semibold);
               transition: transform var(--dur-fast) var(--ease-glass); }
.btn-primary:active { transform: scale(.97); }

@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .glass, .glass--clear, .glass--nav { background: var(--lg-fill-solid); }
}
```

---

## 5. The legibility contract (MANDATORY — this is where glass dies cheap)

`backdrop-filter` cannot read the backdrop, so text contrast is unpredictable as the mesh shifts.
Every text-on-glass pairing must satisfy **all** of these or it doesn't ship:

1. **Body/data text never sits on `--lg-fill` or `--lg-fill-clear`.** It sits on `--lg-fill-strong`
   (0.72) or a `.glass-scrim`. Clear glass with raw text is banned.
2. **Verify at the lightest backdrop point**, not the average. Pick the brightest mesh orb under the
   surface and confirm **AA 4.5:1** for body, **3:1** for ≥`--t-title` / bold.
3. **The hero CTA is solid, not glass.** White on `#2B547E` ≈ **6.8:1** (passes AA). One opaque
   anchor among translucent panels is the Apple composition move, and it sidesteps the contrast
   gamble entirely for the most important control.
4. **Symbols/icons on glass get a vibrancy treatment**, not flat grey: tint them `--brand` at ≥70%
   opacity or give them their own micro-scrim; thin grey icons vanish on busy glass.
5. **Reduced Transparency / Increased Contrast** already swap fills to solid (§3) — never override
   that to "keep the look."

---

## 6. Concentric geometry (the detail that reads as "designed by Apple")

Nested rounded elements must share a center of curvature. The rule:

```
child_radius = parent_radius − parent_padding
```

Tokens make this turnkey. With `--pad-concentric: 8px`:

- Panel `--radius-panel: 28px`, padding `8px` → inner card radius **20px** (`--radius-card`). ✔ already aligned.
- Card `--radius-card: 20px`, padding `8px` → inner chip radius **12px** (`--radius-chip`). ✔ already aligned.

The token ladder (28 → 20 → 12, step 8) **is** a concentric ladder — nest one tier inside the next
with `padding: var(--pad-concentric)` and corners stay parallel for free. Helper:

```css
.concentric { padding: var(--pad-concentric); }
.concentric > .glass { border-radius: calc(var(--radius-panel) - var(--pad-concentric)); }
```

Controls (buttons, the attendance pill) use `--radius-pill` (continuous round), echoing device
hardware curves [Apple]. Never mix sharp 4px corners into a glass composition — it breaks the
material illusion instantly.

---

## 7. RTL (logical-property-native — no `[dir="rtl"]` override file)

Locked constraint from the plan; restated because it's load-bearing for the material too.

- Author **only** with logical properties: `margin-inline-*`, `padding-inline-*`, `inset-inline-*`,
  `border-inline-*`, `text-align: start/end`, `inset-block-*`. `dir="rtl"` on `<html>` flips
  everything for free. The specular glint default (`--lg-glint-x: 30%`) reads from the **leading**
  (right) edge in RTL because the gradient is positioned in content flow — keep it percentage-based.
- **Shadows are physical** (no logical form). Our glass shadows are vertically/centrally symmetric
  (`0 8px 24px`, x-offset 0) precisely so they need **no** RTL flip. Keep hero/contact shadows
  x-centered for the same reason. If a directional shadow is ever needed, flip it in a single
  `:dir(rtl)` rule, not a sprawling override file.
- **Numbers, phones, times stay LTR inside RTL:** wrap in `<bdi>` (e.g. `<bdi class="num">050-1234567</bdi>`,
  `<bdi>14:30</bdi>`). Never let them reorder.
- **Icon mirroring is selective:** mirror only directional glyphs (back/forward, chevrons, send,
  progress) via a `.icon-directional` class flipped under `:dir(rtl)`. Never mirror logos, clocks,
  checkmarks, the hamburger, search, brand.
- **Progress/steppers fill start→end = right→left** in RTL — the "last step vs expected step"
  tracker must originate from the inline-start (right) edge.

---

## 8. Motion (premium where it confirms, silent where it's noise)

Apple's glass glints and morphs are subtle and motion-gated. Mirror that discipline.

**Animate (it confirms an action):** attendance Present/Absent fill, task-complete check, sheet/card
open (materialize), CTA press, metric count-up, the pointer/scroll-driven specular glint on hero
surfaces only.
**Do NOT animate (noise):** per-load entrance staggers on the instructor home (opened ~20×/day —
keep it instant and functional), hover reveals on touch, parallax, list-row entrance on every row.

**Springs:**
- Confirm/overshoot: `--ease-spring` `cubic-bezier(0.34,1.56,0.64,1)` ~280ms (attendance fill, check).
- Glass surfaces (sheet/card open): `--ease-glass` `cubic-bezier(0.32,0.72,0,1)` ~420ms.
- Materialize keyframe (ramp blur+scale+opacity together, gated under reduced-motion):

```css
@media (prefers-reduced-motion: no-preference) {
  @keyframes lg-materialize {
    from { opacity: 0; transform: scale(.96);
           -webkit-backdrop-filter: blur(0); backdrop-filter: blur(0); }
    to   { opacity: 1; transform: scale(1);
           -webkit-backdrop-filter: var(--lg-blur-panel); backdrop-filter: var(--lg-blur-panel); }
  }
  .glass--enter { animation: lg-materialize var(--dur-slow) var(--ease-glass) both; }
}
```

- **Specular glint (motion-reactive, hero only):** vanilla JS updates `--lg-glint-x` on
  `pointermove` (desktop) / a throttled `scroll` (mobile), clamped, **disabled** under reduced-motion
  and battery-saver — mirroring Apple's ≤6px, motion-gated highlight [Apple, corroborated].
- **Library:** Motion One (~5KB, WAAPI springs off the main thread, tree-shakeable, MIT) over GSAP
  for this progressive-enhancement Razor stack. GSAP only if Admin later needs timeline-sequenced
  scroll choreography — not in Slice 0+.
- **GPU discipline:** animate only `transform`/`opacity` (+ the gated `backdrop-filter` ramp);
  `will-change: transform` only while animating, removed after.

---

## 9. Signature surfaces

### 9.1 The solid Blue-Jay "Take Attendance" monolith
The composition anchor. **Opaque**, not glass (`.hero-solid` — Apple's Identity variant), deep
gradient `--brand-tint → --brand → --brand-deep`, `--radius-hero`, the `--lg-shadow-hero` brand
glow. Occupies ~40% of the instructor home viewport, lower-center thumb zone. Press → `scale(.97)`
+ glow tightens. White label `--t-hero-cta`/700 (≈6.8:1, AA). It floats *among* glass panels — the
solid-vs-translucent contrast is the whole point; do not glass-ify it.

### 9.2 The swipe-or-tap attendance pill
Each roster row is a wide `--radius-pill` row on `--lg-fill-strong` glass (rows opaque-enough for
scan speed; the *list panel* is the glass, not 30 individual blurs — perf rule §10). Interaction:
tap inline-end half = Present (fill sweeps from the inline-start/leading edge in `--ok`, `--ease-spring`);
tap inline-start half = Absent (`--absent`). Optional drag for power users (mirror drag axis in JS
for RTL). Name = `--t-body`; phone = `<bdi class="num">`. **Tryout** students pinned at the bottom
inside a `--lg-fill-tint` tray, each with a "TRYOUT" pill badge.

### 9.3 The Admin bento command-center
Asymmetric glass bento (CSS Grid), **not** a list dashboard:
- **Total-customers** = tall hero metric tile, inline-start, `--t-metric`/700 `.num`.
- **Ticketing hub** (Action Items / disputes / CS alerts) = widest center tile spanning 2 rows — the focal point.
- **Open Tasks** = narrow inline-end column, scrollable, quick-complete checkboxes + "Generate New Task" pinned at its block-end.
- **Vacation approvals** = full-width band at block-end.
- **Syllabus module** = its own route/tab, not crammed into the bento.
Tiles are `.glass` Regular; only the metric hero may use `.glass--lensed`. Collapses to single
column below 768px (`grid-template-columns: 1fr`, all spans reset).

### 9.4 The lesson-model chip
The recurring brand signature. A `--radius-pill` `--lg-fill-tint` (brand-tinted glass) pill with a
small live status dot + model name, reused everywhere the current model appears (instructor hub,
attendance header, admin syllabus). Becomes the recognizable Orkabi element. `--lg-blur-inline`
tier; no lensing; `.glass-scrim` not needed because tint + small text on tinted glass clears AA.

---

## 10. Performance budget (mobile is the instructor's whole device)

1. **Blur only on fixed/sticky or non-scrolling surfaces.** Blur the chrome (topbar `.glass--nav`,
   bottom sheet, modal) and static cards. **Never blur a scrolling list container** — the
   attendance list panel is glass *once*; its rows scroll opaque.
2. **Max ~2 stacked blur layers in the viewport at a time on mobile.** A glass nav + a glass sheet
   is fine; a glass nav + glass list-of-30-glass-rows is not.
3. **`.glass--lensed` (SVG refraction) ≤ 1 instance on screen**, desktop-or-hero only, dropped under
   reduced-motion/transparency.
4. Blur ≤ 24px; `brightness()` only in nav/panel tiers, never inline.
5. One app-wide grain overlay max (fixed, `pointer-events:none`) — never per-card.
6. Always ship `-webkit-backdrop-filter` first; always provide the `@supports not` solid fallback.
7. `content-visibility: auto` on long off-screen lists; `will-change` only mid-animation.

---

## 11. Hand-off checklist for the Task 10 implementer

- [ ] Replace plan Task 10 Step 2 `:root` with §3 `tokens.css` (brand, 3-tier glass, fills,
      shadow stacks, radii ladder, type, motion, dark + a11y overrides).
- [ ] Replace Step 3 `base.css` core with §4 (glass tiers, hero-solid, scrim, pill button, fallback).
- [ ] Add the §3.1 mesh `body::before` (blue-family, supersedes the placeholder).
- [ ] Add the §2.3 hidden SVG `#lg-refraction` filter to `_Layout` (used only by `.glass--lensed`).
- [ ] Fonts: Assistant **400/500/600/700** + Heebo **500**, OFL, subset Hebrew+Latin+punct, real
      woff2 (no placeholders — plan Step 4a).
- [ ] Confirm logical-property-native throughout; no `[dir="rtl"]` override file.
- [ ] Legibility contract §5 enforced: no body text on `--lg-fill`/`--lg-fill-clear`; hero CTA solid.
- [ ] A11y media queries present and verified (reduced-transparency/contrast/motion swap to solid).
- [ ] `RtlLayoutTests` still passes (`dir="rtl"`, `lang="he"`, `tokens.css` present); consider adding
      an assertion that `--lg-fill-strong` / `--brand` exist in the served CSS.

---

## 12. Open decisions to confirm with the lead (none block Slice 0)

- **Dark mode in Slice 0?** Tokens include it, but Slice 0 ships light-only UI; the dark block is
  inert until a theme toggle exists. Cheap to keep; flag if you'd rather strip it now.
- **`.glass--lensed` in Slice 0?** Slice 0 has only auth + dashboards; the refraction edge has no
  hero surface yet. Ship the token/SVG plumbing, apply it first in the Slice with the attendance
  sheet / admin metric. (Not deferring the system — just noting first real use is later.)

**Deferred items:** None within this task — the full system is specified. Two scope *notes* above
(§12) are flagged transparently, not silently deferred: dark mode and `.glass--lensed` are fully
defined here but have no Slice-0 surface to attach to yet. Tell me if you want either pulled forward
or removed.
