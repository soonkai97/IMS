# IMS 2026 design refresh - sign-in page, visual specification and logo

Author: fe-designer. Date: 2026-10-11. Status: decided. Ready for fe-architect (slice R25 contract: tokens, `app-logo`, menu look) and fe-framework (build).
Layout, copy and behaviour are owned by `docs/design/2026-refresh-login.md` (ui-ux-designer, "the UX file"). This file decides how it looks. Where this file departs from the UX file the departure is listed in section 0.2 and recorded in `docs/team/HANDOFFS.md`. Tokens, type scale and states come from `docs/design/2026-refresh-visual.md` ("the visual spec"); section numbers below refer to it as `V x.y`.

Rules that apply to every line below:
- Colours are tokens from the visual spec (name, then light and dark behaviour). No raw hex outside the SVG files in `docs/design/assets/`; the hex values those files use are listed in section 1.3.
- No gradient anywhere. The only decoration is one flat fill and one low-contrast line pattern on the brand panel (section 4), never under a control.
- Every pair is WCAG 2.2 AA in light and dark (text 4.5, graphics and control boundaries 3). Section 12 lists the computed ratios (same formula as `contrast-2026.mjs`).
- Targets are 44px or more on phones, nothing breaks at 320px width or 400% zoom.

## 0. Decisions

### 0.1 What I decided

| # | Decision | Reason |
|---|---|---|
| 1 | Symbol: three cartons stacked on a shelf plank, knocked out of a rounded square tile. One colour. File set in section 1. | Says "stock on a shelf" at 16px and at 200px, is not a hexagon cube (the generic box glyph every stock tool uses), has no outline strokes so it stays solid at 16px, and the tile needs no second colour: the cartons are holes that show the surface behind. |
| 2 | Brand panel, light: solid `accent` with `on-accent` text. Dark: `accent-soft` with `on-accent-soft` text (the UX file's choice). Both through a new `brand` alias pair (section 2). | I rendered the UX file's pale `accent-soft` panel in light: it is calm but only 1.2:1 against the white form column, so the page still reads as "white page with a light stripe". Solid accent gives a clear two-region page and the product its colour; in dark a solid light-blue slab would glare, so dark stays a deep navy tint. |
| 3 | A faint shelf-and-carton line pattern on the panel at 6% opacity (inline SVG, token colour). | The owner called the page bare. The pattern is 1.1 to 1.2:1 against the panel: it adds texture without competing with the text, and the text is re-checked on the blended colour (section 12). |
| 4 | In the app the lockup is the inline symbol plus live text from `app.name` (UX decision 2). The drawn wordmark (`logo-wordmark*.svg`, text as paths) is for static places only: README, export headers, emails, social images, install splash. | Keeps the product name one catalog token and keeps zoom and translation working. |
| 5 | Header buttons are 44px high at every width; the Sign in button is 56px on phone and 48px on desktop; fields are 48px. | Gloves and fat fingers; one number instead of a 40/44 split. |
| 6 | Warning banners use the `clock` icon, danger banners the `alert` icon, session-expired is a warning banner (UX 4.1). | Colour-blind users can tell "your input or account" from "try later" by shape. |
| 7 | No entrance animation; the banner fades in (opacity only, 200ms); menus fade (120ms); the phone sheet slides (280ms). Everything is instant under reduce-motion. | UX 5.4, V section 4, plan ruling C2 (no pressed scale, instant hover colour). |

### 0.2 Departures from the UX file (all recorded in HANDOFFS)

| UX file | This file | Why | Who decides |
|---|---|---|---|
| 1.5 item 2, 1.5 item 4: brand panel `accent-soft`, "no pattern" | Light: `accent`; dark: `accent-soft`; pattern at 6% | UX 9 allows fe-designer to add a `brand` pair if the pale panel is too weak; I judged it too weak by screenshot. The pattern is an addition the product owner asked for in the brief to me. | fe-designer (decided); ui-ux-designer may object in HANDOFFS |
| 2.2 symbol: isometric cube, hexagon outline, strokes, files `logo-tile.svg` and `logo-glyph.svg` | Stacked cartons on a shelf, filled, files `logo-mark*.svg` (section 1.1) | The owner's brief for the logo asks for stock, shelves or packages, a clean mark that works at 16px, no trademarked shape. A thin-stroke cube at 16px is a hexagon smudge. `logo-glyph.svg` maps to `logo-mark-mono.svg`; `logo-tile.svg` is replaced by the mark itself (the tile is part of it). | fe-designer (decided) |
| 2.2 "tile uses CSS variable, glyph strokes `currentColor`" | The mark is one shape: fill is `currentColor` or a token on the inline SVG | Same result with one paint, not two. | none needed |
| 1.4 wireframe: tagline vertically centred in panel | Same | no departure | - |

## 1. Logo

### 1.1 Concept and construction

A stack of three cartons (two below, one on top, the top one bridging the gap) on a shelf plank, all knocked out of a rounded square tile. Read as: stock, a shelf, packages. It is a geometric construction on a 64 unit grid, every edge on a multiple of 4 units, so at 16px (0.25 px per unit) the cartons are 4x4 pixels, the gaps and the plank edge fall on whole pixels and nothing blurs.

| Part | Geometry (units, viewBox 0 0 64 64) |
|---|---|
| Tile | 0,0 to 64,64, corner radius 16 |
| Top carton | x 24..40, y 8..24 |
| Lower cartons | x 12..28 and 36..52, y 28..44 |
| Shelf plank | x 8..56, y 48..56 |
| Carton and plank corner radius | 2 |
| Gaps | 4 between rows, 8 between lower cartons; margins 8 to the tile edge, top and bottom |
| Paint | one path, `fill-rule="evenodd"`: the tile is filled, the cartons and the plank are holes |

Wordmark (file `logo-wordmark*.svg`, viewBox 0 0 170 64): the tile as above, then I, M, S drawn as paths, cap height 36 (y 14..50), stem 8, letter gaps about 10, 16 units between tile and "I". Letters are geometric constructions (stems, a V, two elliptical bowls), not a font outline, so there is no font licence or font dependency. Minimum height of the wordmark: 24px; below that use the mark alone.

### 1.2 Files (all in `docs/design/assets/`)

| File | Fill | Use |
|---|---|---|
| `logo-mark.svg` | `#2447c4` | symbol, light surfaces, static places |
| `logo-mark-dark.svg` | `#8fb3ff` | symbol, dark surfaces |
| `logo-mark-reversed.svg` | `#ffffff` | symbol on the light-theme brand panel colour, static places only |
| `logo-mark-mono.svg` | `currentColor` | forced colours, print, and the source of the inline SVG in the app (the app sets `color`) |
| `logo-mark-adaptive.svg` | `#2447c4`, `#8fb3ff` inside `prefers-color-scheme: dark` | `favicon.svg` (replaces today's file) |
| `logo-wordmark.svg`, `-dark.svg`, `-reversed.svg`, `-mono.svg` | as above | symbol plus "IMS" as paths; static places only |
| `panel-pattern.svg` | `#000` strokes (a shape to mask with, or the source of the inline pattern in 4.3) | brand panel texture |

All files carry `role="img"`, a `<title>` and `aria-label="IMS"`. In the app the mark is decorative: use the inline SVG with `aria-hidden="true"` and no title (the live text next to it is the name). Colour never travels with the file inside the app: the inline mark's `color` is set by a token (table below).

| Where | Mark `color` | Name text |
|---|---|---|
| Brand panel (desktop) | `on-brand` | `on-brand` |
| Header row below 1024px | `accent` | `text` |
| Forced colours | system `CanvasText` (automatic through `currentColor`) | system |
| Print | black through the mono file | black |

Do not: stretch, add a shadow or outline, recolour outside the table, put the mark on a busy background, use the light mark on a dark surface (2.13:1 on a dark browser tab strip; that is why the favicon is adaptive), place text over the pattern area that is not on the panel.

### 1.3 Verification and contrast of the mark

Rendered in the Playwright Chromium headless shell at 16, 24, 32, 64 and 200px (mark) and 24 to 200px high (wordmark), on white, `canvas`, dark `surface`, the solid light panel colour and the dark panel colour; looked at in screenshots kept in the scratchpad (not in the repo). All files render, the cartons and the shelf are distinct at 16px, the three letters are clean at 24px high and above. The mono file renders black as an `<img>` and takes `color` when inline. The maskable composition (section 1.4) was drawn and checked against the safe circle.

Hex values the SVG files use, and graphical-object contrast (needs 3:1):

| Mark colour | on | Ratio | Result |
|---|---|---|---|
| `#2447c4` (`accent`, light) | `surface` `#ffffff` | 7.57 | pass |
| `#2447c4` | `canvas` `#f4f5f7` | 6.94 | pass |
| `#2447c4` | `surface-sunken` `#eceef2` | 6.52 | pass |
| `#2447c4` | Chrome light tab `#dee1e6` | 5.78 | pass |
| `#8fb3ff` (`accent`, dark) | `surface` `#151a21` | 8.37 | pass |
| `#8fb3ff` | `canvas` `#0e1217` | 9.00 | pass |
| `#8fb3ff` | `surface-raised` `#1d232c` | 7.57 | pass |
| `#8fb3ff` | `accent-soft` `#1c2b52` | 6.63 | pass |
| `#8fb3ff` | Chrome dark tabs `#202124` / `#35363a` | 7.71 / 5.78 | pass |
| `#ffffff` (reversed) | `accent` `#2447c4` | 7.57 | pass |
| `#2447c4` | dark tab `#202124` (why a dark file exists) | 2.13 | fails, so never used on dark |

The cartons are holes: their contrast with the tile equals the rows above (the surface shows through). The hex values are `accent` and `on-accent` copied from the palette; `check-contrast.mjs` (R01) should assert that the three hex values in the SVG files equal `accent` light, `accent` dark and `on-accent` light.

### 1.4 PWA icon source guidance (notes, not code)

Source of truth: `logo-mark.svg` geometry. Export with any SVG renderer (Inkscape, resvg, sharp); do not redraw.

| Icon | Size | Construction |
|---|---|---|
| `any` (manifest purpose any), 192 and 512 | 192, 512 | Tile filled `accent` (light value `#2447c4`), cartons and plank filled `#ffffff` (opaque, not holes, because launchers draw it on their own backgrounds), scaled so the tile is 464 of 512 (24px transparent padding, scale 7.25). Rounded corners stay (radius 16 of 64). |
| `maskable`, 512 (and 192) | 512 | Full-bleed square filled `accent`, no tile, no rounded corners (the OS masks it). Glyph (cartons plus plank, bounding box 8..56) in `#ffffff`, centred, scale 5.5 to 5.75 (264 to 276px). The Android safe zone is the centred circle of 40% radius (204.8px at 512): the farthest glyph corner is 187 to 195px from the centre, inside it with 10px spare. Do not scale above 5.75. |
| `apple-touch-icon` | 180 | Same as maskable, but no transparency anywhere (iOS paints transparent areas black) and the OS rounds the corners itself. |
| Favicon | SVG plus 32 and 16 | `logo-mark-adaptive.svg` as `favicon.svg` (adapts to the tab strip colour). Fallback `favicon-32.png` / `.ico` from the `any` construction, not from the knockout (the knockout shows the tab colour through the cartons). |
| Install splash | - | Manifest `background_color` is the `canvas` light value and `theme_color` the `surface` light value (V 5.3); the icon sits on it. |
| Contrast | - | white on `accent` 7.57; the maskable background `accent` against a white or dark launcher wallpaper needs no ratio (it is a filled app icon), but its glyph does: 7.57. |

Replace all files in `web/libs/shared/assets/src/assets/icons/`, `favicon.svg` and `icon.svg` in the same PR (today they use the old `#1d4ed8` cube). Check each export at 48px on a white and a dark background by eye before merging.

## 2. Tokens and constants added for this page

No new hex. Five alias tokens, emitted in `globals/_themes.scss` and listed in the palette file as literal copies of the role they point to (the same pattern as the `status-*` copies, V 2.2). The contrast script asserts that each equals its role.

| Token | Light = role | Dark = role | Used for |
|---|---|---|---|
| `brand` | `accent` | `accent-soft` | brand panel background |
| `on-brand` | `on-accent` | `on-accent-soft` | text, lockup mark and name, version on the panel |
| `brand-disc` | `accent-hover` | `surface` | the 32px disc behind each check icon |
| `on-brand-disc` | `on-accent` | `accent` | the check icon |
| `brand-pattern` | `on-accent` | `accent` | pattern stroke, drawn at 6% opacity |

Pairs to add to the script (so the page stays gated): `on-brand` on `brand` (text 4.5), `on-brand-disc` on `brand-disc` (icon 3), and the blended pattern check of section 12 (text 4.5 on `brand` mixed with `brand-pattern` at 6%).

Layout constants (new): `width('auth-form')` = 28rem (UX 1.1), `width('auth-panel-min')` = 22rem. Existing: `height('touch')` 48, `height('touch-min')` 44, `height('bar')` 56, breakpoints 600 and 1024. Density never applies to this page: it forces comfortable (UX 5.2), so `--ims-control-height` is 3rem here even with a stored compact setting.

New icons to add to `icon.paths.ts` (24 grid, stroke, same style; the path strings are the drawn proposal):
- `lock`: `M6 11h12v9H6z M8.5 11V8a3.5 3.5 0 017 0v3 M12 15v2`
- `monitor`: `M3 5h18v11H3z M9 20h6 M12 16v4`
Both are already on the V 2.9 list. Existing icons used on this page: `user`, `eye`, `eye-off`, `globe`, `sun`, `moon`, `chevron-down`, `check`, `alert`, `clock`, `info`, `x`.

## 3. Page frame

| Width | Frame |
|---|---|
| 1024 and up | Grid, columns `minmax(22rem, 5fr) minmax(0, 7fr)`, `min-height: 100dvh`, rows: header (auto), main (1fr), footer (auto). The brand panel (`aside`) sits in column 1 over all rows with its own background; the header spans both columns above it (`z-index` above the aside), so the one lockup in the header lands on the panel and the two buttons on the form column. The footer is placed in column 1 on the panel. The form column background is `surface`. A 1px `border` on the panel's inline end separates the two columns. |
| 600 to 1023 | One column on `surface`, `min-height: 100dvh`. The `aside` is not rendered. Main starts at 12dvh from the top (padding, not centring, so the keyboard does not push it). |
| Under 600 | One column on `surface`. Main starts `space(3)` under the header row. |
| Height 500px or less, any width | Subtitle and tagline hidden (UX 5.2), lockup at phone size, main starts `space(2)` under the header. |

Page padding: 16px (`space(2)`) at every width under 1024; on desktop the panel uses `space(6)` (48px) and `space(8)` (64px) from 1280px; the form column uses `space(6)` inline. Each inline padding is `max(<value>, env(safe-area-inset-left or right))`, block-start `max(<value>, env(safe-area-inset-top))`, block-end `max(<value>, env(safe-area-inset-bottom))`. The `viewport-fit=cover` value is needed in `index.html` for `env()` to be non-zero (ask to fe-framework, section 13).

Form block: `width: min(100%, width('auth-form'))`, centred in its column (`margin-inline: auto`). On desktop the block is centred vertically in the space between header and footer.

## 4. Brand panel (1024 and up only)

### 4.1 Surface

Flat fill `brand`. No gradient, no image, no blur, no shadow. Pattern over it (4.3). Panel inline padding `space(6)`, from 1280px `space(8)`. Content cap: the tagline block is 28rem wide at most; on wide screens the panel just gets more empty space.

| Theme | Panel looks like |
|---|---|
| Light | Solid brand blue (`accent`), white text and icons (`on-brand`), discs one step darker (`accent-hover`), pattern in white at 6%. A confident colour field next to the white form column; the 1px `border` edge is invisible against the blue and is there for forced colours. |
| Dark | Deep navy (`accent-soft`), pale blue text (`on-accent-soft`), discs darker than the panel (`surface`) with `accent` ticks, pattern in `accent` at 6%. No bright slab. A 1px `border` between the panel and the `surface` column (the two are 1.26:1 apart, so the edge matters). |

### 4.2 Content and type

All content colours are `on-brand`; hierarchy is size and weight, not tint (there is no muted-on-accent token: white at 80% is 5.44 and at 70% 4.56 on `accent`, too close to the limit and unpredictable over the pattern).

| Element | Spec |
|---|---|
| Lockup | symbol 40px (`app-logo`, mark `color: on-brand`), gap 12px, name step 6 (32px, 600), live text from `app.name`; row min-height 44 so its centre lines up with the header buttons |
| Tagline | step 5 (24px, 1.25, 600), `on-brand`, max width 16em |
| Points | three rows, gap `space(3)`; each row: 32px disc (`brand-disc`, `border-radius: pill`) with a 20px `check` icon (`on-brand-disc`), 16px gap, text step 3 weight 500 `on-brand`. The disc plus the tick is the cue; text carries the meaning |
| Spacing | lockup at the top; tagline block (tagline, then `space(4)` gap, then the list) vertically centred in the remaining height; version at the bottom (`space(0)` from the panel padding) |
| Version | step 1 (13px, 1.4), `on-brand`, tabular numerals |

### 4.3 Pattern

An outlined shelf-and-carton texture, tile 96x96 (`panel-pattern.svg`): two horizontal shelf lines per tile, three open cartons standing on each, 2px strokes, rounded joins. It is decoration: `aria-hidden`, `pointer-events: none`, behind all content, not announced, not selectable.

Preferred build, no request and token-driven: one inline `<svg aria-hidden="true" focusable="false">` filling the panel (`position: absolute; inset: 0; width: 100%; height: 100%`) with a `<pattern width="96" height="96" patternUnits="userSpaceOnUse">` holding the same lines (`fill: none; stroke: currentColor; stroke-width: 2`), a full-size `<rect fill="url(#id)">`, `color: brand-pattern`, `opacity: 0.06`. Alternative if the inline version is not wanted: a `::before` with `background: brand-pattern; opacity: 0.06; mask-image: url(panel-pattern.svg)` (the mask is an image fetch, so check it against the strict CSP `img-src 'self'` in the Playwright run).

Stroke against panel is 1.14:1 light, 1.12:1 dark: texture, never information. Text over the pattern keeps its contrast (section 12). Under `forced-colors: active` and `print` the pattern is `display: none`. It does not move or fade: no parallax, no animation.

## 5. Header row, lockup, language and appearance menus

### 5.1 Header row

| Width | Spec |
|---|---|
| Under 1024 | `<header>` is a flex row, `align-items: center`, `justify-content: space-between`, min-height 56px (phone) or 72px (600 and up, mark 40px), padding as section 3. Start: lockup (mark `accent`, name `text`; sizes 32px mark and step 5 name on phone, 40px and step 5 from 600; gap 8px phone, 12px otherwise). End: language button, appearance button, 8px apart. Tablet only: the tagline sits under the lockup in `text-muted`, step 3. |
| 1024 and up | the row spans both columns; the lockup is on the panel (`on-brand`), buttons on the form column at its inline end; row min-height 44 inside the `space(6)` padding |

At 320 to 359px the row wraps: lockup first, the two buttons on the next line, still end-aligned; the language button shows the short code. No horizontal scroll.

### 5.2 Language button

Quiet button (V 3.1): height 44, padding-inline `space(1.5)`, gap `space(1)`, radius `md`, border `1px solid transparent`, text step 2 weight 500 `text`. Contents: `globe` 20px `text`, the language in its own script ("English", "中文", "Bahasa Melayu"), `chevron-down` 16px `text-muted` (marks it as a menu). Under 400px the label is the short code ("EN", "中文", "BM").

| State | Look |
|---|---|
| Rest | bg transparent |
| Hover | bg `surface-sunken` |
| Pressed | bg `surface-strong` |
| Open (`aria-expanded="true"`) | bg `surface-sunken` |
| Focus-visible | `outline: 2px solid focus; outline-offset: 2px` |

All colour changes are instant. Text on `surface-sunken` 15.05 / 16.21, on `surface-strong` 13.69 / 10.90; `text-muted` chevron on `surface-sunken` 6.69 / 9.07 (needs 3).

### 5.3 Appearance button

Icon-only, 44x44, radius `md`, same states as 5.2, icon 24px `text` (the icon is the only content, so it takes the strong text colour, not `text-muted`): `monitor` for device setting, `sun`, `moon`. Accessible name and tooltip per UX 4.7. The tooltip is a native `title` or an `app-tooltip` with `surface-raised`, `text`, 1px `border`, radius `sm`, padding `space(0)` `space(1)`, step 1; it appears after 600ms hover or on focus and is not the only name.

### 5.4 Menu (600 and up) and sheet (under 600)

| Part | Menu (popover) | Sheet |
|---|---|---|
| Container | bg `surface-raised`, 1px `border`, radius `md`, `shadow(2)`, padding `space(0)`, min-width 12rem, max-width `100vw - 32px`, anchored under the button, aligned to its inline end, flips above if no room | V 3.6 bottom sheet: `surface-raised`, `shadow(3)`, top radius 16px, handle bar, title step 4 (600) with the visible close button 44x44 (`x`), bottom padding `max(space(2), env(safe-area-inset-bottom))` |
| Row | height 44, padding-inline `space(1.5)`, gap `space(1)`, radius `sm`, text step 3 | height 48, padding-inline `space(2)`, radius 0 |
| Row content | 20px slot (the `check` when selected, empty otherwise, so labels stay aligned), label in its own language with `lang` set, optional sub-line step 1 `text-muted` (appearance: "Now: Dark") | same |
| Hover / arrow-key highlight | bg `surface-sunken` | same (touch: pressed `surface-strong`) |
| Selected (`aria-checked="true"`) | bg `accent-soft`, text `on-accent-soft`, weight 600, 3px `accent` bar on the inline start (`edge`), `check` icon `on-accent-soft` | same |
| Focus | inset ring (`outline-offset: -2px`) | same |
| Scrim | none (popover closes on outside click) | `overlay` 50% light / 60% dark, no blur |

Selected is shape plus weight plus tick, so it survives forced colours (V rule, section 3). Selected row text 9.74 / 10.56 on `accent-soft`; bar 6.31 / 6.63; unselected text on `surface-raised` 17.48 / 13.33; `text-muted` sub-line on `surface-raised` 7.77 / 7.45, on `accent-soft` 6.48 / 6.53; the menu boundary (`border`) is decorative, the shadow plus the lighter dark surface separate it, and the rows are content, not controls with their own boundary.

## 6. Form column

### 6.1 Type and spacing (4px scale)

| Element | Spec |
|---|---|
| Heading block | `h1` "Sign in" step 5 (24px, 1.25, 600) `text`; subtitle step 3 `text-muted`, `space(0)` under the h1 |
| Heading block to form | `space(3)` (UX 1.5 item 6) |
| Legend "* means required" | step 2 `text-muted`, the asterisk `danger` weight 600, first element of the form |
| Gap between legend, fields, banner and button | `space(2)` (16px) |
| Field internals | label step 2 (15px) weight 500 `text`, `space(0)` (4px) to the control; the asterisk after the label, `danger`, `aria-hidden`; hint or error `space(0)` under the control |
| Banner to button | `space(2)`; the banner is between the password field and the button (UX 4.4) |
| Access line | after the button, `space(3)` above, step 2 `text-muted`, max width 70ch |
| Slow-sign-in line | under the button, `space(1)` above, step 2 `text-muted` with the `clock` icon 16px |
| Desktop vertical rhythm | header 44, then the form block centred; footer in the panel column |

Text colours on `surface`: `text` 17.48 / 14.74, `text-muted` 7.77 / 8.24, asterisk `danger` 6.54 / 7.93.

### 6.2 Access line and footer

Access line: plain text, no link. Footer: "Version x.y.z", step 1, tabular numerals, `text-muted` on `surface` under 1024 (7.77 / 8.24), `on-brand` in the panel from 1024 (7.57 / 10.56). Under 1024 it follows the access line with `space(4)` above and the block-end padding below; in the development build the dev hint is a second line in the same style. On the phone the footer is part of the scroll, never fixed.

## 7. Field with leading icon and trailing show/hide button

Control geometry (username and password):

| Part | Spec |
|---|---|
| Height | 48px (`height('touch')`), `min-height` so text zoom wraps nothing |
| Box | bg `surface`, 1px `border-strong`, radius `md`, text step 3 (16px minimum) `text`, caret default |
| Leading icon | 20px, `user` or `lock`, `aria-hidden`, 12px (`space(1.5)`) from the inline start, vertically centred; input padding-inline-start `space(5)` (40px) so text starts 8px after the icon |
| Trailing button (password only) | 44x44 square, inside the field, vertically centred, 1px from the inner inline end; input padding-inline-end 3.5rem (56px) so text never runs under it; username padding-inline-end `space(2)` |
| Trailing icon | `eye` when hidden, `eye-off` when shown, 20px |
| Radius of the button | `sm` (6px, inside the 7px inner radius of the control) |

States (all colour changes instant; a state is never colour alone):

| State | Control box | Leading icon | Trailing button | Other |
|---|---|---|---|---|
| Rest | border `border-strong`, bg `surface` | `text-muted` | icon `text-muted`, bg transparent | placeholder none (labels always visible) |
| Hover | border `text-muted` | `text-muted` | button unchanged unless hovered itself | `cursor: text` |
| Focus (field) | border `accent` + `outline: 2px solid focus; outline-offset: 2px` | `accent` | unchanged | the ring is the cue; border colour reinforces it |
| Error | border `danger` plus 1px inset ring of the same colour (reads as 2px, no layout shift), `aria-invalid="true"`; message below: `alert` icon 16px + text step 2, both `danger` | `danger` | unchanged | the message text is the cue |
| Disabled | bg `surface-sunken`, border `border`, text `text-muted`, `cursor: not-allowed`; label stays `text` | `text-muted` | button disabled: icon `text-muted`, no hover | not used on this page by the UX file (lockout keeps fields editable), defined so the field component is complete |
| Busy (submitting) | `readonly` (UX 4.8), bg `surface-sunken`, border `border-strong`, text `text` | `text-muted` | button stays usable, no change | `aria-busy` on the form; no opacity fade |

Trailing button states (quiet icon button, `aria-pressed` for state, constant name "Show password"):

| State | Look |
|---|---|
| Rest | icon `text-muted` on transparent |
| Hover | bg `surface-sunken`, icon `text` |
| Pressed (mouse down) | bg `surface-strong`, icon `text` |
| Toggled on (`aria-pressed="true"`) | icon `eye-off`, icon `text`, plus bg `accent-soft` and icon `on-accent-soft` so the state is visible by shape and fill |
| Focus-visible | inset ring `outline: 2px solid focus; outline-offset: -2px` (the field is clipping its box), the field border does not change |
| Busy | unchanged (the toggle is allowed during the request) |
| Forced colours | the button keeps `border: 1px solid transparent` so the system draws it |

Caps Lock hint (UX 4.5): under the field, `alert` 16px and the text step 2, both `text-muted`, not red, not a banner. Autofill: on `input:-webkit-autofill` set `box-shadow: inset 0 0 0 1000px surface` and `-webkit-text-fill-color: text` using the tokens, so a browser tint never breaks the dark theme; `color-scheme` stays `light dark`.

Contrast: leading icon `text-muted` on `surface` 7.77 / 8.24 (needs 3); on `surface-sunken` 6.69 / 9.07; boundary `border-strong` on `surface` 4.72 / 5.40; hover border `text-muted` 7.77 / 8.24; focus ring on `surface` 7.57 / 9.18; error `danger` on `surface` 6.54 / 7.93; toggled icon `on-accent-soft` on `accent-soft` 9.74 / 10.56.

## 8. Primary button (Sign in)

As V 3.1, block variant: full width, radius `md`, text step 3 weight 500, icon 20px, 8px gap. Height 56px (`height('bar')`) under 600px, 48px (`height('touch')`) from 600px.

| State | Look |
|---|---|
| Rest | bg `accent`, text `on-accent`, border `accent` (7.57 / 8.64) |
| Hover | bg `accent-hover` (9.98 / 10.53) |
| Pressed | bg `accent-pressed` (12.16 / 7.02); no scale (plan C2) |
| Focus-visible | `outline: 2px solid focus; outline-offset: 2px` |
| Busy | rest colours kept, label "Sign in" kept, 20px spinner replaces the leading icon after 150ms, `aria-busy`, `aria-disabled`, clicks ignored, focus stays. Reduce-motion: spinner becomes the static `clock` icon and the visible word "Working" (V 3.9) |
| Disabled | not used on this page (the button stays enabled, UX 4.1); if ever needed: bg `surface-strong`, text `text-muted` 6.09 / 6.10, border `border` |
| Forced colours | border stays 1px, text `ButtonText` automatically |

## 9. Banners

One banner at a time. Built on `app-banner` (V 3.7): bg `<role>-soft`, text `on-<role>-soft`, 3px `edge` on the inline start in the role colour, radius `md`, padding `space(1.5)` `space(2)`, icon 20px in the role colour top-aligned with the first line (2px offset), gap `space(1)`, text step 3, no heading (one sentence, optional second line), no close button (any edit clears it).

| Kind on this page | Role | Icon | Text pair | Icon and edge pair |
|---|---|---|---|---|
| Wrong credentials, locked account | danger | `alert` | `on-danger-soft` on `danger-soft` 9.19 / 11.47 | `danger` on `danger-soft` 5.68 / 7.17 |
| Rate limit, offline, network, server, session expired | warning | `clock` | `on-warning-soft` on `warning-soft` 9.88 / 11.38 | `warning` on `warning-soft` 6.06 / 7.74 |

Placement: wrong credentials, lockout, rate limit, network and server banners sit between the password field and the button, full form width. The offline and session-expired status banners sit under the subtitle, above the legend (UX 4.4). The lockout wait ("15 minutes") is static text; no ticking countdown.

Second line (repeat hint, administrator hint): same colour as line 1, step 3, `space(0)` below it, never smaller.

Focus: the banner is not focused after a failure (UX 4.2 item 6); if a build ever focuses it, `outline: 2px solid focus; outline-offset: 2px` (6.58 / 8.30 on `danger-soft`).

Appearance: opacity 0 to 1 over 200ms (`duration('base')`, `easing('enter')`), no translation, no height tween. Removal is instant. Reduce-motion: appears instantly.

Forced colours: all four sides of the banner are `1px solid` with `border-inline-start-width: 3px` so the system draws the box; the icon and sentence carry the meaning.

## 10. Phone layout, safe areas and the on-screen keyboard

360x640 reference (heights in px):

| Block | Height |
|---|---|
| Header row | 56 (lockup 32 mark, buttons 44) |
| `space(3)` | 24 |
| `h1` | 30 (24 x 1.25) |
| Legend and gaps | about 40 |
| Two fields with labels and gaps | about 160 |
| Banner when shown (two lines) | about 72 |
| Sign in | 56 |
| Access line and footer | about 100 |

Measured on a mock at 360x640 (token values, system font): about 530px without a banner and about 600px with a two-line banner, including the block-end padding. A phone browser leaves about 560px, so the page fits without a banner and scrolls by a few lines with one, which is acceptable because nothing is fixed. (The mock put the banner above the legend for convenience; the spec places it between the password field and the button, same height.)

- Safe areas: header `padding-block-start: max(space(1), env(safe-area-inset-top))`, inline paddings `max(space(2), env(safe-area-inset-left/right))`, block end `max(space(3), env(safe-area-inset-bottom))`. In landscape on a notched phone the header buttons stay inside the notch margin.
- The status bar: the page is on `surface`, so the `theme-color` meta (the `surface` value of the theme, V 5.3) matches the top; nothing to change on the login route.
- Keyboard: `min-height: 100dvh` (never `100vh`); nothing is `position: fixed` or sticky on this page; no vertical centring below 600px, so the content does not jump when the keyboard opens. Fields and the button get `scroll-margin-block: space(6)` so the focused control scrolls clear of the keyboard. With the keyboard open the visible height is about 300px: header (56) plus h1, both fields and the button can be scrolled into view; the access line and footer stay below. Chrome on Android: set the viewport meta to include `interactive-widget=resizes-content` so `dvh` shrinks with the keyboard (iOS Safari does not resize the layout viewport; because nothing is fixed, scrolling alone is enough there).
- Height 500px or less: UX 5.2 (subtitle and tagline hidden, phone-size lockup), `h1` stays.
- 320px: the header wraps (section 5.1), buttons keep 44px, the Sign in label may wrap (`min-height`, not `height`), banner text wraps, field content width 288px.
- 400% zoom: the page behaves as 320px by about 180px high: one column, header row, `h1`, fields, button, vertical scroll only.

## 11. Motion

| Item | Motion | Reduce-motion |
|---|---|---|
| Page load | none (UX 5.4) | none |
| Banner appear | opacity 0 to 1, 200ms `enter` | instant |
| Banner remove | instant | instant |
| Language / appearance popover | opacity 0 to 1, 120ms (`duration('fast')`), no translation; close instant | instant |
| Bottom sheet (phone) | translateY(100%) to 0, 280ms `enter`; exit 200ms; scrim opacity in step (V 3.6) | instant |
| Busy spinner | rotate, 0.8s linear, replaces the leading icon | static `clock` + "Working" |
| Hover, pressed, focus, selected, error | none (instant) | none |
| Brand panel and pattern | none | none |

Only `opacity` and `transform` ever animate (V section 4). The reduced-motion rule is the global one fixed in V section 4 (no infinite animation, no transform animation). Test target: `document.getAnimations()` is empty on a loaded idle page (LG-47).

## 12. Contrast results

Computed with the visual spec formula (WCAG 2.x relative luminance). Light and dark ratios; "needs" is 4.5 for text and 3 for graphics and control boundaries. Every pair passes; nothing was changed to make a pair pass, but several alternatives were rejected on the numbers and on feel (last table).

| Pair (foreground on background) | Use | Needs | Light | Dark |
|---|---|---|---|---|
| `on-brand` on `brand` | panel text, lockup, version | 4.5 | 7.57 | 10.56 |
| `on-brand-disc` on `brand-disc` | check icon | 3 | 9.98 | 8.37 |
| `on-brand` on `brand` blended with `brand-pattern` at 6% | text over the pattern | 4.5 | 6.62 | 9.42 |
| Pattern stroke on `brand` at 6% | decoration (not required) | - | 1.14 | 1.12 |
| `accent` on `surface` | mark, link | 3 / 4.5 | 7.57 | 8.37 |
| `accent` on `canvas` / `surface-sunken` / `surface-raised` | mark | 3 | 6.94 / 6.52 / 7.57 | 9.00 / 9.21 / 7.57 |
| `accent` on `accent-soft` | mark, menu bar | 3 | 6.31 | 6.63 |
| `text` on `surface` | h1, labels, input text, name | 4.5 | 17.48 | 14.74 |
| `text-muted` on `surface` | subtitle, legend, access line, version, caps hint | 4.5 | 7.77 | 8.24 |
| `text-muted` on `surface-sunken` | busy and disabled field | 4.5 | 6.69 | 9.07 |
| `text` on `surface-sunken` / `surface-strong` | button hover, pressed | 4.5 | 15.05 / 13.69 | 16.21 / 10.90 |
| `text-muted` on `surface-sunken` | chevron, toggle icon at hover | 3 | 6.69 | 9.07 |
| `border-strong` on `surface` | field boundary | 3 | 4.72 | 5.40 |
| `text-muted` on `surface` | hover border | 3 | 7.77 | 8.24 |
| `danger` on `surface` | asterisk, error text, invalid border | 4.5 | 6.54 | 7.93 |
| `focus` on `surface` / `surface-raised` | focus ring | 3 | 7.57 / 7.57 | 9.18 / 8.30 |
| `focus` on `danger-soft` / `warning-soft` / `accent-soft` | ring inside banner or selected row | 3 | 6.58 / 6.69 / 6.31 | 8.30 / 7.70 / 7.27 |
| `on-danger-soft` on `danger-soft` | danger banner text | 4.5 | 9.19 | 11.47 |
| `danger` on `danger-soft` | danger icon and edge | 3 | 5.68 | 7.17 |
| `on-warning-soft` on `warning-soft` | warning banner text | 4.5 | 9.88 | 11.38 |
| `warning` on `warning-soft` | warning icon and edge | 3 | 6.06 | 7.74 |
| `on-accent` on `accent` / `accent-hover` / `accent-pressed` | Sign in rest, hover, pressed | 4.5 | 7.57 / 9.98 / 12.16 | 8.64 / 10.53 / 7.02 |
| `text-muted` on `surface-strong` | disabled button | 4.5 | 6.09 | 6.10 |
| `on-accent-soft` on `accent-soft` | selected menu row, toggled icon | 4.5 | 9.74 | 10.56 |
| `text` on `surface-raised` | menu row | 4.5 | 17.48 | 13.33 |
| `text-muted` on `surface-raised` | menu sub-line | 4.5 | 7.77 | 7.45 |
| `text-muted` on `accent-soft` | selected sub-line | 4.5 | 6.48 | 6.53 |
| `border` on `surface` | panel edge (decorative) | - | 1.42 | 1.33 |

Pairs that were tempting and rejected (a pair "needed a change" when I changed the design, not the colour):

| Tempting | Ratio | Instead |
|---|---|---|
| Dark panel as solid `accent` (`#8fb3ff`) with `on-accent` text | 8.64 passes, but a large bright slab in a dark UI | `accent-soft` (UX choice) |
| Light panel as pale `accent-soft` (UX choice) | text 9.74 passes; panel against the white form column 1.2:1, weak presence | solid `accent` through `brand` |
| Muted text on the solid light panel as white at 80% / 70% / 60% | 5.44 / 4.56 / 3.75 (60% fails; 70% too close once the pattern is added) | one `on-brand` colour, hierarchy by size and weight |
| Pattern at 12% | white text on the blend 5.78, passes, but the shelves compete with the text | 6% (6.62) |
| Light mark on a dark surface | 2.13 on a dark tab strip | `logo-mark-dark.svg`, adaptive favicon |
| Dark disc as `accent-soft` on the `accent-soft` panel | disc invisible | `surface` disc with `accent` tick (8.37) |

## 13. What the build needs from other roles

| To | Ask |
|---|---|
| fe-architect | slice R25: add `app-logo` (inline mark from `logo-mark-mono.svg`, `color` by token, input `size`, decorative), the `brand` alias tokens and the pattern element; a menu component (`menuitemradio`, popover and sheet looks of 5.4); `width('auth-form')`, `width('auth-panel-min')`; the two icons of section 2. |
| fe-framework | `viewport-fit=cover` and `interactive-widget=resizes-content` in `index.html`; replace `favicon.svg` with `logo-mark-adaptive.svg` and regenerate the PWA icons per 1.4; add the five `brand` pairs and the SVG-hex assertion to `check-contrast.mjs`; copy the SVGs you need into `web/libs/shared/assets/src/assets/`; the page must pass `forced-colors` and `reducedMotion` Playwright checks (LG-46, LG-47) and the screenshot gate below. |
| ui-ux-designer | accept or object to section 0.2 (panel colour and pattern; mark geometry; header button height). |
| behavior-test-engineer | nothing new; LG-01 to LG-48 hold. The panel colour is not asserted by the UX criteria. |
| fe-designer (me, wave gate) | review screenshots at 320, 360, 768, 1280 and 1920 in light and dark, plus forced colours, when R25 lands. |

Screenshots produced while designing (scratchpad only, not in the repo): logo at 16 to 200px on light, `canvas`, dark, brand blue, brand navy; a mock of the desktop page in light (with error banner) and dark, and the phone page in light and dark, built from the token values above; the pale-panel alternative. Not tested: the real Angular page (it does not exist yet), a real keyboard on a device, Windows forced-colours rendering, print.
