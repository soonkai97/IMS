# IMS 2026 design refresh - visual specification

Author: fe-designer. Date: 2026-10-11. Status: ready for fe-architect (migration order, component contracts) and fe-framework (implementation).
Behaviour of the command palette, density, role-aware screens and the interaction flows is owned by `docs/design/2026-refresh-ux.md`
(written in parallel by ui-ux-designer; it did not exist when this was written, so this file defines how things look and the UX file wins
on how they behave; conflicts go to HANDOFFS.md).
Starting point: `docs/design/mvp-ux.md`, `docs/architecture/frontend.md`, `web/docs/conventions/scss-design.md`, the tokens in
`web/libs/shared/styles/src/variables/`, the shared UI in `web/libs/shared/ui`, `web/scripts/check-contrast.mjs`.
Product-owner decisions this spec is built on: calm clear tokens, a designed dark mode, a density option, restrained feedback motion,
role-aware screens, a keyboard-first command palette. Rejected on purpose: glassmorphism and heavy effects, decorative animation,
gradients that reduce contrast.

Fixed rules (every section obeys them; they are not re-argued below):

| # | Rule |
|---|---|
| F1 | WCAG 2.2 AA in light AND dark: text 4.5:1, UI components, control boundaries and focus indicators 3:1. Proven by `npm run contrast` (section 2.3). |
| F2 | 44px minimum touch target on phones (48px is the design size, `height('touch')`). Nothing breaks at 320px width or 400% zoom. |
| F3 | Numbers use tabular numerals; money always shows exactly 2 decimals. |
| F4 | A required field shows a red asterisk (`aria-hidden`) and the form shows the legend "* means required". |
| F5 | Status is never conveyed by colour alone: icon + text, or sign + text. |

## 1. Design principles

1. Calm: one accent, neutral surfaces, colour only where it means something (status, error, selection); no decoration.
2. Clear: text and borders that pass AA with margin, one clear primary action per screen, consistent sizes on an 8px grid.
3. Quiet motion: movement confirms an action (press, appear, dismiss) in at most 280ms and never plays for show; it all stops under reduce-motion.
4. Dark is its own palette: lower chroma, lighter surfaces for higher layers, no pure black or pure white, every pair re-checked.
5. Same components everywhere: one `app-*` look for phone and desktop, tokens only (no hard-coded values), so the UI library can be swapped without touching business code (PRD 10).

## 2. Token system

### 2.1 Rules

- Tokens are CSS custom properties `--ims-<group>-<name>` emitted in `globals/_themes.scss`; SCSS functions (`color()`, `space()`, ...) return `var(--ims-...)`. Components never hold a hex, px size, radius, shadow or duration of their own.
- Tokens are named by role, not by hue (`accent`, not `blue`), so a re-brand changes one file. The token names below are the new canonical set; section 6.2 maps every current name to its new name (old names keep working through an alias map until the clean-up step).
- Hierarchy of colour use: neutral for structure, `accent` for the one primary action, selection and links, the four semantic roles (success, warning, danger, info) only for state.
- `surface-*` tokens are backgrounds, `text-*` foregrounds. A foreground token is only valid on the backgrounds listed for it in the contrast table.

### 2.2 Colour roles (hex values)

Light and dark are two designed palettes, not an inversion: dark is lower in chroma, accent and semantic colours move to lighter tints so they stay readable on dark surfaces, and elevation goes lighter (section 5.1).

Surfaces and lines

| Token | Light | Dark | Role |
|---|---|---|---|
| `canvas` | `#f4f5f7` | `#0e1217` | page background (old `bg`) |
| `surface` | `#ffffff` | `#151a21` | cards, table, top bar, tab bar, sidebar (old `surface`) |
| `surface-raised` | `#ffffff` | `#1d232c` | anything floating over the page: dialog, sheet, menu, toast, popover, command palette (new) |
| `surface-sunken` | `#eceef2` | `#0b0f14` | table header, disabled field, kbd keys, segmented-control track, hover on rows (old `surface-2`) |
| `surface-strong` | `#e0e4ea` | `#2a323e` | pressed state, skeleton base, neutral chip, disabled button fill (old `surface-3`, `skeleton`) |
| `border` | `#d5d9e0` | `#2a313c` | dividers, card outlines: decorative, never the only cue (old `border`) |
| `border-strong` | `#6b7482` | `#8590a0` | boundary of an input, checkbox, radio, segmented control: needs 3:1 (old `border-strong`) |
| `overlay` | `#0a0e14` | `#000000` | scrim colour; used at 50% (light) and 60% (dark) |

Text

| Token | Light | Dark | Role |
|---|---|---|---|
| `text` | `#151a21` | `#e8ecf2` | body and headings |
| `text-muted` | `#4a5361` | `#a9b3c1` | secondary text, hints, placeholders, table header, disabled text. Still AA on every surface. There is no lower "subtle" grey: text below AA is not allowed |

Accent and semantic roles. Each role has the same shape: the role colour (text, icon, border and solid fill), `on-<role>` (text on the solid fill), `<role>-soft` (tinted background) and `on-<role>-soft` (text on that tint).

| Token | Light | Dark | Role |
|---|---|---|---|
| `accent` | `#2447c4` | `#8fb3ff` | links, primary button fill, selected tab/nav indicator, checked checkbox/radio/switch, selection edge (old `primary`) |
| `accent-hover` | `#1c37a0` | `#abc6ff` | primary hover (old `primary-hover`) |
| `accent-pressed` | `#162c84` | `#7aa0f5` | primary pressed (new) |
| `on-accent` | `#ffffff` | `#0b1530` | text/icon on accent fills (old `on-primary`) |
| `accent-soft` | `#e4eaff` | `#1c2b52` | selected row, active nav item, "Submitted" chip, active suggestion (old `primary-soft`) |
| `on-accent-soft` | `#1b2f86` | `#d4e1ff` | text on accent-soft (old `on-primary-soft`) |
| `success` | `#17692f` | `#6fd195` | success text/icon, positive stock effect, success button state (new) |
| `on-success` | `#ffffff` | `#07210f` | on success fill (new) |
| `success-soft` / `on-success-soft` | `#e1f4e7` / `#124d24` | `#112c1b` / `#bfeacb` | "Posted" chip, success banner |
| `warning` | `#8a4a00` | `#f2b04a` | warning text/icon, amber edge (old `warning-border`, now also text) |
| `on-warning` | `#ffffff` | `#2b1800` | on warning fill (new, rarely used) |
| `warning-soft` / `on-warning-soft` | `#fdf0d0` / `#5c3000` | `#35260a` / `#ffdf9f` | "Reversed" chip, "Low" chip, warning banner, override-needed lines |
| `danger` | `#b3261e` | `#ff8f86` | error text/icon/border, destructive fill, required asterisk, badge fill |
| `danger-hover` / `danger-pressed` | `#8f1d17` / `#751712` | `#ffb1aa` / `#f57268` | destructive button states |
| `on-danger` | `#ffffff` | `#2a0805` | on danger fill |
| `danger-soft` / `on-danger-soft` | `#fdebe9` / `#7a1a14` | `#3e1613` / `#ffd1cc` | error banner, invalid-line tint |
| `info` | `#0d6a85` | `#5fc5e0` | info icon/edge, "Approved" chip accent (new) |
| `on-info` | `#ffffff` | `#04222b` | on info fill (new) |
| `info-soft` / `on-info-soft` | `#def2f8` / `#0a4a5e` | `#0f2d37` / `#bfeaf5` | "Approved" chip, info banner |
| `focus` | `#2447c4` | `#9bbcff` | focus ring (3:1 on every surface and soft background) |

Chart series (not used yet - Reports are tables - emitted now so the first chart does not invent colours). Chosen from the Okabe-Ito colour-blind-safe set but darkened: the original amber, sky blue and reddish purple fail 3:1 on white (2.25, 2.31, 3.06). Series are always also told apart by marker shape or dash pattern and a direct label, never by hue alone.

| Token | Light | Dark | Hue |
|---|---|---|---|
| `chart-1` | `#2a5bd7` | `#7aa2ff` | blue |
| `chart-2` | `#c2410c` | `#f08a54` | vermilion |
| `chart-3` | `#0e7c5f` | `#4fc9a4` | green |
| `chart-4` | `#8a3fb8` | `#c79bf0` | purple |
| `chart-5` | `#9a6200` | `#e0b34a` | ochre |
| `chart-6` | `#5b6472` | `#9aa5b5` | slate (the "other" series) |

Status colours are aliases of the roles, not new colours (so a status can never drift from its role):

| Status | Background | Text | Icon | Notes |
|---|---|---|---|---|
| Draft | `surface-strong` | `text` | `pencil` | neutral |
| Submitted | `accent-soft` | `on-accent-soft` | `clock` | |
| Approved | `info-soft` | `on-info-soft` | `check` | |
| Posted | `success-soft` | `on-success-soft` | `check-double` | |
| Reversed | `warning-soft` | `on-warning-soft` | `undo` | |
| Cancelled | `surface-strong` | `text-muted` | `x` | label struck through |

Compatibility: the palette file keeps `status-*` / `on-status-*` entries for the contrast script and old call sites; they are literal copies of the values above and the script asserts they equal their role (section 6.2, step 1).

### 2.3 Contrast results

Computed with the WCAG 2.x relative-luminance formula by the script in Appendix A (same formula and structure as `web/scripts/check-contrast.mjs`, extended to the new token set; run it with `node contrast-2026.mjs [--md|--all]`). 81 pairs x 2 themes = 162 checks, 0 failing. Needs = 4.5 for text, 3 for UI components, control boundaries, icons/edges, focus ring, chart series.

Pairs that needed adjusting while designing (the values in 2.2 are the adjusted ones):

| Pair | Tempting value | Ratio | Used instead |
|---|---|---|---|
| Chart amber / sky / purple on white | Okabe-Ito `#E69F00` / `#56B4E9` / `#CC79A7` | 2.25 / 2.31 / 3.06 (need 3) | darkened to `chart-5` `#9a6200`, `chart-6` slate, `chart-4` `#8a3fb8` |
| Success fill with white text | `#16a34a` | 3.30 (need 4.5) | `#17692f` = 6.78 |
| Danger fill with white text | `#ef4444` | 3.76 | `#b3261e` = 6.54 |
| Amber text/edge on white | `#f59e0b` | 2.15 | `#8a4a00` = 6.86 |
| Light accent reused in dark | `#2447c4` on `#151a21` | 2.31 (need 4.5) | dark accent `#8fb3ff` = 8.37 (proof that dark needs its own palette) |

Closest to the limit (all pass): light `border-strong` on `canvas` 4.33 (limit 3), `info` on `surface-sunken` 5.29 (limit 4.5), `danger` on `surface-sunken` 5.63; dark `border-strong` on `surface-raised` 4.89, `text-muted` on `surface-strong` 6.10. Every text pair has at least 0.79 margin over 4.5 in light and more in dark, so a later hue tweak will not silently drop below AA.

Not in the table on purpose: `border`, `skeleton`/`surface-strong` as a fill, card outlines and row dividers are decorative (contrast 1.3 to 1.4). Nothing relies on them to be understood: rows carry text, cards carry headings, inputs use `border-strong`. Disabled controls are exempt from WCAG but are kept readable anyway (`text-muted` on `surface-strong`, 6.09 and 6.10).

Full results (script output, `Light` and `Dark` are the ratios):

| Foreground | on Background | Use | Needs | Light | Dark |
|---|---|---|---|---|---|
| text | canvas | text | 4.5 | 16.02 | 15.85 |
| text | surface | text | 4.5 | 17.48 | 14.74 |
| text | surface-raised | text | 4.5 | 17.48 | 13.33 |
| text | surface-sunken | text | 4.5 | 15.05 | 16.21 |
| text | surface-strong | text | 4.5 | 13.69 | 10.90 |
| text | accent-soft | text | 4.5 | 14.57 | 11.68 |
| text-muted | canvas | text | 4.5 | 7.13 | 8.86 |
| text-muted | surface | text | 4.5 | 7.77 | 8.24 |
| text-muted | surface-raised | text | 4.5 | 7.77 | 7.45 |
| text-muted | surface-sunken | text | 4.5 | 6.69 | 9.07 |
| text-muted | surface-strong | text | 4.5 | 6.09 | 6.10 |
| text-muted | accent-soft | text | 4.5 | 6.48 | 6.53 |
| accent | canvas | text (link) | 4.5 | 6.94 | 9.00 |
| accent | surface | text (link) | 4.5 | 7.57 | 8.37 |
| accent | surface-raised | text (link) | 4.5 | 7.57 | 7.57 |
| accent | surface-sunken | text (link) | 4.5 | 6.52 | 9.21 |
| on-accent-soft | accent-soft | text | 4.5 | 9.74 | 10.56 |
| on-accent | accent | text | 4.5 | 7.57 | 8.64 |
| on-accent | accent-hover | text | 4.5 | 9.98 | 10.53 |
| on-accent | accent-pressed | text | 4.5 | 12.16 | 7.02 |
| success | canvas | text | 4.5 | 6.21 | 10.05 |
| success | surface | text | 4.5 | 6.78 | 9.35 |
| success | surface-raised | text | 4.5 | 6.78 | 8.45 |
| success | surface-sunken | text | 4.5 | 5.84 | 10.28 |
| on-success | success | text | 4.5 | 6.78 | 9.10 |
| on-success-soft | success-soft | text | 4.5 | 8.66 | 11.32 |
| success | success-soft | icon / edge | 3 | 5.91 | 8.02 |
| warning | canvas | text | 4.5 | 6.29 | 9.92 |
| warning | surface | text | 4.5 | 6.86 | 9.22 |
| warning | surface-raised | text | 4.5 | 6.86 | 8.34 |
| warning | surface-sunken | text | 4.5 | 5.91 | 10.14 |
| on-warning | warning | text | 4.5 | 6.86 | 8.99 |
| on-warning-soft | warning-soft | text | 4.5 | 9.88 | 11.38 |
| warning | warning-soft | icon / edge | 3 | 6.06 | 7.74 |
| danger | canvas | text | 4.5 | 5.99 | 8.52 |
| danger | surface | text | 4.5 | 6.54 | 7.93 |
| danger | surface-raised | text | 4.5 | 6.54 | 7.17 |
| danger | surface-sunken | text | 4.5 | 5.63 | 8.72 |
| on-danger | danger | text | 4.5 | 6.54 | 8.39 |
| on-danger-soft | danger-soft | text | 4.5 | 9.19 | 11.47 |
| danger | danger-soft | icon / edge | 3 | 5.68 | 7.17 |
| info | canvas | text | 4.5 | 5.63 | 9.44 |
| info | surface | text | 4.5 | 6.14 | 8.78 |
| info | surface-raised | text | 4.5 | 6.14 | 7.94 |
| info | surface-sunken | text | 4.5 | 5.29 | 9.65 |
| on-info | info | text | 4.5 | 6.14 | 8.31 |
| on-info-soft | info-soft | text | 4.5 | 8.43 | 11.24 |
| info | info-soft | icon / edge | 3 | 5.31 | 7.27 |
| on-danger | danger-hover | text | 4.5 | 8.91 | 10.68 |
| on-danger | danger-pressed | text | 4.5 | 11.12 | 6.60 |
| accent | accent-soft | icon / edge | 3 | 6.31 | 6.63 |
| text-muted | surface-strong | text (disabled button) | 4.5 | 6.09 | 6.10 |
| border-strong | canvas | control boundary | 3 | 4.33 | 5.81 |
| border-strong | surface | control boundary | 3 | 4.72 | 5.40 |
| border-strong | surface-raised | control boundary | 3 | 4.72 | 4.89 |
| accent | canvas | indicator / fill | 3 | 6.94 | 9.00 |
| accent | surface | indicator / fill | 3 | 7.57 | 8.37 |
| accent | surface-raised | indicator / fill | 3 | 7.57 | 7.57 |
| focus | canvas | focus ring | 3 | 6.94 | 9.87 |
| focus | surface | focus ring | 3 | 7.57 | 9.18 |
| focus | surface-raised | focus ring | 3 | 7.57 | 8.30 |
| focus | surface-sunken | focus ring | 3 | 6.52 | 10.10 |
| focus | surface-strong | focus ring | 3 | 5.93 | 6.79 |
| focus | accent-soft | focus ring | 3 | 6.31 | 7.27 |
| focus | success-soft | focus ring | 3 | 6.60 | 7.88 |
| focus | warning-soft | focus ring | 3 | 6.69 | 7.70 |
| focus | danger-soft | focus ring | 3 | 6.58 | 8.30 |
| focus | info-soft | focus ring | 3 | 6.55 | 7.60 |
| danger | surface | invalid border | 3 | 6.54 | 7.93 |
| chart-1 | surface | chart series | 3 | 5.87 | 7.02 |
| chart-1 | surface-raised | chart series | 3 | 5.87 | 6.35 |
| chart-2 | surface | chart series | 3 | 5.18 | 7.05 |
| chart-2 | surface-raised | chart series | 3 | 5.18 | 6.37 |
| chart-3 | surface | chart series | 3 | 5.16 | 8.51 |
| chart-3 | surface-raised | chart series | 3 | 5.16 | 7.69 |
| chart-4 | surface | chart series | 3 | 6.03 | 7.80 |
| chart-4 | surface-raised | chart series | 3 | 6.03 | 7.05 |
| chart-5 | surface | chart series | 3 | 5.10 | 8.92 |
| chart-5 | surface-raised | chart series | 3 | 5.10 | 8.06 |
| chart-6 | surface | chart series | 3 | 5.98 | 7.01 |
| chart-6 | surface-raised | chart series | 3 | 5.98 | 6.34 |

Usage limits that follow from the table (components must respect them; the script cannot see a misuse):
- Muted text is allowed on `canvas`, `surface`, `surface-raised`, `surface-sunken`, `surface-strong`, `accent-soft`. On other soft tints (success/warning/danger/info) use that tint's `on-*-soft`.
- A role colour as text (`danger`, `success`, ...) is allowed on the four surfaces, never on a soft tint of another role. On its own tint use `on-<role>-soft` for text and the role colour only for icon/edge.
- Links inside a selected row (`accent-soft`) are `accent` (6.31 / 6.63) - fine, but keep underline (links are underlined everywhere, so colour is never the only cue).
- White-on-photo or text over images is not allowed; text sits on a surface token.

### 2.4 Spacing

8px grid, base unit `0.5rem`, 4px half-step. Unchanged names; one new step.

| Step | `space(n)` | px | Typical use |
|---|---|---|---|
| 0 | `0.25rem` | 4 | icon-to-label gap in chips, label-to-control gap |
| 1 | `0.5rem` | 8 | gap between controls in a row, card gap on phone |
| 1.5 (new) | `0.75rem` | 12 | compact-density padding, kbd padding, list row padding |
| 2 | `1rem` | 16 | page gutter, card padding, form gap, dialog padding |
| 3 | `1.5rem` | 24 | section gap |
| 4 | `2rem` | 32 | page section, empty-state padding |
| 5-9 | 2.5 / 3 / 3.5 / 4 / 6 rem | 40 / 48 / 56 / 64 / 96 | layout only |

Page gutter: 16px at every width (so 320px keeps 288px of content). Content max width 76rem. Inline padding uses logical properties (`padding-inline`) in new code.

### 2.5 Radius and border widths

| Token | Now | New | Use |
|---|---|---|---|
| `sm` | 6px | 6px | chips inside dense rows, kbd, inline code, skeleton lines |
| `md` | 10px | 8px | buttons, inputs, cards, table frame, banners, toasts |
| `lg` | 16px | 12px | dialogs, popovers, command palette; the top corners of a bottom sheet are 16px (`lg` + 4px, via `border-radius('sheet')`) |
| `pill` | 999px | 999px | status chips, count badges, segmented-control thumb |

Nested rule: an inner radius is the outer radius minus the padding between them (never rounder than its container).
Border widths: `thin` 1px (outlines, dividers); `thick` 2px (tab underline, invalid state ring); new `ring` 2px (focus ring, replaces 3px); new `edge` 3px (left edge of banners, toasts, selected rows, line status). Today `border-width('focus')` (3px) is used for both; the migration splits it.

### 2.6 Elevation

At most three levels, all subtle. In light they are shadows; in dark the shadow is weak and the layer reads through the lighter surface plus a visible border (section 5.1).

| Level | Token | Light shadow | Dark shadow | Used for |
|---|---|---|---|---|
| 0 | none | - | - | everything on the page: cards, table, bars (separated by `border`) |
| 1 | `shadow(1)` | `0 1px 2px rgb(16 24 40 / 8%), 0 1px 1px rgb(16 24 40 / 6%)` | `0 1px 2px rgb(0 0 0 / 40%)` | sticky action bar, sticky table header while stuck |
| 2 | `shadow(2)` | `0 4px 12px rgb(16 24 40 / 12%), 0 1px 3px rgb(16 24 40 / 8%)` | `0 4px 12px rgb(0 0 0 / 45%)` | menus, popovers, toasts |
| 3 | `shadow(3)` | `0 12px 32px rgb(16 24 40 / 16%), 0 2px 6px rgb(16 24 40 / 8%)` | `0 12px 32px rgb(0 0 0 / 55%)` | dialogs, bottom sheets, command palette |

Shadows become theme custom properties (`--ims-shadow-1..3`), so `shadow(n)` returns `var(...)`. No coloured shadows, no inner glow, no blur backdrop.

### 2.7 Density (comfortable / compact)

Density only changes the tokens below; font sizes, colours and the 44px phone rule do not change. Default is comfortable. Stored per user in `localStorage` key `ims.density` (same pattern as `ims.theme`); applied as `<html data-density="compact">`.

Compact applies only where a precise pointer and room exist: `@media (pointer: fine) and (min-width: 600px)`. On a phone, or any coarse (touch) pointer, comfortable is forced and the toggle is hidden, so the 44px target rule (F2) cannot be broken by a stored preference.

| Token | Comfortable | Compact | Used by |
|---|---|---|---|
| `--ims-control-height` | 3rem (48) | 2.5rem (40) | button, input, select, search, segmented control |
| `--ims-control-pad-x` | 1rem | 0.75rem | button and input inline padding |
| `--ims-row-height` | 3rem (48) | 2.25rem (36) | table row, list row, nav item, command-palette item (compact 36) |
| `--ims-cell-pad-y` | 0.5rem | 0.25rem | table cells |
| `--ims-card-pad` | 1rem | 0.75rem | card, banner, dialog body |
| `--ims-stack-gap` | 1rem | 0.75rem | gap between form fields and between cards |

36px compact rows still exceed the WCAG 2.2 AA minimum target (24px); the whole row is the click target, not a small link. `height('touch')` keeps meaning "48px design size" and becomes `var(--ims-control-height)`; `height('touch-min')` (44px) stays a hard floor used by icon-only buttons on phones and all coarse pointers.

### 2.8 Typography

Font stack: keep the system stack, no web font.

```
system-ui, -apple-system, 'Segoe UI', Roboto, 'Noto Sans', 'PingFang SC', 'Microsoft YaHei', 'Noto Sans SC', sans-serif
```

Why (PWA, offline, performance, language):
- The app is English, Chinese and Malay. A Latin variable font (Inter and similar) covers one of the three; Chinese would still fall back to a system font, so two typefaces with different metrics would sit side by side. A full CJK web font is several MB and out of the question.
- Zero font bytes: no FOUT/FOIT, no layout shift, nothing to precache, nothing that can fail offline, no `font-src` change in the strict CSP (`font-src 'self'` stays as is).
- Every system face in the stack has tabular numerals (`tnum`) and a clear digit shape (Segoe UI Variable, SF, Roboto). The tabular-numerals rule (below) works without extra files.
- Cost: the look differs slightly per OS. Accepted for a work tool; the spacing and tokens carry the identity. If the product owner wants brand typography later, the alternative is one self-hosted Latin variable font, subset, `font-display: swap`, with `size-adjust` on the fallback to prevent shift; budget +60 to 110 KB precached in the service worker (open question Q2).

Type scale (rem; `font-size(n)` steps keep their numbers so call sites do not change; line heights are new and paired):

| Step | Size | Line height | Weight | Role |
|---|---|---|---|---|
| 1 | 0.8125rem (13) | 1.4 | 400 / 500 | caption: hint, chip, table header, tab label, kbd, badge |
| 2 | 0.9375rem (15) | 1.4 | 400 / 500 | small: field label, error text, secondary line, table body in compact |
| 3 | 1rem (16) | 1.5 | 400 / 500 | body, inputs (never below 16px: iOS zoom), buttons, table body |
| 4 | 1.25rem (20) | 1.3 | 600 | section heading, dialog title, card heading |
| 5 | 1.5rem (24) | 1.25 | 600 | page title (h1) |
| 6 | 2rem (32) | 1.15 | 600 | stat value on Home only |

Weights: 400, 500, 600 (`bold` 650 becomes 600: system fonts have no 650, Roboto would jump to 700). Body line height 1.5 (was 1.45) so text still passes WCAG 1.4.12 (text spacing override) without clipping. Letter spacing 0 everywhere (Chinese). No uppercase transforms (hurts readability and Chinese/Malay); table headers become sentence case 13px/500 `text-muted`. Text blocks are capped at 70ch. Everything is in rem, so 200% text zoom and 400% page zoom reflow.

Tabular numerals rule: `font-variant-numeric: tabular-nums` (mixin `numeric`) on every element that shows a number a user compares, adds or scans in a column: table cells with `--end`, quantities, money, stat values, document numbers and dates in lists, pager range, count badges, kbd shortcuts, inputs with `inputmode="decimal"`/`type=number`. Prose (banner and dialog sentences) keeps proportional digits. Numbers are right-aligned in columns, the header aligns the same side, units sit in their own muted cell or a muted suffix after the number.

Number formats (already in `format.pipes.ts`; the design confirms them):
- Money: exactly 2 decimals always (`1,234.50`, `0.00`), thousands separator, currency as a muted `RM` prefix only when the column or label does not already say it. Negative money uses the true minus `−` (U+2212). A cost the user may not see is not rendered at all (no `0.00` placeholder, `mvp-ux.md` 1.4).
- Quantity: up to 3 decimals, trailing zeros trimmed.
- Stock effect: always signed (`+5`, `−5`; `0` has no sign). The sign carries the meaning; colour (`success` / `danger` text) only reinforces it.

### 2.9 Iconography

- One set: the in-house 24x24 stroke icons in `icon.paths.ts` behind `app-icon`. No second library, no emoji, no raster icons in the UI. Swapping the set later changes only `app-icon`.
- Grid and style: 24 unit view box, round caps and joins, no fill (except the dot glyphs), `currentColor`. Stroke is fixed in screen pixels with `vector-effect: non-scaling-stroke`: 1.5px at 16px, 1.75px at 20px and 24px. (Today the stroke is 2 units and scales down to 1.3px at 16px, which is too thin.)
- Sizes: 16 (inside chips, inline with text), 20 (buttons, fields, table actions), 24 (nav, tab bar, top bar), 40 (empty state, inside a 64px `surface-sunken` disc). No other sizes.
- Rules: an icon never stands alone as the only carrier of meaning - it comes with visible text, or the control has `aria-label` and a 44px target. Decorative icons are `aria-hidden`. Colour follows the text it sits with; semantic colour only for state icons (alert, check).
- New icons to draw (same style): `monitor` (system theme), `command` (palette trigger), `density-comfortable`, `density-compact`, `lock` (view-only). The six status icons already exist.

### 2.10 Layout constants (unchanged unless noted)

| Token | Value | Note |
|---|---|---|
| `height('touch')` | `var(--ims-control-height)` | 48 comfortable, 40 compact (desktop pointer only) |
| `height('touch-min')` | 44px | hard floor on phone and coarse pointer |
| `height('bar')` / `('tabbar')` | 56px / 64px | tab bar adds `env(safe-area-inset-bottom)` |
| `height('actionbar')` (new) | 64px | sticky document action bar |
| `width('dialog')` | 30rem (480) | `width('palette')` (new) 40rem |
| Breakpoints | 600px, 1024px | unchanged |
| `z-index` | base 0, sticky 10, nav 20, overlay 30, dialog 40, toast 50 | add `palette` 45 |

## 3. Component specs

Every component below is an `app-*` wrapper: callers pass semantics (variant, state, density-neutral), the wrapper maps them to tokens. Nothing in this section names a library class or element, so a different underlying library can sit behind the same wrapper. A component sets custom properties on `:host` and its parts read them (existing pattern, no class nesting).

Global interaction rules:
- Focus: `outline: 2px solid focus; outline-offset: 2px` on `:focus-visible` for everything focusable. Where the outline would be clipped (rows, table cells, scroll containers, tab list) use `outline-offset: -2px` (inset). Never `outline: none` without a replacement. Verified on all backgrounds in 2.3.
- Hover is never the only way to reach anything; hover colours are subtle (`surface-sunken`, `*-hover`).
- Forced colours (Windows High Contrast): keep borders on buttons, chips, inputs and selected rows (use `border: 1px solid transparent`, never `border: 0`, so the system draws them); selected state also has an edge bar and an icon or check, so it survives `forced-colors`.
- 320px / 400% zoom: no fixed widths in px; min-width 0 on flex children; text wraps (`overflow-wrap: anywhere` for names, nowrap only for SKU and document numbers); buttons may wrap their label below 400px (`min-height` instead of `height`).

### 3.1 Button

Variants and what they mean: Primary = the one main action of the screen (at most one visible per area). Secondary = other actions. Quiet = low-emphasis, toolbar and inline. Danger = destructive or irreversible (post is not danger; reverse, reject, cancel document, discard are).

| State | Primary | Secondary | Quiet | Danger |
|---|---|---|---|---|
| Rest | bg `accent`, text `on-accent`, border `accent` | bg `surface`, text `text`, border `border-strong` | bg transparent, text `text`, border transparent | bg `danger`, text `on-danger`, border `danger` |
| Hover | bg `accent-hover` | bg `surface-sunken` | bg `surface-sunken` | bg `danger-hover` |
| Pressed | bg `accent-pressed`, `transform: scale(0.98)` | bg `surface-strong`, scale 0.98 | bg `surface-strong`, scale 0.98 | bg `danger-pressed`, scale 0.98 |
| Focus-visible | ring `focus` 2px, offset 2px | same | same | same |
| Busy | rest colours kept (no opacity fade, contrast stays), label kept, 20px spinner replaces the leading icon, `aria-busy="true"` and `aria-disabled="true"`, focus stays on the button, clicks ignored | same | same | same |
| Disabled | bg `surface-strong`, text `text-muted`, border `border`, `cursor: not-allowed` | same | text `text-muted`, no bg | same |
| Success (1.5s, after a save/copy that completes in place) | bg `success`, text `on-success`, `check` icon and the label "Saved" (or the action's past tense); reverts after 1.5s; announced by the live region | bg `success-soft`, text `on-success-soft`, border `success` | - | - |

- Danger has an outlined subtype for secondary placement (e.g. Reverse on a posted document, `mvp-ux.md` 7.3): bg `surface`, text `danger`, border `danger`, hover bg `danger-soft`, pressed `danger-soft` + border `danger-hover`.
- Disabled-with-a-reason: use `aria-disabled="true"` (stays focusable) and point `aria-describedby` at the visible reason, e.g. Approve for the submitter (E3). Truly unavailable actions are absent, not disabled (`allowedActions`).
- Size: height `--ims-control-height` (48 / 40), min width 44, inline padding `--ims-control-pad-x`, radius `md`, font step 3 weight 500, icon 20px with 8px gap. Icon-only button: square of the same height, never below 44x44 on phone/coarse pointer, `aria-label` required. Two buttons in a bar share width equally on phone (`flex: 1`), primary on the right.
- Block variant (`--block`) fills the width; used in sheets and the Home actions.
- Transition: none on colour (see section 4: only opacity and transform animate). Pressed feedback is the 80ms scale.

### 3.2 Form field

Structure, top to bottom, 4px (`space(0)`) apart: label, hint (optional), control, error (optional). Between fields `--ims-stack-gap`.

| Part | Spec |
|---|---|
| Label | step 2 (15px) weight 500, `text`, always visible (no placeholder labels), above the control. |
| Required marker | `*` directly after the label text, `danger`, weight 600, `<span aria-hidden="true">`. The control itself carries `required` (or `aria-required="true"`), so assistive technology says "required" without the asterisk. This replaces the "(required)" text of `mvp-ux.md` section 2 (decision of the product owner, rule F4). |
| Legend | The first element inside every form (and dialog form) that contains at least one required field: `* means required` - step 2, `text-muted`, the asterisk in `danger`. The sentence is the i18n message `form.requiredLegend`. It is `aria-hidden` (the same information is exposed per control). Forms with no required field show no legend. |
| Hint | step 1 (13px) `text-muted`, linked with `aria-describedby`. |
| Control | min height `--ims-control-height`, padding-inline `--ims-control-pad-x`, font step 3 (16px minimum), bg `surface`, border 1px `border-strong`, radius `md`, text `text`, placeholder `text-muted`. Numeric controls use `inputmode="decimal"` and `font-variant-numeric: tabular-nums`; right-aligned only inside table-like line grids. |
| Hover | border `text-muted`. |
| Focus | border `accent` + ring `focus` 2px offset 2px. |
| Error | border `danger` plus a 1px inset ring of the same colour (looks 2px, no layout shift), `aria-invalid="true"`; message below: `alert` icon 16px + text step 2 `danger`, `id` linked by `aria-describedby`. The message text is the cue, the colour only reinforces it. |
| Disabled | bg `surface-sunken`, text `text-muted`, border `border`, `cursor: not-allowed`; the label stays `text`. If a rule disabled it, the reason is the hint. |
| Read-only (posted documents) | not a disabled input: rendered as label + plain text value (`dl`), no border. |

Checkbox, radio and switch: 24px visual (20px glyph box inside a 44px hit area on phone), border `border-strong` 2px, checked bg `accent` with an `on-accent` check/dot icon (shape, not only colour); switch track 44x24 with a 20px thumb that moves by `transform`. Label to the right, whole row is the target.

### 3.3 Table and list rows

Desktop (>= 1024px) is a table; below that every row becomes a card (the existing `app-table` behaviour, kept).

| Aspect | Spec |
|---|---|
| Frame | bg `surface`, 1px `border`, radius `md`, no shadow. |
| Header | bg `surface-sunken`, text `text-muted`, step 1 weight 500 sentence case, 1px `border` below. Sticky (`position: sticky; top: <top bar height>`) inside the page scroll; when stuck it gains `shadow(1)`. Sort button is the whole header cell (min 36 high, 44 on coarse pointer), `aria-sort` set, sort direction shown with a chevron icon, not only bold. |
| Row height | `--ims-row-height`: 48 comfortable, 36 compact. Cell padding `--ims-cell-pad-y` x `space(2)` (compact `space(1.5)`). |
| Separation | Dividers: 1px `border` between rows. No zebra by default (zebra plus status chips plus selection makes three competing tints). Zebra is allowed only for report tables of 8+ columns, as `surface-sunken` at every second row, and never together with selection tint. |
| Hover | bg `surface-sunken` (rows that are links). |
| Selected | bg `accent-soft`, 3px `accent` bar on the row's inline start (`edge`), a checked checkbox in the first column, `aria-selected="true"`. Text stays `text` (14.57 / 11.68). |
| Focus | inset ring (`outline-offset: -2px`) on the row link or cell. |
| Numeric columns | right-aligned header and cells, tabular numerals, units in their own muted column or muted suffix. Signed values keep the sign. Empty cell shows nothing (not `-`) except where a value is conceptually zero (`0`). |
| Text columns | start-aligned; primary column (SKU, document number) weight 500 and `nowrap`; names wrap, never truncate with ellipsis in the primary reading path (truncate only in secondary columns, with the full value in the row detail). |
| Row status | a status chip (3.4) in the last column on desktop, top-right of the card on phone. Flags (Returned, Override needed, Large) sit left of the status chip. |
| Loading refetch | existing: keep rows at 60% opacity and show the thin progress bar (3.9); first load shows skeleton rows (3.8). |
| Phone card | bg `surface`, 1px `border`, radius `md`, padding 12px 16px, 8px between cards, min height 64, title (document no. or product) step 3 weight 500, status chip at top right, meta lines step 2 `text-muted`, the key number (e.g. on-hand) step 4 weight 600 tabular. The card is one tap target >= 48px high. Cards are never compact-density (phones are comfortable only). |
| Footer / pager | buttons 44px on phone, `--ims-control-height` on desktop; range text tabular. |

### 3.4 Status chips and flags

- Shape: pill, height 24px, padding-inline 8px, gap 4px, step 1 weight 500, icon 16px before the label, no border, no shadow. Label is always visible text (never icon-only, never truncated). Colours from the status table in 2.2. Cancelled label is struck through (`text-decoration: line-through`) in addition to the `x` icon.
- Draft/Submitted/Approved/Posted/Reversed/Cancelled: as the table in 2.2.
- Flags (not statuses, outlined: transparent bg, 1px border, same height): Returned (`warning` border and text, icon `arrow-left`), Override needed (`danger` border and text, icon `alert`), Large (`accent` border and text, icon `tag`). Text colours are the role colours (`warning` 6.86 / 9.22 on surface, `danger` 6.54 / 7.93, `accent` 7.57 / 8.37); all pass 4.5 on `surface`, `canvas`, `surface-raised` and `surface-sunken`. A flag on a selected row (`accent-soft`) uses the matching `on-*-soft` text colour instead.
- "Low" stock chip: `warning-soft` bg, `on-warning-soft` text, `alert` icon, text "Low".
- A chip that opens a reason (Returned) is a button: the hit area grows to 44px with a transparent pseudo-element (`::before { inset: -10px 0 }`), the visible chip stays 24px.
- Count badge (tab bar, nav): min 20px, pill, `danger` bg, `on-danger` text step 1 weight 600, tabular, `99+` cap; the number is the cue.

### 3.5 Cards

| Variant | Spec |
|---|---|
| Card | bg `surface`, 1px `border`, radius `md`, padding `--ims-card-pad`, no shadow. Heading step 4 weight 600. Flat in both themes. |
| Interactive card | whole card is one link/button (the `::after` cover technique already used); hover border `border-strong`; focus inset ring; no lift, no scale. |
| Stat card (Home) | icon 24px in `accent`, label step 2 `text-muted`, value step 6 weight 600 tabular, optional "Attention" state: 3px `warning` edge on the inline start plus `alert` icon and a text label ("Needs attention"), not colour alone. A hidden value (no `valuation.view`) removes the whole tile. |
| Document summary (`dl`) | label step 1 `text-muted`, value step 3 weight 500, grid `repeat(auto-fit, minmax(14rem, 1fr))`. |
| Phone-first Home action | the two big buttons: `accent` fill, `on-accent` text, step 4 weight 600, height 64, radius `lg`; hover `accent-hover`. |

### 3.6 Dialogs and bottom sheets

| Aspect | Dialog (>= 600px) | Bottom sheet (< 600px) |
|---|---|---|
| Container | native `<dialog>`, width `min(100% - 32px, 30rem)`, bg `surface-raised`, 1px `border`, radius `lg`, `shadow(3)`, centred | width 100%, anchored bottom, top radius 16px, max-height 90dvh, bg `surface-raised`, `shadow(3)`, bottom padding `env(safe-area-inset-bottom)`; a 4x36 `surface-strong` handle bar at the top (decorative, `aria-hidden`; swipe down closes, plus the visible close button) |
| Scrim | `overlay` at 50% (light) / 60% (dark), no blur | same |
| Header | title step 4 weight 600, close icon button 44x44 at the end, bottom 1px `border` | same |
| Body | padding `--ims-card-pad`, scrolls inside, gap `--ims-stack-gap` | same |
| Footer | right-aligned buttons, 8px gap, wraps; primary last. Destructive dialogs: the danger button is last and never pre-focused | buttons `flex: 1 1 8rem`: two buttons stay side by side at 360px and wrap onto two lines at 320px; DOM order is kept (primary last) in both cases |
| Motion | opacity 0 to 1 + `translateY(8px)` to 0, 200ms `enter`; exit 120ms opacity only | `translateY(100%)` to 0, 280ms `enter`; exit 200ms |
| Focus | trapped, Esc closes, first `data-autofocus` control gets focus, focus returns to the trigger | same |

Content never sits on the scrim, only on `surface-raised`, so dialog text keeps the `surface-raised` contrast rows in 2.3.

### 3.7 Toasts and inline banners

Toast (transient, `app-toast`):
- bg `surface-raised`, 1px `border`, 3px `edge` on the inline start in the state colour (success `success`, error `danger`, info `info`), radius `md`, `shadow(2)`, padding 12px 8px 12px 16px, width up to 24rem and `100% - 32px` on phone.
- Content: state icon 20px (check / alert / info) in the state colour, message step 3 `text`, optional action ("View", "Retry": quiet button, underlined text, min 44x44), close button 44x44. Error toasts use bg `danger-soft`, text `on-danger-soft`, icon `danger`.
- Success auto-dismisses after 4s (timer pauses on hover and focus); error stays until dismissed and has `role="alert"`; success/info are polite. Max 2 stacked, 8px apart. Position: phone above the action bar and tab bar (`bottom: calc(var(--ims-bottom-offset) + 8px)`, the shell publishes `--ims-bottom-offset`), desktop top right below the top bar.
- Motion: opacity + `translateY(8px)` in 200ms; exit 120ms opacity.

Inline banner (persistent, `app-banner`):
- Tinted block: bg `<role>-soft`, text `on-<role>-soft`, 3px `edge` on the inline start in the role colour, radius `md`, padding `--ims-card-pad`, icon 20px in the role colour (info `info`, success `success`, warning `warning`, danger `danger`), heading weight 600, body step 3, actions below or at the end (buttons are the normal variants; on tinted backgrounds the focus ring keeps 3:1, see 2.3).
- Roles: info (default), success, warning (override, returned, short stock), danger (stale document E4, business rule error). Danger/warning banners are `role="alert"` only when they appear as a result of an action; those present at load are `role="status"`.
- Copy lives in i18n; banners never rely on colour: the icon and the heading word ("Returned by Raj", "Short by 18 PCS") carry the state.

### 3.8 Skeleton loaders

- Shapes (each `aria-hidden`; the region has `aria-busy="true"` and a visually hidden "Loading..."): text line (height 1em, width 40-90%, radius `sm`), title line (1.5em, 40%), chip (24px high, 72px wide, pill), block (card, radius `md`), table row (`--ims-row-height` with 3-5 line placeholders in the real column widths), card row on phone (the 64px card with two lines and a chip), circle (40px, avatar-like).
- Colour: base `surface-strong` on `surface`/`canvas`. They contain no text, so no text-contrast requirement.
- Shimmer: a pseudo-element covers the shape, `background: linear-gradient(90deg, transparent, color-mix(in srgb, var(--surface) 55%, transparent), transparent)`, animated with `transform: translateX(-100%)` to `translateX(100%)` over 1.4s `ease-in-out`, infinite; the gradient lives only on placeholder blocks, never over text or controls. In dark the highlight is `surface-raised` at 55%.
- Timing: appear only after 300ms of waiting (nothing flashes for fast responses), show at least 400ms once shown. Skeleton counts match the expected page (5 rows on lists, header + 3 lines on a document).
- Reduce-motion: no shimmer, no pulse; the static `surface-strong` shapes remain. Layout is identical, so nothing shifts when content arrives.

### 3.9 Progress and busy indicators

- Top progress bar (shell and table refetch): a 3px track of `surface-strong` with a solid `accent` segment 40% wide (no gradient fade) moves with `transform: translateX(-100%)` to `translateX(250%)` over 1.2s linear. (Replaces the `transparent -> primary -> transparent` gradient animated through `background-position`, which animates paint and fades the ends of the bar to nothing.) Reduce-motion: a static `accent` bar at 40% width plus the text "Loading" for assistive technology (`role="progressbar"` without value).
- Spinner (button, slow request): 20px ring, 2px stroke, rotates with `transform: rotate`, 0.8s linear. Reduce-motion: the spinner is replaced by the static `clock` icon and the visible text "Working..." next to it (never an empty static circle).

### 3.10 Command palette

Opened by the keyboard shortcut and the top-bar trigger (behaviour and the list of commands: UX file). Visual spec:

| Part | Spec |
|---|---|
| Trigger (desktop top bar) | looks like a quiet search field: height 40, width 20rem (collapses to a 44px icon button below 1024px), bg `surface-sunken`, border 1px `border`, `search` icon 20px, placeholder "Search or jump to..." in `text-muted`, `<kbd>` hint `Ctrl K` (`Cmd K` on macOS; hidden on touch devices). |
| Overlay | native `<dialog>` modal, scrim `overlay` at 35% (lighter than a dialog: it is a navigation aid), container width `min(100% - 32px, 40rem)`, top offset 12vh (desktop); on phones a full-height sheet from the top bar. bg `surface-raised`, 1px `border`, radius `lg`, `shadow(3)`, max-height 70dvh. |
| Input row | height 56, `search` icon 20px `text-muted`, input font 18px (step 3 up to 18 allowed, never below 16), no own border (the container is the frame), bottom 1px `border`. A clear button (44px) when text is present. |
| Group label | step 1 weight 500 `text-muted`, padding 12px 16px 4px (e.g. "Go to", "Actions", "Recent"). |
| Result item | height `--ims-row-height` (min 44 on touch), padding-inline 16px, icon 20px, label step 3, optional right-aligned secondary text `text-muted` and a `<kbd>` shortcut. Matched characters weight 600 (not colour). |
| Active item (arrow keys / hover) | bg `accent-soft`, 3px `accent` edge on the inline start, label `on-accent-soft`, and a `↵` hint at the end; `aria-selected="true"`. Focus stays in the input (`aria-activedescendant`), so the active item uses this fill instead of the focus ring. |
| Disabled / not allowed | not rendered (role-aware: a command the user may not run is absent, not greyed). If the UX file asks for a "locked" row, use `lock` icon + `text-muted` + the reason as secondary text; never opacity. |
| Empty | `search` icon 40, "No results for "{query}"." step 3, one line "Try a document number, SKU or a page name." `text-muted`. |
| Footer | bg `surface-sunken`, height 40, step 1 `text-muted`: `↑ ↓ Navigate`, `↵ Open`, `Esc Close`, each key in a `<kbd>`. Hidden on phones. |
| `<kbd>` | min 24x24, padding 0 6px, bg `surface-sunken`, 1px `border`, radius `sm`, step 1 weight 500 tabular, `text` colour. |
| Motion | opacity 0 to 1 + `translateY(-8px)` to 0, 160ms `enter`; exit 100ms opacity. |

### 3.11 Density toggle

A two-option segmented control (radio group semantics): `Comfortable` / `Compact`, each with its icon (`density-comfortable`, `density-compact`) and its text. Track bg `surface-sunken`, 1px `border`, radius `md`, 2px padding; selected segment bg `surface`, 1px `border-strong`, weight 600, plus a `check` icon (so selection is shape + weight, not only a lighter fill). Segment height `--ims-control-height`, min 44 on touch. Placement: the account menu and the table toolbar (wide screens). Hidden when compact cannot apply (phone or coarse pointer, section 2.7). Switching changes the tokens instantly (no animation) and announces "Compact density on" politely.

### 3.12 Theme switch

Three-option segmented control, same visual as 3.11: `System` (`monitor` icon), `Light` (`sun`), `Dark` (`moon`), icons plus text labels at >= 400px; at 320 to 399px labels stay visible (the segments wrap to a vertical list inside the account menu rather than dropping the text). When System is selected the control shows the resolved result as muted text: "System (dark)". Keyboard: arrow keys move, Space/Enter select. Changing it applies at once with no animation (no cross-fade of the whole page).

### 3.13 Sticky mobile footer and tab bar (phone, < 600px)

Tab bar:
- Fixed bottom, height 64 plus `env(safe-area-inset-bottom)`, bg `surface`, 1px top `border`, no shadow. Four role tabs plus More (`mvp-ux.md` 1.2): five equal cells (64px each at 320px width), each a 64x64 target.
- Cell: icon 24px, label step 1 below (single line, ellipsis, full name in `aria-label`). Inactive: `text-muted`. Active (`aria-current="page"`): icon and label `accent`, label weight 600, plus a 3x24px `accent` pill above the icon (shape cue) .
- Badge: count badge (3.4) at the icon's top-right.
- The tab bar hides while the on-screen keyboard is open (`visualViewport` height under 75% of the window) so it never covers an input; the action bar stays.
- At <= 500px window height (landscape phone, 400% zoom) the bar becomes part of the page flow (existing `media-short` rule).

Sticky action bar (document screens, `app-document-actions`):
- Phone: full-bleed bar directly above the tab bar (`bottom: calc(64px + inset)`), bg `surface`, 1px top `border`, padding 8px 16px, `shadow(1)`. Left: summary line (step 2 `text-muted`; when the primary is blocked, `alert` icon + the reason text). Buttons below the summary, equal width, 48px high, primary on the right. At 320px the two buttons stack when the labels would not fit (flex-wrap).
- >= 600px: the existing floating bar: radius `md`, 1px `border`, `shadow(1)`, sticky `bottom: 8px`, content on one row.
- Content gets `padding-bottom` of the combined bar heights so the last line is never hidden; focused fields scroll clear of both bars (`scroll-padding-block`, existing).

### 3.14 Empty states

- Layout: centred column, max-width 28rem, padding 32px 16px: icon 40px (`text-muted`) inside a 64px `surface-sunken` disc; title step 3 weight 500 `text`; one supporting line step 2 `text-muted` (optional); exactly one action button below. No illustration, no animation.
- Variants: first-use (primary button "New receipt"), filtered-none (quiet button "Clear filters"), role-limited (no button; one line that says why: "Your role can view receipts but not create them." with the `lock` icon), error (icon `alert` in `danger`, primary "Retry", technical detail behind a disclosure). Copy comes from `mvp-ux.md` section 6.2.
- Inside a table the state replaces the rows within the same frame (bg `surface`, 1px `border`, radius `md`).

### 3.15 Role-aware surfaces (visual rules only)

What a role sees is decided by permissions (UX file, `allowedActions`); the look rules are:
- A control the role cannot use is absent. The exception stays as in the MVP: one disabled button with a visible reason when the reason helps (Approve for the submitter).
- Read-only roles see values as plain text, not as disabled inputs. A small "View only" chip (neutral: `surface-strong` bg, `text` text, `lock` icon) may sit in the page header next to the status chip; no tinted banner.
- Home: tile order follows the role's primary job; the one primary action is the only `accent`-filled element on the page. Values the role may not see leave no gap, no `0.00`, no empty column (grid reflows).
- The current role and warehouse are shown as text in the account menu and the top bar title context, never as a colour theme per role (colour is reserved for status).

## 4. Motion tokens

Principle: motion confirms that something happened (a press, an item appearing or leaving), is short, comes from the element's own position, and can be removed with no loss of information.

| Token | Value | Use |
|---|---|---|
| `duration('fast')` | 120ms | exit of toast/dialog (opacity), press release |
| `duration('press')` (new) | 80ms | button press scale |
| `duration('base')` | 200ms | toast and dialog enter, banner appear |
| `duration('slow')` (new) | 280ms | bottom sheet enter only |
| `duration('loop')` (new) | 1400ms | skeleton shimmer period (other loops: bar 1200ms, spinner 800ms) |
| `easing('standard')` (replaces `smooth`; same curve) | `cubic-bezier(0.4, 0, 0.2, 1)` | moves within the screen |
| `easing('enter')` (new) | `cubic-bezier(0, 0, 0.2, 1)` | things arriving (decelerate) |
| `easing('exit')` (new) | `cubic-bezier(0.4, 0, 1, 1)` | things leaving (accelerate) |
| `easing('linear')` (new) | `linear` | progress bar, spinner |

Allowed to animate: `opacity` and `transform` (translate, scale, rotate) only. Both run on the compositor, never cause layout, and never change contrast of text at rest.

What animates (the complete list):
1. Button press: `transform: scale(0.98)`, 80ms.
2. Toast, dialog, sheet, command palette enter/exit: opacity + 8px translate (sheet: full-height translate), per section 3.
3. Success state of a button: check icon `opacity` 0 to 1 and `scale(0.8)` to 1, 160ms.
4. Highlight of a row that just changed (saved, new): an overlay pseudo-element in `accent-soft`, `opacity` 1 to 0 over 1200ms.
5. Skeleton shimmer, top progress bar, spinner (loading indicators; all three have a reduce-motion form).
6. Segmented control thumb and tab indicator movement: `transform: translateX` 160ms `standard`.

Must not animate:
- Anything that changes layout or size (`width`, `height`, `top/left`, `margin`, `padding`), so no accordion height tween: disclosures open instantly.
- `color`, `background-color`, `border-color`, `box-shadow`, `filter`, `backdrop-filter`, `background-position` and gradient positions. Hover, focus, selected, error and disabled changes are instant. (This removes today's `transition: background-color` on the button and the `background-position` animations of the two progress bars. Open question Q3 asks whether a 120ms hover colour fade is wanted.)
- No page or route transition, no list reorder animation, no number count-up, no parallax, no bounce/spring/overshoot easing, no looping decoration, no attention-grabbing pulse, no animation that starts without a user action or a loading state.
- Nothing animates for longer than 280ms except the indefinite loading loops.

Reduce-motion (`@media (prefers-reduced-motion: reduce)`; the in-app setting, if the product owner wants one, sets `<html data-motion="reduced">` with the same rules):
- No transforms animate: translate, scale and rotate transitions are removed (state changes are instant). Opacity fades stay but are capped at `fast` (120ms) because a fade is not motion.
- All infinite loops stop: skeleton shimmer off (static shapes), progress bar static, spinner replaced by `clock` icon + "Working...".
- The row highlight shows as a static `accent-soft` tint for 1.2s, then is removed (no fade).
- Fix needed in the global rule (`globals/_base.scss`): it sets `animation-duration: 0.01ms !important` but not `animation-iteration-count`, so an infinite animation still runs (as rapid flicker). The new rule sets `animation: none !important` on the named loops and `animation-iteration-count: 1 !important`, `transition-duration: 0.01ms !important` as the catch-all. The two `!important` exceptions already carry a stylelint disable comment; the block stays the only place.
- Test: Playwright run with `reducedMotion: 'reduce'` asserts no `animation-name` other than `none` on `.ui-skeleton*`, the progress bars and spinner.

## 5. Dark mode rules

### 5.1 How elevation reads

Light uses shadow to separate layers; dark cannot (a shadow on a dark page is invisible), so the layer is shown by a lighter surface and a visible border.

| Layer | Light | Dark |
|---|---|---|
| Page (`canvas`) | `#f4f5f7` | `#0e1217` |
| Inset (`surface-sunken`) | `#eceef2` slightly darker than canvas | `#0b0f14` darker than canvas |
| Content (`surface`) | `#ffffff` + `border` | `#151a21` + `border` |
| Floating (`surface-raised`) | `#ffffff` + `shadow(2/3)` + `border` | `#1d232c` + `border` + `shadow(2/3)` (weak) |

Rules: never pure black (`#000`) or pure white text in dark (`canvas` `#0e1217`, `text` `#e8ecf2`, 15.85:1: strong enough, without halation); higher layers are always lighter; every surface boundary in dark keeps its 1px `border`; coloured fills are desaturated tints (the `*-soft` pairs), never saturated colours on dark; no shadow-only separation; scrims are `#000` at 60%.

### 5.2 Images, icons, charts

- Product photos and attachments are shown unmodified (colour accuracy matters to a warehouse), inside a 1px `border` frame with `radius md` on a `surface-sunken` background so white photos do not glare as bare rectangles.
- UI icons and the logo mark use `currentColor`; the app icon and `favicon.svg` ship a dark-friendly variant (`prefers-color-scheme` media query inside the SVG). No inverted raster images.
- Empty-state and decorative graphics: none (icons only), so there is nothing to recolour.
- Charts: series use the `chart-1..6` dark set (all 3:1 on `surface` and `surface-raised`), gridlines `border`, axis labels `text-muted` (step 1), tooltips on `surface-raised` with `shadow(2)`. Series are separated by marker/dash and direct labels as well as colour. A chart has a "table view" toggle (the data as a table) for accessibility.
- Form controls, scrollbars and autofill follow `color-scheme` (set on `:root` per theme; `light dark` for System) so native pieces are dark in dark mode.

### 5.3 Theme-color, system setting, manifest

- `ThemeService` keeps three modes (System, Light, Dark; key `ims.theme`). System follows `prefers-color-scheme` through CSS only, so it switches live with the OS and needs no JavaScript.
- `index.html` carries two theme-color tags, and the page also sets `color-scheme`:

```html
<meta name="color-scheme" content="light dark" />
<meta name="theme-color" content="#ffffff" media="(prefers-color-scheme: light)" />
<meta name="theme-color" content="#151a21" media="(prefers-color-scheme: dark)" />
```

  The two colours are the `surface` value of each theme (the top bar colour). When a user forces a theme that differs from the OS, `ThemeService` (and `theme-init.js`, below) sets the `content` of both tags to the forced theme's surface colour, and restores the defaults on System. The contrast script asserts that these two hex values equal `surface` in the palette, so they cannot drift.
- `manifest.webmanifest`: `theme_color` becomes `#ffffff` (the top bar; today `#1d4ed8` is the old accent, which no longer matches the bar), `background_color` becomes `#f4f5f7` (light `canvas`). A manifest cannot follow the theme, so the install splash screen is light even for dark users; it is brief and the CSS takes over at first paint (open question Q5).
- Respect the setting everywhere: System is the default for new users; the choice is per device, not per account.

### 5.4 No flash of the wrong theme

Problem today: the stored mode is applied by `ThemeService` after Angular boots, so a user who forced Dark on a Light OS (or the reverse) sees a flash of the wrong palette, and density would flash too. Fix without breaking the strict CSP (`script-src 'self'`, no inline script):
1. Put a tiny classic script `assets/theme-init.js` (about 400 bytes) in `<head>` before the stylesheet link, as `<script src="assets/theme-init.js"></script>` (blocking on purpose; not `defer`, not an Angular `scripts` entry, which is deferred). It reads `ims.theme` and `ims.density` from `localStorage` inside a `try`, sets `data-theme` and `data-density` on `<html>`, and fixes the two theme-color tags. It is same-origin, so the CSP allows it, and it is listed in `ngsw-config.json` assets so it is available offline.
2. `System` needs no script: `:root { color-scheme: light dark }` plus the `prefers-color-scheme` rule in `_themes.scss` paints the right palette from the first frame; `<meta name="color-scheme" content="light dark">` makes the browser's default canvas dark before CSS arrives.
3. `html { background: color('canvas') }` in the first stylesheet (render-blocking `<link>`), so no white flash on dark.
4. `ThemeService` and a new `DensityService` keep the same logic for runtime changes and read the same storage keys; the script only runs the first paint earlier.
5. Test: Playwright with `colorScheme: 'dark'` and no stored mode: the `canvas` background is `rgb(14, 18, 23)` on the first paint (screenshot at `domcontentloaded`); with `ims.theme=dark` stored and OS light: the same.

## 6. Migration plan

### 6.1 Principles

- The aliases make it a drop-in: step 1 swaps the values and keeps every old token name working, so the whole app is recoloured in one small diff and no component has to change to ship it. Renames come later, mechanically.
- Every step is shippable alone and gates on: `npm run contrast` (extended in step 1), `npm run lint:styles` (stylelint), `npm run lint`, `npm test`, and the Playwright accessibility scenario. A step that fails any gate is not merged.
- Each step stays under about 1,200 changed lines of frontend code (estimates below; the whole SCSS base is about 2,900 lines, so even the widest codemod is small).
- Stylelint: the new files follow `stylelint-config-standard-scss` as configured (`.stylelintrc.json`): `rgb(0 0 0 / 40%)` notation, no `!important` except the commented reduce-motion block, `@media (pointer: fine) and (min-width: 600px)` is plain media syntax, no new unknown pseudo-classes. Hex values exist only in `_palette.var.scss` (stylelint `color-no-hex` is turned on in step 1 for every other SCSS file, so a hard-coded colour fails the gate).

### 6.2 Token mapping (current to new)

Colour (CSS custom property `--ims-color-<name>`, SCSS `color('<name>')`)

| Current | New | Notes |
|---|---|---|
| `bg` | `canvas` | |
| `surface` | `surface` | |
| (none) | `surface-raised` | used by dialog, toast, menus, palette |
| `surface-2` | `surface-sunken` | |
| `surface-3` | `surface-strong` | |
| `skeleton` | `surface-strong` | |
| `text`, `text-muted`, `border`, `border-strong` | same names, new values | |
| `primary` / `primary-hover` / `on-primary` | `accent` / `accent-hover` / `on-accent` | `accent-pressed` is new |
| `primary-soft` / `on-primary-soft` | `accent-soft` / `on-accent-soft` | |
| `danger`, `danger-hover`, `on-danger`, `danger-soft`, `on-danger-soft` | same | `danger-pressed` is new |
| `warning-border` | `warning` | now also valid as text |
| `warning-soft`, `on-warning-soft`, `success-soft`, `on-success-soft`, `info-soft`, `on-info-soft` | same | `success`, `on-success`, `warning`, `on-warning`, `info`, `on-info` are new |
| `focus`, `overlay` | same | |
| `status-<s>` / `on-status-<s>` | kept as aliases to the role pairs in 2.2 | removed in step 10 once call sites use the roles |
| (none) | `chart-1` to `chart-6` | |

Non-colour

| Current | New |
|---|---|
| `shadow(1)`, `shadow(2)` | `shadow(1..3)` as theme custom properties; dialog uses 3, toast 2, editor bar 1 |
| `border-radius('md')` 10px / `('lg')` 16px | 8px / 12px, plus `('sheet')` 16px |
| `border-width('focus')` 3px | split into `('ring')` 2px (focus mixin) and `('edge')` 3px (banner, toast, line, selection edges); `('focus')` kept as an alias of `('edge')` until step 10 |
| `font-weight('bold')` 650 | 600 (name `'semibold'`; `'bold'` kept as alias) |
| `$line-height` 1.45 | 1.5 body; per-step line heights |
| `height('touch')` 3rem | `var(--ims-control-height)` |
| `easing('smooth')` | `easing('standard')` (same curve); `smooth` kept as alias |
| `duration('fast')` 120ms, `('base')` 200ms | unchanged; `press`, `slow`, `loop` added |
| `space(0..9)` | unchanged; `space(1.5)` added |

### 6.3 Steps

| # | Step | Content | Files (main) | Est. changed lines | Gates and risk |
|---|---|---|---|---|---|
| 1 | Palette and aliases | New `$light`/`$dark` maps with new names and values (2.2); `$aliases` map of old to new names; `color()` resolves an alias and returns the canonical variable; `status-*` kept as literal copies; `check-contrast.mjs` replaced by the 81-pair list of Appendix A (reads the same maps, also asserts the status aliases equal their roles and the two theme-color hex values in `index.html`); `color-no-hex` stylelint rule outside the palette file. No component edited. | `_palette.var.scss`, `_color.var.scss`, `scripts/check-contrast.mjs`, `.stylelintrc.json` | 250 | `npm run contrast` passes on the new pairs. Whole app recolours at once: review in light, dark and both densities by screenshot. Rollback = revert one file set |
| 2 | Shape, shadow, type foundations | Radius values, `ring`/`edge` border widths, theme-aware `shadow(1..3)` emitted in `_themes.scss`, `semibold`, body line height 1.5, per-step line heights, `numeric` mixin and base rule (tabular numerals on table, number inputs, stat values), `html` background and `color-scheme: light dark`, space 1.5, `ring` in `focus-ring` mixin. | `_shape.var`, `_font.var`, `_font-size.var`, `_space.var`, `_themes.scss`, `_base.scss`, `_a11y.mixin` | 200 | Layout shift check on 360px and 1280px screenshots (line height and radius change); stylelint passes |
| 3 | Motion tokens and reduce-motion | `duration`/`easing` additions; reduce-motion block rewritten (3.9 and section 4); skeleton gets shimmer pseudo-element; progress bars (`ui-table-progress`, `shell-progress`) rewritten with `transform`; spinner reduce-motion form; button colour transition removed, pressed scale added. | `_layout.var`, `_base.scss`, skeleton, spinner, table, shell, button scss | 280 | Playwright `reducedMotion` check added to the a11y scenario; no animation on `background-position` or `background-color` remains (grep gate) |
| 4 | Density tokens | `--ims-control-height`, `--ims-row-height`, `--ims-cell-pad-y`, `--ims-card-pad`, `--ims-stack-gap` emitted (comfortable default; compact inside `@media (pointer: fine) and (min-width: 600px)`); `height('touch')` and table/line-editor/card/dialog/nav padding switched to them. Comfortable values equal today's, so the diff is visually neutral. | `_layout.var`, `_themes.scss`, button, input, select, table, card, dialog, shell, line-editor scss | 350 | Screenshot diff must be zero in comfortable (proves neutrality) |
| 5 | Theme and density services, anti-flash, PWA colours | `DensityService` (mirrors `ThemeService`, key `ims.density`); `assets/theme-init.js` and its `<script>` tag; two `theme-color` tags + `color-scheme` meta; `ThemeService` rewrites theme-color on forced mode; manifest `theme_color`, `background_color`; `ngsw-config.json` asset entry; theme switch (3.12) and density toggle (3.11) in the account menu; i18n keys; unit tests. | `libs/core/theme`, `apps/main/src/index.html`, manifest, ngsw-config, account menu, `libs/core/i18n` messages | 450 | Playwright first-paint test (5.4); CSP unchanged (script is same-origin); service-worker precache includes the script |
| 6 | Forms and buttons | Button variants and states (3.1, success state, danger outline); form field (asterisk `aria-hidden`, legend component `app-form-legend`, error with icon); input/select/textarea/checkbox/radio states (3.2); `form.requiredLegend` message; replace the "(required)" text; unit tests on required marker and legend. | button, form-field, input, select, textarea, checkbox, radio, new legend, forms in containers (legend line) | 600 | Axe passes; `required`/`aria-required` present on every required control (unit test) |
| 7 | Tables, lists, chips | Table header/row/selection/sticky/compact (3.3); phone card; status chip on role aliases, flags, Low chip, count badge (3.4); sentence-case header; row highlight; tests for chip icon + text. | table, pager, status-chip, tabs, product/stock/document lists, stat-card | 650 | Chip contrast already in step 1 gate; 320px and 400% zoom screenshots of the lists |
| 8 | Overlays and feedback | Dialog + bottom sheet (3.6), toast + banner (3.7), card variants (3.5), empty state (3.14), skeleton shapes (3.8), `surface-raised` use, scrim levels, motion classes. | dialog, confirm-dialog, toast, toast-host, banner, card, empty-state, skeleton scss/html | 600 | Focus trap and Esc unchanged (existing tests); sheet at 320px screenshot |
| 9 | Shell and icons | Tab bar and action bar (3.13), nav active indicator, badges, `--ims-bottom-offset`, icon component (non-scaling stroke, size set) and the five new icons. | shell scss/html, `icon.paths.ts`, icon component, document-editor bar scss | 450 | Tab bar at 320px (5 cells), keyboard-open hiding behaviour, `media-short` still flows |
| 10 | Command palette | `app-command-palette` (3.10) in `shared/ui` styled to spec; `<kbd>` component; trigger in the top bar; wiring and behaviour from the UX file. Split in two PRs if over budget: (a) visual component with fake data and stories, (b) wiring. | new shared/ui component, shell, core/navigation | 900 (or 2 x 450) | Keyboard-only Playwright scenario; palette contrast uses rows already in step 1 |
| 11 | Clean-up | Mechanical rename of call sites to the new names (`primary` to `accent`, `bg` to `canvas`, `surface-2` to `surface-sunken`, `surface-3` to `surface-strong`, `status-*` to the role pairs, `warning-border` to `warning`, `border-width('focus')` to `ring`/`edge`); delete the alias map and the `status-*` copies; add a stylelint `declaration-property-value-disallowed-list` entry that rejects the old names; update `web/docs/conventions/scss-design.md` and `docs/architecture/frontend.md` (theme row). | all `*.scss`, `_color.var`, palette, docs | 300 | Compile is the test: removing the aliases makes `color()` throw on any missed name; contrast and stylelint stay green |

Total about 5,000 changed lines over 11 PRs; order matters only for 1 before everything, 4 before 6 to 9, 5 before 10. Steps 6 to 9 can run in parallel by different people once 1 to 4 are merged (they touch different component folders).

How the two gates stay green at every step:
- `check-contrast.mjs` reads the maps in the palette file, so it always tests what ships. From step 1 it exits non-zero if any of the 81 pairs fails, if a `status-*` alias differs from its role, or if the `index.html`/manifest colours differ from the palette. A new token enters the palette only together with the pairs that use it.
- Stylelint: no step adds a rule that the existing code breaks (the `color-no-hex` rule is added in step 1 when the only hex values left are in the palette; the disallowed-names rule is added in step 11 after the rename). Each step runs `npm run lint:styles` before the PR.

## 7. Open questions for the product owner

Defaults in brackets are what this spec assumes.

| # | Question |
|---|---|
| Q1 | Accent colour. The refresh keeps a blue accent (`#2447c4`, close to today's `#1d4ed8` and the PWA icon). Is there a brand colour or logo colour to adopt instead? [Keep blue; a re-brand is a one-file change.] |
| Q2 | Typography. System fonts (zero bytes, works offline, covers Chinese) or one self-hosted Latin variable font for a uniform brand look on every device (+60 to 110 KB precached, Chinese still falls back to the system font)? [System fonts.] |
| Q3 | Motion strictness. "Only opacity and transform" means hover and selection colours change instantly. Allow a 120ms colour fade on hover for buttons and rows (paint-only, still cheap)? [No; instant.] |
| Q4 | Density default and scope. Comfortable for everyone, or compact by default for Supervisor/Owner/Admin on desktop? Remembered per device (as theme) or per account? [Comfortable for everyone; per device.] |
| Q5 | Install splash screen. The manifest cannot follow dark mode, so a dark-mode user sees a light splash at launch. Accept it, or choose a mid-tone splash that suits both? [Accept light.] |
| Q6 | Tables. OK to drop uppercase table headers (sentence case) and zebra stripes? [Yes, dividers only.] |
| Q7 | Required fields. Confirm the red asterisk + "* means required" replaces the "(required)" text of the MVP design on every form, and that the legend is hidden from screen readers because each control already says "required". [Yes.] |
| Q8 | Reduce-motion. OS setting only, or also an in-app switch in the account menu (for shared PCs where the OS setting is locked)? [OS setting only.] |
| Q9 | Currency. Show `RM` as a muted prefix on every amount, or only in the column header (all amounts are one currency)? [Column header or label; prefix only in free-standing amounts.] |
| Q10 | Command palette on phones. Offer it (search icon in the top bar, full-height sheet) or keep it a desktop feature? [Offer it; it doubles as global search.] |

## Appendix A - contrast script

Save as `contrast-2026.mjs` and run `node contrast-2026.mjs` (failures only) or `--all` (every pair). It is a drop-in for step 1: the two maps move to `_palette.var.scss` (the script then parses them like the current `check-contrast.mjs` does), and the `pairs` list stays. Same formula as the current script (relative luminance, 4.5 text, 3 UI).

```js
const light = {
  canvas: '#f4f5f7', surface: '#ffffff', 'surface-raised': '#ffffff', 'surface-sunken': '#eceef2', 'surface-strong': '#e0e4ea',
  border: '#d5d9e0', 'border-strong': '#6b7482', text: '#151a21', 'text-muted': '#4a5361',
  accent: '#2447c4', 'accent-hover': '#1c37a0', 'accent-pressed': '#162c84', 'on-accent': '#ffffff',
  'accent-soft': '#e4eaff', 'on-accent-soft': '#1b2f86',
  success: '#17692f', 'on-success': '#ffffff', 'success-soft': '#e1f4e7', 'on-success-soft': '#124d24',
  warning: '#8a4a00', 'on-warning': '#ffffff', 'warning-soft': '#fdf0d0', 'on-warning-soft': '#5c3000',
  danger: '#b3261e', 'danger-hover': '#8f1d17', 'danger-pressed': '#751712', 'on-danger': '#ffffff',
  'danger-soft': '#fdebe9', 'on-danger-soft': '#7a1a14',
  info: '#0d6a85', 'on-info': '#ffffff', 'info-soft': '#def2f8', 'on-info-soft': '#0a4a5e',
  focus: '#2447c4',
  'chart-1': '#2a5bd7', 'chart-2': '#c2410c', 'chart-3': '#0e7c5f', 'chart-4': '#8a3fb8', 'chart-5': '#9a6200', 'chart-6': '#5b6472'
};
const dark = {
  canvas: '#0e1217', surface: '#151a21', 'surface-raised': '#1d232c', 'surface-sunken': '#0b0f14', 'surface-strong': '#2a323e',
  border: '#2a313c', 'border-strong': '#8590a0', text: '#e8ecf2', 'text-muted': '#a9b3c1',
  accent: '#8fb3ff', 'accent-hover': '#abc6ff', 'accent-pressed': '#7aa0f5', 'on-accent': '#0b1530',
  'accent-soft': '#1c2b52', 'on-accent-soft': '#d4e1ff',
  success: '#6fd195', 'on-success': '#07210f', 'success-soft': '#112c1b', 'on-success-soft': '#bfeacb',
  warning: '#f2b04a', 'on-warning': '#2b1800', 'warning-soft': '#35260a', 'on-warning-soft': '#ffdf9f',
  danger: '#ff8f86', 'danger-hover': '#ffb1aa', 'danger-pressed': '#f57268', 'on-danger': '#2a0805',
  'danger-soft': '#3e1613', 'on-danger-soft': '#ffd1cc',
  info: '#5fc5e0', 'on-info': '#04222b', 'info-soft': '#0f2d37', 'on-info-soft': '#bfeaf5',
  focus: '#9bbcff',
  'chart-1': '#7aa2ff', 'chart-2': '#f08a54', 'chart-3': '#4fc9a4', 'chart-4': '#c79bf0', 'chart-5': '#e0b34a', 'chart-6': '#9aa5b5'
};

function luminance(hex) {
  const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map(c => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
function ratio(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const pairs = [];
const T = 4.5, U = 3;
const bgAll = ['canvas', 'surface', 'surface-raised', 'surface-sunken', 'surface-strong'];
const add = (fg, bgs, min, kind) => bgs.forEach(bg => pairs.push([fg, bg, min, kind]));

add('text', [...bgAll, 'accent-soft'], T, 'text');
add('text-muted', [...bgAll, 'accent-soft'], T, 'text');
add('accent', ['canvas', 'surface', 'surface-raised', 'surface-sunken'], T, 'text (link)');
add('on-accent-soft', ['accent-soft'], T, 'text');
add('on-accent', ['accent', 'accent-hover', 'accent-pressed'], T, 'text');
for (const r of ['success', 'warning', 'danger', 'info']) {
  add(r, ['canvas', 'surface', 'surface-raised', 'surface-sunken'], T, 'text');
  add(`on-${r}`, [r], T, 'text');
  add(`on-${r}-soft`, [`${r}-soft`], T, 'text');
  add(r, [`${r}-soft`], U, 'icon / edge');
}
add('on-danger', ['danger-hover', 'danger-pressed'], T, 'text');
add('accent', ['accent-soft'], U, 'icon / edge');
add('text-muted', ['surface-strong'], T, 'text (disabled button)');
add('border-strong', ['canvas', 'surface', 'surface-raised'], U, 'control boundary');
add('accent', ['canvas', 'surface', 'surface-raised'], U, 'indicator / fill');
add('focus', [...bgAll, 'accent-soft', 'success-soft', 'warning-soft', 'danger-soft', 'info-soft'], U, 'focus ring');
add('danger', ['surface'], U, 'invalid border');
for (let i = 1; i <= 6; i++) add(`chart-${i}`, ['surface', 'surface-raised'], U, 'chart series');

let fails = 0;
for (const [name, p] of [['light', light], ['dark', dark]]) {
  for (const [fg, bg, min, kind] of pairs) {
    const v = ratio(p[fg], p[bg]);
    const ok = v >= min;
    if (!ok) fails++;
    if (!ok || process.argv.includes('--all'))
      console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}: ${fg} on ${bg} = ${v.toFixed(2)} (need ${min}) [${kind}]`);
  }
}
console.log(`${pairs.length * 2} checks, ${fails} failing`);
process.exit(fails ? 1 : 0);
```

Last run (2026-10-11, Node): `162 checks, 0 failing`.
