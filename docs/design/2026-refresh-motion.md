# IMS 2026 design refresh - motion language

Author: fe-designer. Date: 2026-10-11. Status: proposal for the product owner, ready for fe-architect (plan v2 changes, section 7) and fe-framework (build) once the owner has seen the demo.
Live demo (one file, no dependencies, light and dark, Full / Lite / Reduced / Off switch): `docs/design/assets/motion-demo.html`. Open it in a current Chrome, Edge, Safari 17.4+ or Firefox 129+.
Starting point: `docs/design/2026-refresh-visual.md` (section 4 motion tokens, 3.1 buttons, 3.8 skeletons, 3.9 progress), `docs/design/2026-refresh-ux.md` (7.1 to 7.6, 11), `docs/design/2026-refresh-plan.md` (rulings C1 to C15, N1 to N14, D8, R07, R08, R11, R13 to R15, A-slices, 4.3 budget), `docs/design/2026-refresh-login-visual.md` (section 11), and the real code (`globals/_base.scss`, button, skeleton, spinner, table, shell, dialog, tabs).
Where this file and an earlier file disagree on motion, this file wins once the owner accepts it (list in 1.2). Everything else (colour, contrast, 44px targets, 360px, density) is unchanged. This file edits no other document: the fe-architect applies section 7 to the plan.

Fixed limits the owner set (every pattern below obeys them; they are not re-argued):

| # | Limit |
|---|---|
| H1 | `prefers-reduced-motion` is honoured: every animation has a non-moving or fade-only form (section 5.1). |
| H2 | Animate `transform` and `opacity`. Two bounded exceptions, named and capped: `clip-path` reveals (paint only, small areas, one shot) and the height of one disclosure through `grid-template-rows` (one container, never inside a virtualised body). View transitions are the third tool. |
| H3 | Functional transitions 300 ms at most, entrances 400 ms at most including stagger, nothing blocks input. |
| H4 | No layout shift: every motion either does not change layout or runs inside 500 ms of the user's own input (excluded from CLS by definition) and is measured anyway. |
| H5 | No looping decoration. Loops exist only for loading, are finite where they can be, and stop after about 10 s (WCAG 2.2.2). |
| H6 | Smooth on a low-end Android phone and with 5,000 rows: nothing per-row animates except the first 8 rows on first reveal. |

## 1. Principles and what changes

### 1.1 Five principles

1. Motion explains: where something came from (a dialog grows from a spring pop, a drawer from its edge), where it went (a toast fades), that it worked (a tick), that work continues (a bar, a shimmer). It never plays for show.
2. Fast in, faster out: arrivals decelerate (200 to 280 ms), departures accelerate and are shorter (100 to 140 ms). The user's next action is never waiting on an exit.
3. Compositor only: `opacity` and `transform`. Colour, shadow and size changes are faked with a pseudo-element whose `opacity` or `transform` moves (hover fade, success fill, elevation).
4. One vocabulary: the same five tokens (duration, easing, distance, scale, stagger) everywhere; a reduced or low-end device changes the tokens, not the components.
5. Meaning never lives only in motion: every animated state also has text, an icon or a position (the word "Saved", the chip text "Posted"), so removing the motion loses nothing.

### 1.2 Earlier rulings that are superseded

Decided here; the plan keeps the others (colour, density, tokens, cost visibility) as they are.

| Earlier ruling (where) | New decision | Why |
|---|---|---|
| C1 / D8: skeleton pulses, "no gradient anywhere", no shimmer (plan 0, UX 7.1 item 5, UX 11 item 3) | **Shimmer.** A soft highlight sweeps across each skeleton shape by `transform: translateX`, 1.5 s per sweep, 6 sweeps (9 s) then it stops on the static shape. The gradient is allowed only on that decorative pseudo-element, never on text or controls. Reduced and Lite: static shapes at 0.7 opacity, no sweep. The pulse is deleted. | Shimmer reads as "alive and progressing"; it costs one composited layer per shape (at most 8 shapes on screen). Finite, so it meets WCAG 2.2.2. |
| C2 / D8: no pressed scale; `duration('press')` dropped | **Press scale 0.97**, 80 ms in, spring back in 200 ms. Large Home action buttons and cards 0.98. Not on table rows. Reduced: none. `duration('micro')` 80 ms returns. | A pressed button that does not move feels dead on a phone. Transform only, no layout, hit area unaffected (the box shrinks 0.7 px at each edge of a 48 px button). |
| C3 / D8 / visual Q3: hover and selection colours change instantly | **Hover fades in 120 ms** for buttons, quiet buttons, menu and nav items, tabs, chips that are buttons, interactive cards: an overlay pseudo-element in the hover colour fades its `opacity`. **Still instant:** focus ring (never lags a keyboard user), table rows and dense list rows (5,000 rows, fast pointer sweeps smear), selected, pressed colour, error, disabled. Answers visual open question Q3 with "yes, by overlay". | Colour itself is not animated, so the "only opacity and transform" rule holds. Hover is guarded by `@media (hover: hover)` so touch devices never see a stuck hover. |
| C7 (plan 0): dialog desktop fade 120 ms only, sheet 200/120, palette fade 120/100; "no `duration('slow')` token" | **Dialog:** spring pop, 220 ms in (opacity 200, transform 280 with overshoot), 120 ms out. **Sheet and drawer:** edge slide 280 ms in, 200 ms out, no overshoot. **Palette:** 180 ms in, 100 ms out. Scrim fades with them. `duration('slow')` = 400 ms returns for single-element entrances only (count-up, chart draw). | The owner asked for more life; edge-anchored panels never overshoot because the overshoot would open a gap at the screen edge. |
| C8: row highlight 120 / 1500 / 1000 ms | Kept for the user's own change (Add item, scan). New shorter flash for data that changed under the user (refetch): 120 / 300 / 700 ms, at most 10 rows at once, never on first load. Same colour `accent-soft`. Reduced: static tint 1.5 s then removed (unchanged). | Both are one-shot overlay opacity. |
| C13 / visual 3.9: top progress bar loops, 1.2 s | **Trickle then complete, no loop:** appears after 300 ms of waiting, creeps to 80 percent (transform scaleX, 2 s), completes in 200 ms and fades when the request ends. Reduced: static bar at 40 percent, hidden at the end. | A loop is only needed when the end is unknown; a trickle is truer and stops by itself (2.2.2). |
| Visual 4 "must not animate height": no accordion, disclosures open instantly (also UX 7.6 "Disclosure: height changes at once") | **Exception E1:** one disclosure (accordion, row expansion, banner, validation message) may animate height through `grid-template-rows: 0fr to 1fr`. Not inside a virtualised table body, not for more than one open element per interaction. | The owner asked for it; it is one container's layout, measured by the browser. |
| Visual 4 and UX 7.6: no page or route transition; theme switch instant (visual 3.12) | **Route transition by View Transitions** (200 ms, fade plus 8px rise of the page, chrome stays put), fallback CSS entrance. **Theme switch crossfades** (200 ms, view transition, no flash). Density and language switches stay instant. | Both are one-shot, user-triggered, short. |
| Visual 4 and UX 11: no count-up, no list reorder animation, no spring or overshoot, no bounce | Count-up allowed for **integer counts on Home only, once per load, 400 ms, skippable, never money**. List insert and remove get FLIP (200 ms, at most 30 elements). Spring overshoot allowed for **floating** overlays and small controls only (dialog, popover, toast, checkbox, switch, tick), never edge-anchored panels and never text blocks. **Still banned:** parallax, confetti, bounce loops, looping icons, attention pulses, animated backgrounds, glow. | Delight where it confirms an action; nothing that repeats. |
| Visual 4: nothing longer than 280 ms except loops | Functional 300 ms at most, entrances 400 ms at most (including stagger). Loops finite or while loading. | Owner's limit. |
| UX 7.6 "Inline error: none"; login visual 0.1 item 7 and section 11: no entrance, banner opacity only, remove instant | Validation and banners open by disclosure (160 to 200 ms) and leave in 120 ms. The sign-in page gets a one-shot entrance (4.8) and one shake on a failed sign-in. | The sign-in page is the first impression. |
| Visual 3.5 "interactive card: no lift"; 3.14 "empty state: no animation"; 3.12 theme instant | Interactive cards and Home tiles lift 2 px on hover (`(hover: hover) and (pointer: fine)` only). Empty state plays a one-shot entrance. Theme crossfade (above). | Small, once, pointer only. |
| UX Q8 / visual Q8: reduce-motion by OS only | Proposed default: add an in-app "Animations: follow device / reduce" switch in Appearance (open question M1). The OS value can never be overridden towards more motion. | Shared PCs with a locked OS setting. |
| FB-10, FB-12, R07 "no other keyframes besides three loops (grep)" | Replaced by MO-01 to MO-08 (section 6.4). FB-10 now reads: with reduce, no transform runs; fades of 120 ms or less are allowed; loops stop. | The old tests would fail a legitimate crossfade. |

Kept as decided: no glassmorphism, blur, backdrop-filter, coloured glow, gradients behind text; density switch and language switch instant; palette option move has no animation; table hover instant; no optimistic server writes; no animation without a user action or a loading state (one-shot entrances run on arrival of data or a route, not on a timer).

## 2. Motion tokens

Tokens are CSS custom properties `--ims-<group>-<name>` emitted in `globals/_themes.scss` (R05 adds them; section 7), read in SCSS through functions (`duration('base')`, `easing('out')`, `move('sm')`, `scale('press')`) that return `var(--...)`. Components never hold a literal duration, curve or distance.

### 2.1 Table

| Token | SCSS | Value | Used for |
|---|---|---|---|
| `--ims-dur-micro` | `duration('micro')` | 80 ms | press in, checkbox tick |
| `--ims-dur-fast` | `duration('fast')` (exists) | 120 ms | hover fade, all exits of small things (toast, popover, banner), crossfades, tick |
| `--ims-dur-base` | `duration('base')` (exists) | 200 ms | entrances, tab indicator, validation, toast in, route, theme |
| `--ims-dur-moderate` | `duration('moderate')` | 280 ms | sheet, drawer, dialog transform, row expansion |
| `--ims-dur-slow` | `duration('slow')` | 400 ms | count-up, chart draw, success mark (single elements, never staggered) |
| `--ims-dur-hold` | `duration('hold')` | 1500 ms | success tick stays, row highlight hold |
| `--ims-dur-loop` | `duration('loop')` | 1500 ms (was 1400) | one shimmer sweep; spinner 800 ms is fixed |
| `--ims-ease-standard` | `easing('standard')` (exists, was `smooth`) | `cubic-bezier(0.4, 0, 0.2, 1)` | moves inside the screen: tab and segment indicator, FLIP |
| `--ims-ease-out` | `easing('out')` (alias `enter`) | `cubic-bezier(0, 0, 0.2, 1)` | the standard ease-out: arrivals, fades, route |
| `--ims-ease-out-strong` | `easing('out-strong')` | `cubic-bezier(0.22, 1, 0.36, 1)` | edge panels (sheet, drawer), disclosure height, switch thumb: fast start, long soft landing, no overshoot |
| `--ims-ease-in` | `easing('in')` (alias `exit`) | `cubic-bezier(0.4, 0, 1, 1)` | departures |
| `--ims-ease-spring` | `easing('spring')` | `cubic-bezier(0.34, 1.45, 0.64, 1)` | floating overlays and small controls: overshoots by about 7 percent of the travel, settles in the same duration. Only for elements that are not edge-anchored. |
| `--ims-ease-linear` | `easing('linear')` | `linear` | spinner, shimmer |
| `--ims-move-xs` / `sm` / `md` | `move('xs'\|'sm'\|'md')` | 4 / 8 / 12 px | message rise / content rise / dialog and toast lift |
| `--ims-slide` | `move('slide')` | 100% | sheet and drawer travel (edge to edge) |
| `--ims-scale-pop` | `scale('pop')` | 0.94 | dialog, popover (menu 0.96 via its own override), mark start |
| `--ims-scale-press` | `scale('press')` | 0.97 (0.98 on large targets) | press |
| `--ims-scale-leave` | `scale('leave')` | 0.98 | exit of toast and list item |
| `--ims-lift` | `move('lift')` | -2 px | card and tile hover (pointer only) |
| `--ims-shake` | `move('shake')` | 4 px | one error shake |
| `--ims-stagger` | `duration('stagger')` | 25 ms per item | list and card entrance; item index capped at 7 |

Stagger arithmetic: item 7 starts at 7 x 25 = 175 ms and runs 200 ms, so a full reveal ends at 375 ms (limit 400). Items 8 and up are not animated at all.

Distance rule: entrances never travel more than 16 px (we use 4, 8, 12). The only full-length travels are sheet and drawer from their own edge.

Loop names: every looping keyframe is named `ims-loop-*` (`ims-loop-spin`, `ims-loop-shimmer`). The test helper and the lint script rely on this.

### 2.2 Values per mode

`<html data-motion>` carries the mode. No attribute means Full. The operating system's `prefers-reduced-motion: reduce` is mapped to Reduced by CSS (the same token block is emitted under the media query and under `[data-motion='reduced']`), so Reduced works with no JavaScript and no flash.

| Token group | Full | Lite (weak device, automatic) | Reduced (OS or in-app setting) | Off (test kill switch only) |
|---|---|---|---|---|
| durations micro, fast | as table | as table | as table | 1 ms |
| durations base, moderate, slow | as table | as table | 120 ms | 1 ms |
| `move-*`, `slide` | as table | as table | 0 | 0 |
| `scale-pop`, `press`, `leave` | as table | as table | 1 | 1 |
| `lift`, `shake` | as table | as table | 0 | 0 |
| `stagger` | 25 ms | 0 | 0 | 0 |
| easing | as table | as table | as table | as table |
| shimmer | on | static | static | static |
| view transitions | on | skipped | crossfade only (120 ms) | skipped |
| count-up, chart draw, FLIP | on | final value, no draw, no FLIP | final value, fade only, no FLIP | final value |

Why Reduced keeps 120 ms fades: a fade moves nothing, so it is the accepted equivalent for vestibular safety and it still shows that something appeared. Why Off uses 1 ms and not 0: `animate.leave` and `animationend` listeners need an event, and a 0 s animation fires none.

Lite is chosen automatically when `navigator.deviceMemory <= 2`, or when a guard sees 3 long animation frames over 100 ms during the first 3 s of an entrance or route change (then it is stored locally as `ims.motion.lite=1` and the next load starts in Lite). It is never shown as a setting and never overrides Reduced. Lite keeps every feedback motion (press, hover, overlays, tabs, toasts) and drops decoration (stagger, shimmer, count-up, draw-in, route transition, FLIP).

## 3. Pattern catalogue

Notation: "in / out" are durations. Every spec is the Full mode; section 5.1 gives the reduced form of each in one table. Reading order follows the owner's list.

### 3.1 Route and page transitions

| Aspect | Spec |
|---|---|
| Mechanism | Router `withViewTransitions({ skipInitialTransition: true, onViewTransitionCreated })` (verified in the installed Angular 22.2.2 router). The shell chrome (`shell-bar`, `shell-nav`, `shell-tabbar`) carries a permanent `view-transition-name`, so it is lifted out of the page snapshot and stays still. The page content is the default `root` group. Content elements never get a name (a named element becomes a containing block for fixed descendants and would break the phone action bar). |
| Forward (list to document, any drill-down) | old page fades out 100 ms `in`; new page fades in and rises 8 px, 200 ms `out`. |
| Back / sibling (tab bar, sidebar, Back) | crossfade only, 120 ms, no rise. |
| Skip | same route with new params or query (filters, paging), Lite, hidden tab, a transition already running (`transition.skipTransition()` in `onViewTransitionCreated`). |
| Blocking | while the pseudo-tree runs, the page is not hit-testable, so the whole transition is at most 200 ms and keyboard focus is unaffected. The router activates only after data guards and lazy chunks have resolved, so the freeze covers rendering, not loading. To be proven on the throttled-CPU harness (section 4.4) before the slice is accepted; if it fails the budget the fallback below is used everywhere. |
| Fallback | where `document.startViewTransition` is missing: the page host plays `ims-rise` 200 ms on activation through `animate.enter`. The CSS is wrapped in `@supports not (view-transition-name: none)` so the two never both run. |
| Shared element (Could) | list row to document: the status chip (identical size and style in both places, so the snapshot morph is crisp) carries the name `doc-status` on the clicked row only (set in the click handler, cleared on `transition.finished`); the document header chip carries it permanently. On phones the card to the header card pairs the same way. Text is never shared across sizes (a 16 px number morphing into a 24 px heading blurs). Ships behind a flag; kept only if the device test passes. |
| Focus | the route announcer and the focus move to the page `h1` run in the update callback, as today; nothing waits for the animation. |
| Reduced | crossfade 120 ms, no rise; shared element off. |

```css
::view-transition-old(root) { animation: ims-fade-out duration('fast') easing('in') both; }
::view-transition-new(root) { animation: ims-rise duration('base') easing('out') both; }  /* distance token is 0 in Reduced */
```

### 3.2 Content entrance and reveal

Plays once, when content replaces a skeleton or an empty first load, or when a page first shows. Never on refetch, sort, filter, paging or scrolling.

| Aspect | Spec |
|---|---|
| Animation | `ims-rise`: opacity 0 to 1 and `translateY(8px)` to 0, 200 ms `out`, `animation-fill-mode: backwards`. |
| Stagger | container gets the class `ims-reveal` for 600 ms when its data first arrives; CSS delays children 1 to 8 by `index x 25 ms`; children 9 and up have no animation. One class on one container, no per-row directive, no per-row JavaScript, so a 5,000-row table (virtual window or 100-row page) costs 8 animated elements. |
| Where | Home widgets and stat tiles, card lists on phones, the first rows of a table, document header blocks, form pages after skeleton. |
| Where not | refetch (rows stay at 60 percent then replace, unchanged), pager clicks, sort, rows added by virtual scroll, anything above the fold of an already shown page. |
| Reduced | opacity only, 120 ms, no stagger. |

```scss
.ims-reveal > :nth-child(-n + 8) { animation: ims-rise duration('base') easing('out') backwards; }
@for $i from 1 through 8 { .ims-reveal > :nth-child(#{$i}) { animation-delay: calc(#{$i - 1} * var(--ims-stagger)); } }
.ims-reveal > :nth-child(n + 9) { animation: none; }
```

### 3.3 Overlays: dialog, drawer, bottom sheet, command palette

Native `<dialog>` and CSS only: `@starting-style` for the entrance and `transition-behavior: allow-discrete` on `display` and `overlay` so the exit also plays and the element is removed from layout afterwards. Support: Chrome 117+, Safari 17.4+, Firefox 129+; anywhere else the dialog appears and disappears at once (still correct). No JavaScript, no `animationend` wait: `showModal()` and `close()` behave as today, focus moves at once.

| Overlay | In | Out | Detail |
|---|---|---|---|
| Dialog (>= 600 px) | opacity 200 ms `out`, transform 280 ms `spring`: from `translateY(12px) scale(0.94)` | 120 ms `in`, opacity and transform | scrim opacity 0 to 1, 200 ms in, 120 ms out; scrim colour unchanged (50 percent light, 60 percent dark) |
| Bottom sheet (< 600 px, same `<dialog>`) | `translateY(100%)` to 0, 280 ms `out-strong`, opacity 200 ms | 200 ms `in` | no overshoot (bottom edge). Drag handle: swipe down follows the finger 1:1 by `transform` (pointer events, one `requestAnimationFrame` write per frame); release past 40 percent or faster than 0.5 px/ms dismisses over at most 200 ms, otherwise it springs back in 200 ms. The close button stays the primary way. |
| Drawer (A16) | `translateX(100%)` to 0 (start edge for RTL-ready logical value), 280 ms `out-strong`, opacity 200 ms; content inside uses the reveal of 3.2 after 100 ms | 200 ms `in` | docked (L09) mode does not animate (it changes layout) |
| Command palette | opacity 180 ms, `translateY(-8px) scale(0.97)` 180 ms `spring`, scrim 35 percent fades 160 ms | 100 ms opacity | result list swaps by 100 ms opacity crossfade on query change, no stagger (typing speed); the active-row move has no animation (unchanged) |
| Popover and menu (A01) | opacity 120 ms, `scale(0.96)` from the anchor corner (`transform-origin` set by placement) 160 ms `spring` | 100 ms opacity | Angular-controlled, so `animate.enter` and `animate.leave` |
| Tooltip | opacity 120 ms after the existing hover delay | 80 ms | no movement |

```css
dialog { opacity: 0; transform: translateY(var(--ims-move-md)) scale(var(--ims-scale-pop));
  transition: opacity var(--ims-dur-fast) var(--ims-ease-in), transform var(--ims-dur-fast) var(--ims-ease-in),
              overlay var(--ims-dur-fast) allow-discrete, display var(--ims-dur-fast) allow-discrete; }
dialog[open] { opacity: 1; transform: none;
  transition: opacity var(--ims-dur-base) var(--ims-ease-out), transform var(--ims-dur-moderate) var(--ims-ease-spring),
              overlay var(--ims-dur-moderate) allow-discrete, display var(--ims-dur-moderate) allow-discrete; }
@starting-style { dialog[open] { opacity: 0; transform: translateY(var(--ims-move-md)) scale(var(--ims-scale-pop)); } }
dialog::backdrop { opacity: 0; transition: opacity var(--ims-dur-fast) var(--ims-ease-in), overlay var(--ims-dur-fast) allow-discrete, display var(--ims-dur-fast) allow-discrete; }
dialog[open]::backdrop { opacity: 1; transition-duration: var(--ims-dur-base); }
@starting-style { dialog[open]::backdrop { opacity: 0; } }
```

Focus, scroll lock, Esc and the trap are the browser's (unchanged). During the exit the dialog is already closed: it cannot take clicks or focus.

### 3.4 Toasts and banners

| Element | In | Out | Detail |
|---|---|---|---|
| Toast (Angular, `animate.enter` / `animate.leave`) | opacity 200 ms and rise 12 px (desktop bottom right, from below), phone: from 12 px above (under the top bar), `spring`; the state icon pops (`scale 0.8 to 1`, 280 ms, 80 ms delay) | opacity and `scale(0.98)` 120 ms `in` | the leaving toast gets `aria-hidden` and `pointer-events: none` at once; the remaining toast slides into place with FLIP (200 ms `standard`); timer pause on hover and focus unchanged (R11) |
| Banner (inline, `app-banner`) | disclosure: height by `grid-template-rows` 200 ms `out-strong`, content opacity and 4 px rise 40 ms later | 120 ms opacity then height 160 ms | one banner at a time per placement; a `role="alert"` banner is in the DOM and announced at once, the animation does not delay the announcement |
| Error summary (R14) | same as banner | same | focus moves to it at once |

### 3.5 Tabs, segmented control, navigation indicators

| Element | Spec |
|---|---|
| Segmented control (theme, density, Mine/All) | equal-width segments, one thumb that moves by `translateX(index x 100%)`, 200 ms `standard`. No first-paint animation (the thumb gets its transition after the first frame). |
| Tabs | one 3 px underline element: `translateX(left) scaleX(width)` from a 1 px base, 200 ms `standard`, positions measured with `offsetLeft` and `offsetWidth` on change and by `ResizeObserver`. The panel crossfades (opacity 120 ms, 8 px rise 200 ms). |
| Sidebar active item (desktop) | the same indicator element moving vertically, 200 ms; the item text weight change is instant. |
| Phone tab bar | the 3x24 pill above the active icon slides between five equal cells (translateX), and the active icon pops once (`scale 1, 1.12, 1`, 200 ms). |
| Count badge | when a count increases while on screen: `scale 1, 1.2, 1` 200 ms once per change; never on first render; never loops. |
| Table sort chevron | rotates 180 degrees, 160 ms `standard`. Rows do not animate on sort. |
| Reduced | the indicators and thumb jump (no transform transition); the icon pop, badge bump and chevron rotation are off (the chevron swaps instantly). |

### 3.6 Disclosure: accordion, row expansion, validation message

| Aspect | Spec |
|---|---|
| Technique | a wrapper `display: grid; grid-template-rows: 0fr` that becomes `1fr` when open; the single child has `min-height: 0; overflow: hidden`. 280 ms `out-strong` for accordion and row expansion, 200 ms for banners and validation messages. The browser interpolates the track, so no measuring in JavaScript. Chosen over `interpolate-size: allow-keywords`, which is not available in every supported engine; it may be added later as an enhancement. |
| Content | opacity 120 ms and 4 px rise, delayed 40 to 60 ms so it appears as the space opens. |
| Collapsed content | `inert` while closed (no tab stop, not read), button `aria-expanded` as today; the region is not hidden with `display: none` (it would end the transition). |
| Row expansion in `app-table` (A08) | page-size tables (25 to 100 rows) only. In continuous (virtual) scroll mode the row opens at once with the 120 ms content fade. One row opens at a time (opening another closes the first, both animate). |
| Validation message (D4/F4 messages) | opens by the same disclosure, 160 ms, so the field list below slides rather than jumps. The live-region text is set at once. Closing is 120 ms. |
| CLS | these shifts follow the user's own click or blur, so the browser excludes them from CLS; the test still reads `layout-shift` entries with `hadRecentInput` and fails on any without it. |
| Reduced | no height animation: opens at once; content fade 120 ms. |

### 3.7 Buttons

All four variants. Structure: the button is `position: relative; isolation: isolate; overflow: hidden`; `::before` is the hover overlay, `::after` the success overlay, both `z-index: -1` and `opacity: 0`; the label is a stack of three layers in one grid cell (idle, busy, done) so the button is always as wide as its widest state and never changes size.

| State | Spec |
|---|---|
| Hover (`@media (hover: hover)`) | `::before` in the hover colour (`accent-hover`, `surface-sunken`, `danger-hover`) fades `opacity` 0 to 1 in 120 ms `out`; leaves in 120 ms. Contrast is the hover pair already in the gate (the end state), the 120 ms between is not text-critical. Forced colours: the overlay disappears, the border gets `Highlight`. |
| Press | `transform: scale(0.97)` over 80 ms `out`; release returns over 200 ms `spring`. Pressed colour swap is instant (overlay `accent-pressed`). |
| Focus-visible | ring appears instantly (2 px, offset 2 px). No animation, by rule. |
| Busy | the click is accepted once; at 150 ms the busy layer fades in (opacity 120 ms) with a 20 px spinner and the label "Saving" or the verb in progress; the idle layer rises out 8 px. A fast answer shows no spinner. The spinner rotates 800 ms linear and exists only while busy (a hidden spinning spinner is a bug: it keeps a layer and a timeline alive). Reduced: spinner replaced by the static `clock` icon plus the same word. |
| Success (in-place saves only, R08 rule) | the busy layer rises out, the done layer rises in (check tick drawn by `clip-path: inset(0 100% 0 0)` to `inset(0)`, 200 ms, 60 ms delay, with the word "Saved"); the `::after` success fill fades in 200 ms (the button turns from accent to success without animating colour); holds 1500 ms; reverts by the reverse. The polite live region speaks once at the start of the state, not at the end of the animation. Reduced: fill and label swap by 120 ms crossfade, tick shown complete. |
| Error | the failed button shakes once: `translateX` 0, -4, +4, -2.4, +2.4, 0 over 280 ms. Only on a failed sign-in and on a failed confirm of a dialog; never for validation errors (the error text, icon and focus are the cue). A new shake needs a new failure and 1 s since the last. Reduced and Off: `--ims-shake` is 0, so nothing moves (the banner and focus carry the message). "Removable": the token is zeroed by Reduced and by the in-app setting (M1). |
| Disabled | no animation. |

### 3.8 Form fields and controls

| Element | Spec |
|---|---|
| Focus | the 2 px focus ring is instant. An accent underline (2 px, inset 8 px each side) under the control grows by `scaleX(0 to 1)` from the centre in 200 ms `out` (decoration only; the ring stays the accessibility cue). Reduced: appears at once. |
| Label | no floating label. Labels are always visible above the control (visual 3.2, no placeholder labels). The label does not animate. |
| Required asterisk, hint | static. |
| Validation message | 3.6. The `danger` border plus inset ring is instant (it is state, not decoration). |
| Checkbox | on check: the accent fill (an overlay) scales 0.6 to 1 and fades in 200 ms `spring`; the check mark is drawn by `clip-path` in 120 ms, 50 ms after. Uncheck is the reverse in 120 ms. |
| Radio | the inner dot scales 0 to 1, 200 ms `spring`; the fill fades 120 ms. |
| Switch | the thumb moves 20 px by `translateX`, 200 ms `out-strong` (no overshoot inside the track); the track fill is an overlay fading 200 ms; while pressed the thumb stretches `scaleX(1.2)`. |
| Select, date field (R23) | the listbox follows 3.3 popover. The calendar button pops nothing. |
| Reduced | all of the above switch at once; the check mark and fill appear at once. |

### 3.9 Loading: skeleton, top bar, spinner, pull to refresh

| Element | Spec |
|---|---|
| Skeleton | shapes as visual 3.8. Each shape clips a pseudo-element (`overflow: hidden`) holding a horizontal highlight (`transparent`, `shine`, `transparent`) that moves `translateX(-100%)` to `100%`, `ims-loop-shimmer`, 1500 ms linear, `animation-iteration-count: 6`, then stops. `shine` is a per-theme token (white 65 percent in light, `surface-raised` 75 percent in dark); it is decorative, so it is not in the contrast gate. Timing unchanged: appears after 300 ms of waiting, stays at least 400 ms; then content replaces it with the reveal of 3.2 (crossfade: skeleton fades 120 ms while content rises in). At most 8 shapes animate at once on a phone (6 cards); on desktop 8 rows. Reduced and Lite: static shapes at 0.7 opacity. |
| Top route bar | after 300 ms of navigation or refetch: a 3 px `accent` bar over a `surface-strong` track, `scaleX(0)` to `0.8` over 2 s with `cubic-bezier(0.1, 0.6, 0.2, 1)`; on completion `scaleX(1)` in 200 ms then the track fades 200 ms. It never loops. The table's own refetch bar is the same component. Reduced: static bar at 40 percent while loading, removed at the end. |
| Spinner | 20 px ring, `ims-loop-spin` 800 ms linear, only while busy. Essential loading indicator for WCAG 2.2.2, always next to a word. After 8 s the existing "Still working. Don't close this page." text appears (UX 7.2). |
| Refetch | old rows at 60 percent opacity (120 ms) plus the top bar, as today. |
| Pull to refresh (phone lists, Could, slice M08) | Sensible on the three phone lists that refetch (Receipts and Issues, Approvals, Stock), where a warehouse user holds a phone in one hand. Only at scroll top, only touch pointers, and the browser's own pull-to-refresh is turned off for the list scroller (`overscroll-behavior-y: contain`) because it would reload the whole PWA and lose state. The indicator follows the finger 1:1 by `translateY` to 72 px with resistance (0.5), a threshold at 64 px flips the arrow 180 degrees, release runs the normal refetch and the top bar; the indicator settles back in 200 ms. Never the only way (a "Refresh" action stays in the page menu). Reduced: no follow; releasing past the threshold shows the text "Refreshing" for the duration of the request. Rejected for desktop. |

### 3.10 Numbers, charts, row feedback

| Element | Spec |
|---|---|
| Count-up | Home stat tiles only, **integers** (document counts, low-stock count, products with stock), once per page load, 400 ms `out` (cubic), one `requestAnimationFrame` loop for at most 6 elements. A value under 10 does not count (it just appears). Never money, never quantities with decimals (an intermediate `3,421.17` could be misread; money must reconcile). Layout: a hidden sizer span holds the final digits so the tile never changes width; `tabular-nums` as everywhere. The real final value is in the DOM from the first frame (visually hidden text) so screen readers and tests read the truth; the animated digits are `aria-hidden`. A click or key press on the tile skips to the final value. Reduced, Lite, Off: final value at once. |
| Chart draw-in (A20) | starts when the chart enters the viewport (`@defer (on viewport)`), once. Axes and gridlines fade 120 ms; bars grow `scaleY(0 to 1)` from the baseline, 400 ms `out`, delayed 20 ms per bar (cap 8); line series reveal left to right by `clip-path: inset(0 100% 0 0)` to `inset(0)`, 400 ms `out`, 120 ms delay; donut and area fade and `scale(0.96 to 1)`. Areas under 400 x 300 CSS px only. Series hover: the other series drop to 0.35 opacity (120 ms). Tooltip: opacity 120 ms. The table view toggle and the numbers are in the DOM at once. Reduced: 120 ms fade of the whole chart. Lite and Off: drawn complete. |
| Row highlight on data update | an `accent-soft` overlay pseudo-element on the row: opacity 0 to 1 in 120 ms, hold 300 ms, out 700 ms (one keyframe, 1.1 s). Only rows whose values changed between two loads of the same list, at most 10 rows, never on the first load, never on sort or paging. The user's own line edits keep 120 / 1500 / 1000 ms (R14). Changed content is also the cue (the number itself), the flash only helps find it. Reduced: static tint for 1.5 s, then removed. |
| Saved and posted confirmation | in-place save: the button tick (3.7). State change (Post, Approve, Reverse, Cancel): the status chip swaps (old layer fades 120 ms, new layer fades in with `scale(0.94 to 1)` 200 ms `spring`, its check icon drawn by `clip-path` 200 ms), a toast follows (3.4); a 56 px success mark (soft disc pops `scale 0.8 to 1`, 280 ms `spring`, check wipes in 200 ms) appears in the document header only for Post and Reverse, which are final, and stays as the static status icon. No confetti, no full-screen celebration. Reduced: chip and mark swap with a 120 ms crossfade. |
| List insert and remove (FLIP) | a new line or row: `ims-rise` with `spring` 200 ms; siblings below slide down with FLIP (their old position measured, new position measured, a 200 ms `standard` `transform` from the delta, Web Animations API). Remove: the item fades and shrinks to 0.98 in 120 ms (`animate.leave`), then the siblings slide up over 160 ms, total 280 ms. Used for the document line cards and grid (editor, at most about 50), the toast stack, filter chips and selection chips. At most 30 elements are measured; above that, or inside a virtualised body, no animation. Reduced and Lite: instant. |

### 3.11 Drag and drop (advanced table column reorder, A03)

CDK drag-drop already animates siblings with a `transform` transition; our styles set it to `duration('base')` and `easing('standard')`.

| Moment | Spec |
|---|---|
| Pick up | the dragged header cell lifts: an elevation pseudo-element (shadow 2) fades in 120 ms and the cell scales to 1.02; the cursor is `grabbing`; the others dim to 0.7 opacity only if the user prefers (not by default). |
| Over | siblings slide aside by the CDK transform, 200 ms; the drop slot shows the existing 3 px `accent` insertion edge, not an animation. |
| Drop | the cell settles into the slot in 200 ms `out-strong`, the elevation fades 120 ms. |
| Keyboard reorder (move left and right through the column menu or Alt+Arrow) | same slide of the neighbour cells, plus the polite announcement "Unit moved to position 3 of 8". Never required: the menu items exist. |
| Reduced | no scale, no elevation animation; siblings jump; the dragged cell shows a static `shadow(2)` and outline; the drop is instant. |

### 3.12 Empty states

One-shot, on mount, no loop: the 64 px disc fades in and scales 0.92 to 1 (240 ms `spring`), the icon rises 4 px 60 ms later (200 ms), the title and line fade 120 ms after that (total 340 ms). With the optional A21 illustration: at most three parts, each `ims-rise` 25 ms apart. No idle motion, no hover motion. Reduced: 120 ms fade of the block.

### 3.13 Sign-in page entrance (R25 look, built in M05)

Plays once on load, 360 ms in total, nothing waits for it (the fields take focus and input from the first frame; opacity does not block typing).

| Element | Animation | Delay |
|---|---|---|
| Brand panel (desktop) | opacity 0 to 1, 300 ms `out`; the line pattern fades from 0 to its 6 percent over 400 ms; no movement, no parallax | 0 |
| Logo lockup and `h1` (the LCP candidates) | `ims-rise` 200 ms | 0 |
| Field group | `ims-rise` 200 ms | 40 ms |
| Sign in button | `ims-rise` 200 ms | 80 ms |
| Footer, language and appearance menus | opacity 200 ms | 120 ms |
| Failed sign-in | banner discloses (3.4), one button shake (3.7), focus stays on the field as the login spec says | at once |
| Session ended re-sign-in dialog (R26) | normal dialog (3.3), no page entrance | |
| Reduced | all fade 120 ms with no delay; no shake | |

### 3.14 Theme switch

The theme change runs inside `document.startViewTransition(() => applyTheme())`: the old frame is captured, the new theme is applied, and the two crossfade over 200 ms `standard` (120 ms in Reduced, which is a fade, not a movement). No per-property transitions on the page (they would paint every element at its own time and flash mid-colours; they would also animate 5,000 table cells). Not used for the first paint (`theme-init.js` and the media query decide that, unchanged), not for OS-driven changes, not in Lite or Off (instant). While the transition runs the page is not hit-testable for those 200 ms. The `theme-color` meta tags change at the start. Density, language and other preferences stay instant.

## 4. Implementation and performance

### 4.1 How to build it

1. **CSS first.** Keyframes, transitions, `@starting-style`, `animation-delay`, `grid-template-rows`, View Transitions. All patterns above are CSS except the five below.
2. **Angular `animate.enter` and `animate.leave`** for elements Angular inserts and removes (toast, banner, popover, `@if` panels, list items): they take a class name (or a function for rare cases) and Angular defers removal until the animation ends. Native `<dialog>` does not need them. Set `MAX_ANIMATION_TIMEOUT` (exported by `@angular/core`) to 500 ms so a stuck animation can never keep an element in the DOM; check its default against the Angular docs when wiring it.
3. **Web Animations API (`element.animate`)** only for FLIP deltas, the sheet drag settle, and the tab or segment indicator measurement. About 60 lines in one shared helper.
4. **`requestAnimationFrame`** only for count-up (at most 6 elements, 400 ms) and the pointer-driven sheet and pull-to-refresh follow.
5. **A small `MotionService`** (signal `mode`: full, lite, reduced, off; reads `matchMedia`, the stored Lite flag and `ims.motion`; writes `data-motion` on `<html>`; exposes `animate()` to the five JavaScript users above, which then set the final state synchronously when it is false).

**No animation library.** Reasons: every JavaScript animation library adds bytes to the initial chunk for work that CSS and WAAPI already do on the compositor; we have no timeline or physics need. `@angular/animations` is not an option: in the installed Angular 22.2.2 its public types are marked `@deprecated 20.2 Use animate.enter or animate.leave instead. Intent to remove in v23` (read from `node_modules/@angular/animations/types`), and the Angular router offers `withViewTransitions` natively. The package is listed in `web/package.json` but nothing in `web/apps` or `web/libs` imports it (searched), so M01 removes it from the dependencies and Q07 bans the import. If a library is ever proposed (a spring physics need for the sheet drag), it needs a bundle-cost line in the PR and the fe-architect's approval; none is recommended.

**Rules for every animation:**
- `transition-property` and `animation` keyframes may name only `opacity`, `transform`, `translate`, `scale`, `rotate`, `clip-path`, `visibility`, `display`, `overlay`, and (only inside the single `disclosure` mixin) `grid-template-rows`. Never `all`, colour, background, border, shadow, filter, width, height, margin, padding, `top`, `left`, `inset`.
- Elevation, hover colour and success colour are pseudo-element overlays whose opacity moves.
- `will-change` is not written in any stylesheet. CSS animations of transform and opacity are promoted by the browser while they run; a permanent `will-change` on a list row would keep layers alive for 5,000 rows. The only allowed use is a script setting it on one element for the length of a pointer-driven drag and removing it at the end.
- A loop exists in the DOM only while visible (a hidden, spinning spinner is a defect; found and fixed in the demo while building it). Loops are named `ims-loop-*`; `animation-iteration-count: infinite` is allowed for `ims-loop-spin` only.
- At most 12 animations in flight at any moment, excluding skeleton shapes (at most 8) and the spinner. The reveal cap of 8 items enforces it.
- Hover and press effects sit behind `@media (hover: hover)` or `:active`; table rows and dense list rows have none.
- Animation never gates behaviour: no handler waits for `animationend` except the `animate.leave` removal (500 ms ceiling) and the toast removal.
- No `transition: all`, no `!important` in components. The one reduced block stays the only place with `!important`, as now.

### 4.2 Budgets

| Item | Budget |
|---|---|
| Initial JS, motion code (`MotionService`, reveal helpers, directives) | at most 3 kB raw; the whole programme at most 8 kB raw added to the initial total. The plan's target after R24 moves from 575 kB to 583 kB raw; warning 600 kB and error 650 kB do not change. |
| Global styles (tokens, keyframes, utility classes, VT rules) | at most 2 kB raw; the plan's global-styles cap of 9 kB moves to 11 kB (Q04 updates `bundle-budget.json`). |
| Lazy code (count-up, FLIP, indicator measurement, sheet drag, pull to refresh) | inside the chunks that use them; each at most 1.5 kB. |
| Animation library | 0 kB (none). |
| Concurrent animations | at most 12 plus loops. |
| Entrance total | at most 400 ms from first frame to last. |
| Route transition | at most 200 ms; at most 16 ms main-thread work to start it on a 4x throttled CPU. |

### 4.3 What the sections of the codebase look like afterwards

`libs/shared/styles/src/globals/_motion.scss` (new): keyframes (`ims-rise`, `ims-fade`, `ims-pop`, `ims-leave`, `ims-shake`, `ims-flash`, `ims-wipe`, `ims-loop-shimmer`, `ims-loop-spin`), utility classes (`.ims-reveal`, `.ims-enter`, `.ims-leave`), the `disclosure` mixin, the VT rules, the reduced and off token blocks. `libs/shared/styles/src/mixins/_motion.mixin.scss` (new): `hover-overlay`, `press`, `elevation-overlay`, `disclosure`. `libs/shared/utils/src/lib/motion/*` (new, pure): `MotionService`, `flip()`, `countUp()`, `indicator()`. Components own only their selectors; they take no literals.

### 4.4 What to measure, and how

Playwright with Chromium; CPU throttling through the DevTools protocol (`Emulation.setCPUThrottlingRate`): 4x is the mid-range Android proxy, 6x the low-end proxy. Real-device spot checks (one low-end Android, one iPhone) at each motion gate; the lab numbers are a gate, the device is the judge.

| Metric | How | Gate |
|---|---|---|
| Frame pacing during an animation | a page-side `requestAnimationFrame` loop records frame gaps during the scenario (open dialog, route change, Home reveal with count-up, expand a row, toast stack); report the share of frames over 20 ms and over 33 ms | at 4x: at most 5 percent over 20 ms, none over 100 ms. At 6x: at most 5 percent over 33 ms |
| Long tasks and long animation frames | `PerformanceObserver` for `longtask` and `long-animation-frame` during the same scenarios | at 4x: no task over 100 ms started by an animation; at most 1 over 50 ms per scenario |
| INP | `PerformanceObserver({ type: 'event', durationThreshold: 16 })` while clicking, typing and tabbing during each animation (the "no animation blocks input" rule) | at 4x: worst interaction at most 200 ms (the "good" band); route transition clicks included |
| CLS | `layout-shift` entries across the registry screens with Full motion | 0 (a shift with `hadRecentInput` is reported but allowed for the disclosure patterns only) |
| Idle | `document.getAnimations().length` after a page settles | 0 on every registry screen except while a loading state is shown (the existing LG-47 target stays) |
| 5,000-row table | virtual scroll mode (A11) with a 5,000-row stub, first reveal, then 5 s of scrolling | no animation on any row beyond the first 8; frame budget as above |
| Paint cost | one Chromium trace per gate scenario archived as an artefact | reviewed by fe-designer at the motion gate |

### 4.5 Kill-switch for tests

`localStorage['ims.motion'] = 'off'` (read by `theme-init.js` before first paint, 90 bytes more) or `?motion=off` on non-production builds sets `data-motion="off"`: every duration is 1 ms, every distance 0, loops static, JavaScript animators go to the final state. Playwright's `addInitScript` sets it for visual-regression and for functional tests that do not test motion. The key is honoured in production builds too; it only affects the person's own device and offers nothing but less motion. A separate Playwright project runs the same tests with Full motion so that motion bugs (a stuck `animate.leave`, a click blocked by a view transition) are caught.

## 5. Accessibility

### 5.1 Reduced-motion mapping (every pattern)

| Pattern | Full | Reduced (OS setting) |
|---|---|---|
| Route and page | VT fade plus 8 px rise | VT crossfade 120 ms |
| Content reveal, stagger | fade and rise, staggered | fade 120 ms, together |
| Dialog | spring pop | fade 120 ms |
| Sheet, drawer | edge slide | fade 120 ms |
| Palette, popover, menu | pop and fade | fade 120 ms |
| Toast, banner | rise and fade, FLIP | fade 120 ms; neighbours jump |
| Tabs, segmented, nav, tab bar | sliding indicator, icon pop | indicator jumps |
| Disclosure, row expansion, messages | height by grid rows | opens at once, content fade 120 ms |
| Button hover | overlay fade 120 ms | overlay fade 120 ms (a fade) |
| Button press | scale 0.97 | none |
| Button busy | spinner after 150 ms | clock icon plus the word |
| Button success | morph and tick | crossfade, tick complete |
| Error shake | 4 px once | none |
| Focus underline, checkbox, radio, switch | grow, pop, slide | instant |
| Skeleton | shimmer, 9 s | static 0.7 opacity |
| Top bar | trickle and complete | static 40 percent |
| Pull to refresh | follows the finger | text only |
| Count-up | 400 ms | final value |
| Chart draw-in | draw | 120 ms fade |
| Row highlight | flash 1.1 s (or 2.6 s on own edit) | static tint 1.5 s |
| Posted mark, chip swap | pop and wipe | crossfade |
| FLIP, drag | slide, elevation | jump, static outline |
| Empty state, sign-in entrance | rise, fade | fade 120 ms |
| Theme switch | crossfade 200 ms | crossfade 120 ms |
| Card and tile lift | 2 px | none |

### 5.2 Rules

- **No motion-only meaning.** Each animation is paired with text, an icon or a place: "Saved" and "Posted" are words; the highlight is also the changed number; the shake is also the banner and the focus move; a skeleton is also an `aria-busy` region with one "Loading" status; the count-up has the true number in the DOM.
- **Focus during transitions.** Focus moves when the state changes, not when the animation ends: `showModal()` focuses at once (the dialog is already at opacity above 0 and rendered, never `display: none`), focus returns to the trigger on `close()`; a leaving element is `inert` and `aria-hidden` from the first frame so it cannot be focused, clicked or read twice; the route `h1` takes focus in the view-transition update callback; a collapsed disclosure is `inert`. `scroll-padding` for sticky bars is unchanged.
- **Screen-reader announcements.** Live regions are updated at the moment of the state change and never wait for `animationend`. Toast text is in the DOM, in the existing live region, on insertion. Animation never toggles `display` or `visibility` on a live region. No announcement is added or removed by this document.
- **Vestibular safety.** No parallax. No scale change larger than 6 percent in an entrance (dialog 0.94, popover 0.96, press 0.97), nothing zooms the page, no rotation beyond the 180 degree chevron (and the loading spinner). Entrance travel is at most 16 px (we use 4, 8, 12); only sheets and drawers travel from their own edge, and that is user-triggered. No background or full-width motion that is not triggered. The route transition moves 8 px, fades, and never slides the page sideways.
- **Flashing.** Nothing flashes more than three times in a second: the shimmer sweeps once per 1.5 s and is a soft highlight, the row flash is one pulse, the busy bar completes once.
- **WCAG 2.2.2 (pause, stop, hide).** Auto-starting motion over 5 s: skeleton (9 s then stops), top bar (no loop), nothing decorative. The spinner is an essential loading indicator, runs only while a request is open, and the "Still working" text appears at 8 s. No carousel, ticker or marquee exists.
- **2.5.8 and 2.5.5.** Press scale and lift never reduce the target below its design size: the transformed box is only used while pressed and the 44 px floor has 3 px of slack at 48 px.
- **Forced colours.** Overlays vanish (a `background` pseudo-element is not drawn): hover shows the border in `Highlight`, success shows the check and the word. Animations still run; they are opacity and transform and carry no colour.
- **Zoom and 320 px.** Distances are in px but small; the sheet and drawer travel in percent; the reveal never causes horizontal overflow because it moves vertically inside the container; `overflow: hidden` wrappers are only the button and the disclosure.

## 6. How to test

### 6.1 Playwright projects

Two projects share the specs: `motion-full` (`reducedMotion: 'no-preference'`) and `motion-reduced` (`reducedMotion: 'reduce'`). A third setting, `data-motion=off`, is applied by `addInitScript` in the visual-regression project and in tests that are not about motion.

### 6.2 Waiting without sleeping

```ts
// e2e/src/support/motion.ts (M07)
export async function settle(page: Page) {
  await page.evaluate(() => Promise.all(document.getAnimations()
    .filter(a => !((a as CSSAnimation).animationName ?? '').startsWith('ims-loop-'))
    .map(a => a.finished.catch(() => undefined))));
}
```
- After an action that animates, `await settle(page)`; for one element use `locator.evaluate(el => Promise.all(el.getAnimations().map(a => a.finished)))`; for `animate.leave` wait for the element to be detached (`expect(locator).toHaveCount(0)`).
- Assertions use computed style: `animation-name`, `transition-property`, the token values (`getComputedStyle(root).getPropertyValue('--ims-move-sm')` is `0px` in Reduced).
- Never `waitForTimeout` to cover a transition.

### 6.3 Visual regression

Baselines are taken with `data-motion=off` and `toHaveScreenshot({ animations: 'disabled', caret: 'hide' })`. Playwright's `animations: 'disabled'` fast-forwards finite animations and cancels infinite ones, and the kill switch makes both agree. A separate small set of "mid-state" screenshots is not kept (flaky by nature).

### 6.4 Acceptance IDs

| ID | Criterion | Where it is proven |
|---|---|---|
| MO-01 | With `reducedMotion: 'reduce'`: on every registry screen no element has a running `transform` animation or transition, no `ims-loop-*` animation runs, and `--ims-move-sm` is `0px`; the busy button shows the word and the clock; features all work | `motion-reduced` project (M07) |
| MO-02 | No stylesheet animates a property outside the allowed list (4.1); `will-change` appears in no stylesheet; no `infinite` except `ims-loop-spin`; every `@keyframes` is in `_motion.scss` or a component loop | `web/scripts/check-motion.mjs` and stylelint (M01), runs in `npm run lint` |
| MO-03 | After a registry screen settles, `document.getAnimations().length` is 0 (loading states excluded) | motion-full |
| MO-04 | CLS is 0 (or only input-excluded disclosure shifts) on the registry screens with Full motion | Q12 extended |
| MO-05 | Frame, long-task and INP gates of 4.4 at 4x throttle for the six scenarios | M07 harness, report-only until G3 of the plan, then blocking |
| MO-06 | A leaving toast, banner or popover is inert: a click on it does nothing and Tab skips it; focus after `close()` is on the trigger within one frame; the route `h1` has focus before the route transition ends | motion-full |
| MO-07 | The live region text of a toast is present within one frame of insertion; the count-up tile's accessible name contains the final number from the first frame | motion-full, Spectator |
| MO-08 | Skeleton shapes stop animating after 9 s; the top bar has no infinite animation; the busy spinner is absent from the DOM or not running when the button is idle | motion-full |
| MO-09 | Every animation in the product is listed in section 3 (the check script prints the list of keyframe names and the owner checks it against section 3 at each motion gate) | gate review |

### 6.5 Unit tests (Spectator and Jest)

- `MotionService`: with a `matchMedia` stub for `prefers-reduced-motion` the mode is `reduced` and `data-motion` is set on `document.documentElement`; the stored Lite flag starts Lite; Reduced is never overridden to Full by the in-app setting; `ims.motion=off` wins.
- Components that use JavaScript motion (`countUp`, `flip`, indicator, sheet drag) get a `provideMotion('reduced' | 'off' | 'full')` test provider: in Reduced they set the final state synchronously and never call `Element.animate` (spy), in Full they call it once with the expected keyframes.
- Component specs assert the structure that the CSS relies on (three label layers in the button, `inert` on a closed disclosure, `aria-hidden` on a leaving toast, the sizer span in the counter), not computed animation (jsdom has none).

## 7. Changes to plan v2

Numbers are changed frontend lines including specs, in the plan's style. The cap of 1,200 per slice still holds; slices over 1,000 during the build are split. New ids are the "M" programme (Motion); no existing id changes meaning.

### 7.1 Slices that grow

| Slice | Growth | New size | What it adds |
|---|---|---|---|
| R05 | +60 | 660 | the motion tokens of section 2 (CSS properties and SCSS functions, Reduced and Off blocks in `_themes.scss`, the `shine` token) so that R07 and R08 can use them in wave 3 |
| R07 | +100 | 450 | skeleton: shimmer replaces the pulse, finite 6 sweeps, `ims-loop-*` names; spinner exists only while busy; reduce-motion block uses the token mapping (the safety-net rule stays); acceptance text changes per 1.2 |
| R08 | +150 | 900 | button hover overlay, press, three-layer label stack, busy, success morph and tick, shake token; checkbox, radio, switch micro-interactions; focus underline; the stylelint transition rule becomes the allow-list of 4.1 |
| R09 | +200 | 500 | sliding indicator for tabs and the segmented control (uses M02), badge bump, chip swap pop, stat-card lift |
| R10 | +120 | 520 | tab bar pill and icon pop, sidebar indicator, top bar progress trickle markup (the component is built in M03) |
| R11 | +80 | 930 | toast `animate.enter` / `animate.leave`, FLIP of the survivors, `inert` while leaving |
| R13 | +250 | 850 | dialog, sheet, drawer-ready CSS (`@starting-style`), scrim, sheet swipe handle gesture (small), banner disclosure, empty-state entrance |
| R14 | +80 | 780 | row highlight variants (user edit and data update), error-summary disclosure |
| R15 | +150 | 800 | skeleton to content crossfade and reveal on every screen, no entrance on refetch |
| A01 | +100 | 650 | popover and menu pop, `animate.enter` / `animate.leave` usage |
| A08 | +120 | 620 | row expansion by grid rows (page-size tables only) |
| A09 | +40 | 840 | floating bulk bar rise and fade |
| A12 | +40 | 840 | combobox listbox pop |
| A16 | +120 | 920 | drawer slide and content reveal, swipe-to-close on phones |
| A17 | +40 | 640 | timeline entrance |
| A18 | +60 | 760 | bell panel pop, badge bump |
| A20 | +200 | 900 | chart draw-in, series hover, tooltip |
| A21 | +100 | 450 | empty-state entrance with illustration parts |
| Q10 | +60 | 760 | gallery gets a motion mode switch and "replay" per component |
| Q12 | +60 | 760 | MO-04 and the 5,000-row frame scenario |

Unchanged on purpose: A02 and A03 (at the 1,000 cap; their motion is in M06), A04, A05, A19 (draw-in is in A20), R17 and R19 (Home motion is in M04), R25 (entrance is in M05), R06 (theme crossfade is in M03), Q01 to Q09, L-slices (L06 re-checks the overlays under forced colours as part of its existing scope).

### 7.2 New slices

| Slice | Name | Size | Depends on | Lane | What it contains |
|---|---|---|---|---|---|
| M01 | Motion foundation | 650 | R05, R07 | Y (styles) and S for `theme-init.js` | `globals/_motion.scss` (keyframes, utility classes, `disclosure` mixin, VT rules, mode blocks), `mixins/_motion.mixin.scss`, `MotionService` in `libs/shared/utils/src/lib/motion`, `data-motion` and `ims.motion` in `theme-init.js`, Lite guard, `MAX_ANIMATION_TIMEOUT` provider, removal of the unused `@angular/animations` dependency, `web/scripts/check-motion.mjs` and the stylelint allow-list (MO-02), a short convention page `web/docs/conventions/motion.md` |
| M02 | Motion primitives | 900 | M01 | none (new folder `shared/ui/.../motion`) | `app-disclosure` (grid-rows wrapper), `imsReveal` container directive (one class bound to a signal, 600 ms), `imsIndicator` directive (tab, segment, nav, tab bar), `flip()` helper and `imsFlip` directive, `countUp` directive with sizer, `imsLeave`/`imsEnter` class helpers, specs for each with `provideMotion` |
| M03 | Route and theme transitions, top bar | 650 | M01, R06, R10 | S (shell) | `withViewTransitions` in `app.config.ts`, chrome `view-transition-name`s, `onViewTransitionCreated` rules (skip same-route, Lite, hidden), fallback CSS, `ThemeService` view-transition wrapper, trickle progress component replacing `shell-progress` and the table bar (one component), shared element behind a flag (Could), a stub e2e spec for focus and input during the transition |
| M04 | Home motion | 400 | M02, R17 | H | widget frame reveal, stat tile count-up and hover lift, widgets for later profiles (R19) inherit it from the frame, no change to data code |
| M05 | Sign-in motion | 300 | M02, R25 | U | entrance table of 3.13, failed sign-in disclosure and one shake, panel fade, spec and `reducedMotion` check (LG-47 stays) |
| M06 | Table, list and editor motion | 700 | A03, A04, R14, M02 | T and E (serial after R30 and A15) | sort chevron, row flash on refetch change, FLIP for the editor lines and filter chips, column drag feedback on CDK drag-drop, keyboard reorder announcement, first-reveal class on list pages |
| M07 | Motion test harness | 600 | Q01, M01 | Q | `e2e/src/support/motion.ts` (`settle`, throttle, frame-pacing and INP probes), the `motion-full` and `motion-reduced` projects, kill-switch fixture, the six gate scenarios, MO-01 to MO-09 specs, report artefacts |
| M08 | Pull to refresh on phone lists (Could) | 350 | R15, R30, M02 | T | gesture directive on the three phone lists, scroller `overscroll-behavior`, text fallback, spec; first to cut |

New slices total 4,550; growth 2,130; together **6,680 lines**, so the plan goes from 72 slices and 48,660 lines to 80 slices and 55,340 lines. M08 is Could.

### 7.3 Where they fit in the waves

At most three builds per wave and the plan's lane rules are kept. Waves are inserted after the wave that finishes their last dependency (shown as `5b`).

| Wave | Builds (lane, lines) | Notes |
|---|---|---|
| 2 | R05 grows (+60) | tokens land with the rest of the non-colour foundation |
| 3 | R07 (+100), R08 (+150) grow | buttons and skeletons are the first thing the owner sees move |
| 5b (new) | M01 (Y, 650) / M07 (Q, 600) | lanes free; M01 needs R07, M07 needs Q01 |
| 6b (new) | M02 (900) | new folder, no collision; waits for M01 |
| 7 | R13 (grows +250) | after M01; add M01 to its dependencies |
| 8 | R09 (+200) | add M02 to its dependencies |
| 8b (new) | M03 (S, 650) / M04 (H, 400) / M05 (U, 300) | disjoint lanes; M03 follows R12 and precedes L03 in lane S |
| 19b (new) | M06 (700) | lanes T and E after R30 and A15 |
| 20b (new, Could) | M08 (350) | cut first |
| Other grown slices | grow inside their own waves | R10 (4), R11 (6), R14 (10), R15 (11), A01 (5), A12 (11), A09 (15), A08 (14), A16 (19), A17 (20), A18 (20), A20 (23), A21 (21), Q10 (17), Q12 (19) |

Plan waves go from 25 to 29 (or 30 with M08). Added elapsed time, on the plan's own assumptions: 4 inserted waves of 1 to 1.5 days plus review and a gate half day is about 6 working days, about one calendar week, so the honest range becomes 10 to 11 weeks. The owner sees the first visible motion in wave 3 (shimmer, buttons), the full set (overlays, tabs, route, Home, sign-in) by wave 8b, and the table and editor polish last.

Gate additions: G1 (after wave 5) gets a fe-designer motion check on a real low-end Android phone and an iPhone; G3 (after wave 14) flips MO-05 from report-only to blocking; every slice that animates adds the row "motion" to the definition of done in plan 4.1: the pattern is in section 3, MO-01 and MO-02 are green, the gallery shows it (Q10) and reduced mode was looked at.

### 7.4 New rulings for plan section 0.2 (N15 is already taken by content containment, so these are N16 to N19)

| # | Ruling |
|---|---|
| N16 | Motion language is this file; the rows of section 1.2 replace C1, C2, C3, C7 (durations), C13 and the motion lines of D8, visual section 4, UX 7.6 and 11 item 3, and login visual section 11. |
| N17 | Animate `opacity` and `transform` (plus the named `clip-path` and `grid-template-rows` exceptions); colour, shadow and size effects are overlays. Enforced by `check-motion.mjs` and stylelint. |
| N18 | No animation library; no `@angular/animations`; `animate.enter` and `animate.leave`, CSS and WAAPI only. |
| N19 | `data-motion` modes (full, lite, reduced, off) are the single switch; reduced can only be requested, never bypassed. |

### 7.5 Cuts if time is short

In order: M08 (pull to refresh), the shared-element transition inside M03, chart draw-in (A20 +200), M06 drag feedback, count-up. Never cut: M01, M07 (the safety net), the Reduced mapping, and the R07 and R08 changes.

## 8. Open questions for the product owner

Defaults are what this document assumes (taste decisions already taken).

| # | Question | Default |
|---|---|---|
| M1 | Add an in-app "Animations: follow device / reduce" setting in Appearance (about 60 lines), for shared PCs where the OS setting is locked? | Yes. It can only reduce, never override the OS. |
| M2 | Press scale 0.97 on every button and a 2 px hover lift on interactive cards and Home tiles (pointer devices only): keep or remove? | Keep. Both are the first things that make the UI feel alive and neither moves layout. |
| M3 | Count-up on Home integers (400 ms, once, skippable). Keep, or drop as decoration? | Keep for integers only. Money never counts. |
| M4 | Shared-element transition from a list row to the document (status chip, and card to header on phones). Build it, or leave route transitions plain? | Build behind a flag in M03; keep only if it is clean on the low-end phone. |
| M5 | Pull to refresh on the three phone lists (M08). Build it? | Yes, last, and first to cut. |
| M6 | Dialog overshoot: the spring curve overshoots about 7 percent of the travel. Gentler (no overshoot, same timing) or as proposed? | As proposed; one token (`--ims-ease-spring`) changes it everywhere. |
| M7 | Sign-in entrance on every load, or once per browser tab session? | Every load (360 ms, never blocks typing). |
| M8 | Lite mode on weak devices is automatic and has no setting. Agree? | Yes; it only removes decoration and is stored locally. |

## Appendix. What the demo shows

`docs/design/assets/motion-demo.html` implements: the four button variants with hover overlay and press; save with busy and the success tick morph; a failed sign-in with the one shake and the disclosure banner; a dialog (spring) and a drawer (edge slide), which becomes a bottom sheet under 600 px; toasts with FLIP of the survivor; tabs and a segmented control with sliding indicators; the list with the 8-item stagger, expanding rows, add and remove with FLIP; a skeleton with the 300 ms delay and 400 ms minimum; counters; a bar and line chart draw-in; the Posted chip swap and success mark; form focus underline, validation disclosure, checkbox, radio, switch; the route bar trickle; theme crossfade through View Transitions; and a Motion switch (Full, Lite, Reduced, Off) that starts in Reduced when the operating system asks for it. The header shows the number of running animations, which is 0 on an idle page. It was exercised headlessly in Chrome at desktop and 360 px widths, light, dark and with reduced motion: no script errors, 0 running animations at rest.
