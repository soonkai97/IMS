# SCSS design

Tokens live in `libs/shared/styles` (`@ims/styles`) and are used through functions and mixins. **No hard-coded colours, spacing,
sizes or radii in component styles.**

```scss
@use 'libs' as *;

.stock-list-card {
  padding: space(2);
  color: color('text');
  background: color('surface');
  border: border-width('thin') solid color('border');
  border-radius: border-radius('md');
}
```

| Function / mixin                                                                                                         | Examples                                                                                    |
| ------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------- |
| `color('name')`                                                                                                          | `surface`, `text`, `text-muted`, `border`, `accent`, `danger`. A CSS variable (light/dark)  |
| `space(0..9)`                                                                                                            | 4px, 8px, 16px, 24px ... (8px grid); `space(1.5)` is 12px                                   |
| `font-size(1..6)`, `font-weight('medium')`, `font-family('sans')`                                                        | 16px body is `font-size(3)` (inputs must stay >= 16px)                                      |
| `border-radius('sm' \| 'md' \| 'lg' \| 'sheet' \| 'pill')`                                                               | 6, 8, 12, 16 (sheet top corners), pill                                                      |
| `border-width('thin' \| 'thick' \| 'ring' \| 'edge')`                                                                    | 1, 2, 2 (focus ring), 3 (accent edge of banner, toast, line). `'focus'` is the old `'edge'` |
| `shadow(1 \| 2 \| 3)`                                                                                                    | `--ims-shadow-n`, per theme: 1 sticky bars, 2 menus and toasts, 3 dialogs and sheets        |
| `height('touch' \| 'touch-min' \| 'bar' \| 'tabbar')`                                                                    | touch = 48px, touch-min = 44px; `width('nav' \| 'dialog' ...)`, `z-index('nav' \| 'toast')` |
| `duration('fast' \| 'base' \| 'slow')`, `easing('standard' \| 'out')`                                                    | motion tokens, see Motion below                                                             |
| `@include media-phone / media-tablet / media-below-desktop / media-desktop`                                              | breakpoints 600px and 1024px                                                                |
| `@include focus-ring`, `visually-hidden`, `reset-button`, `control-base`, `truncate-line`, `clamp-lines($n)`, `disabled` |                                                                                             |

## Icons

`app-icon` draws the in-house 24x24 path set in `icon.paths.ts` (no icon library). Sizes are 16, 20, 24 (default) and 40 px
(`<app-icon name="check" [size]="20" />`); nothing else. The stroke is fixed in screen pixels (`vector-effect: non-scaling-stroke`):
1.5px at 16, 1.75px at 20 and above. An icon is decorative (`aria-hidden`): the meaning is in the text or the button's
`aria-label`. A new icon is appended to `icon.paths.ts` in alphabetical order by the slice that needs it.

## Motion

Three durations (`fast` 120ms: hover, exits, fades; `base` 200ms: entrances; `slow` 280ms: sheet and dialog travel) and two easings
(`standard` for moves, `out` for arrivals), emitted as `--ims-dur-*` and `--ims-ease-*` by `globals/_motion.scss`. Components use
`duration()` and `easing()`, never a literal. Animate only `opacity` and `transform`; colours change instantly. Stylelint rejects a
`transition` or `transition-property` that names colour, background, border, outline, shadow, filter, width, height, margin, padding
or `all`.

- Reduced motion: `prefers-reduced-motion: reduce` sets every duration to 1ms and runs every animation once for 0.01ms, so no loop
  keeps going (an infinite animation with a shortened duration used to flicker). There is no in-app switch.
- Tests: `<html data-motion="off">` gives the same result for Playwright. `page.emulateMedia({ reducedMotion: 'reduce' })` works too.
- A loop is finite wherever possible: the skeleton shimmer sweeps 6 times (9s) and stops. Only the busy spinner and progress bars
  loop, and only while loading. Name loop keyframes `ims-loop-*`.
- Loading indicators appear through `delayedLoading(loading)` (`@ims/utils`): shown after 300ms of waiting, kept at least 400ms.
- `@angular/animations` is not used (deprecated in Angular 22); ESLint rejects the import. Use CSS transitions, `animate.enter`
  and `animate.leave`.

## Buttons and form controls

- `app-button` variants: `primary` (the one main action), `secondary` (default), `danger`, `danger-outline` (a destructive action
  beside a primary one), `ghost` (quiet, toolbars and rows). Rest, hover and pressed colours change at once; pressed also scales to
  `press-scale()` (a `transform` transition of `fast`); the token is 0.97 and 1 under reduced motion and `data-motion="off"`. Hover is
  guarded by `@media (hover: hover)` so a touch screen never shows a stuck hover.
- Busy: `loading` sets `aria-busy` and `aria-disabled` at once and ignores clicks, focus stays. The spinner appears after 150ms
  (`delayedLoading`, no minimum time: it leaves with the answer), replaces the leading `app-icon`, and the button keeps the width it
  had (`min-width`/`max-width` in px). The grid column is `minmax(0, 1fr)`, so a text-only label plus spinner spills evenly into the
  padding instead of out of one edge. Colours do not change while busy.
- `[saved]` shows a check and "Saved" for 1.5s (an action that finishes on the same screen); the button does not shrink while it
  shows and ignores clicks. A label shorter than "Saved" grows the box just enough to fit it. Announcing the result (a live region)
  is the caller's job.
- Windows high contrast: the checked checkbox, radio and segment opt out of forced colours and use `Highlight`/`HighlightText`.
- Disabled: `disabled` for an action that does not exist for this user's state; `ariaDisabled` + `describedBy` when the reason
  matters (the button stays focusable).
- Text-like controls share the `control-base` mixin: hover border `text-muted`, focus border `accent` plus the ring, error border
  `danger` plus a 1px inset ring (and the icon and sentence from `app-form-field`), disabled and read-only on `surface-sunken`.
- `app-checkbox` and `app-radio-group` draw their own 24px box (`border-strong` edge, `accent` fill, tick or dot); the row is the
  44px target. `app-radio-group variant="segmented"` is one track with equal options, still native radios.

## Shell, tab bar, action bar, tabs

- Phone: top bar, bottom tab bar (`$profile().phoneTabs` plus "More"; links with `aria-current`, not a tablist), content. Tablet: icon
  rail. Desktop: sidebar. The current page is shown by shape (a 3px pill above the tab icon, a 3px edge in the sidebar), not colour only.
- Safe areas: `viewport-fit=cover`; `--ims-safe-*` are the insets and `tabbar-offset()` is the tab bar height plus the bottom inset.
  Components never write `env()`.
- Sticky action bar (`.document-editor-bar`): phone = full-width strip directly above the tab bar; tablet and up = floating card. It
  publishes its real height as `--ims-action-bar-height` (`appPublishHeight`; it grows when the buttons wrap at 320px) and the `html`
  scroll padding reserves tab bar + that height, so a focused field is scrolled clear of it (e2e `16-shell`). Toasts: R11.
- Route fade: `withViewTransitions(ROUTE_FADE)`, one `fast` crossfade; top bar, sidebar and tab bar have a `view-transition-name` and
  stay still; query-only navigations skip it; 1ms under reduced motion. Never name content elements (a named element becomes the
  containing block of fixed descendants).
- Top progress bar: behind `delayedLoading`, `role="progressbar"` without a value, named "Loading".
- Tabs: `<app-tabs panel="id">` plus `<div appTabPanel="id" [labelledByTab]="active">` around what it controls (`aria-controls`,
  `role="tabpanel"`, `aria-labelledby`). Arrows, Home and End move.

## Naming: flat BEM, component specific

- Block = component purpose, prefixed by the library area: `ui-button`, `document-editor-bar`, `stock-list-page-filters`
  (never `card`, `list`).
- Element = block + `-name` (`line-editor-row`). Modifier = `&.--name` on the same selector.
- **No class nesting.** If a child depends on a parent modifier, set a CSS custom property on the parent and read it in the child.
- Property order: `@include`, custom properties, normal properties, pseudo-classes, modifiers.
- `declaration-no-important` is on; the exceptions (`[hidden]` in `globals/_base.scss`, the reduced-motion rules in `globals/_motion.scss`) are commented.

## Dark mode and contrast

Palette maps in `variables/_palette.var.scss` (`$light`, `$dark`) are emitted by `globals/_themes.scss` as `--ims-color-*`,
following `prefers-color-scheme` unless `<html data-theme>` forces a theme. `npm run contrast` fails if any text/background pair
drops below WCAG AA (4.5:1 text, 3:1 UI) or an alias has no target. Status chips always carry an icon and text, never colour
alone.

Token names are roles (`canvas`, `surface-sunken`, `accent`, `success`, ...). The names of before the 2026 refresh (`bg`, `surface-2`,
`surface-3`, `primary`, `primary-soft`, `warning-border`, `status-*`, ...) are aliases: `color('primary')` returns
`var(--ims-color-accent)`. New code uses the canonical names. A hex value is allowed only in `_palette.var.scss` (stylelint
`color-no-hex`).

## Long values never widen the page (L00)

Names, remarks and codes can be hundreds of characters, unbroken, in any script. A long value must never cause horizontal
page scroll at any width, and a short one (a number, a date, a code, a chip) must never be broken or cut.

- `html` sets `overflow-wrap: break-word` (`globals/_containment.scss`): a word that does not fit is broken at the line end, so
  min-content widths stay honest. Free text that really holds long unbroken runs (names, remarks, e-mail addresses, URLs)
  opts in with `@include wrap-anywhere` (`overflow-wrap: anywhere` and `min-width: 0`, so it also shrinks inside a flex or
  grid row). Never put `anywhere` on numbers, headings or whole pages. Never hide a symptom with `overflow-x: hidden` or
  `clip` on `html` or `body`.
- Numbers, money, quantities, dates, codes, chip and tab labels: `@include keep-whole` (`white-space: nowrap`, no cut). A long
  one makes its container scroll, not the page. Text that is shown in full somewhere else (a menu label) may use
  `truncate-line` (one line, ellipsis). `keep-whole` is the only place a literal `white-space: nowrap` is allowed (also
  `text-wrap: nowrap` and `text-wrap-mode: nowrap`, any case): everywhere else `npm run lint:styles` fails, and so does a
  copy of it in another file. `visually-hidden` keeps its own commented exception. Inline styles in templates are not
  linted: do not write `nowrap` there.
- A user's text that is cut after a few lines goes through `app-clamped-text` (`clamp-lines` plus a real "Show more" / "Show
  less" button with `aria-expanded` and `aria-describedby` pointing at the text, shown only when the text overflows), so it is
  readable by keyboard and by touch. The
  button is left out where the full value is one click away (a row's title that links to its detail page). `title` is not a
  way to read a value: it needs a mouse.
- `app-table`: columns have a `type` (`name`, `text`, `code`, `number`, `status`, `date`, `action`, see `table.types.ts`). From 1024px
  the table is as wide as its columns need. Cells of type `number`, `date`, `code`, `status` and `action` are `keep-whole` and size to
  their content (the header has `width: 1%` and may wrap at a space, never inside a word); a plain-text code is as wide as its
  value up to `width('code-max')` and wraps inside that box beyond it, so one 64-character SKU cannot push the other columns out
  of view. `name` and `text` cells have a minimum width (`width('column-min')`) and are clamped to two lines by
  `app-clamped-text`. A low-priority column can set `hideOnNarrow` (hidden from 1024 to 1279px). When the columns still need
  more than the page leaves, the table scrolls inside its own box, which shows a soft shadow on the side with more to see and,
  only while it really scrolls, is a labelled, focusable region (`appScrollRegion`: `role="region"`, `tabindex="0"`,
  `data-scroll-region`), so a table that fits has no extra tab stop. The page never scrolls. Chips are never cut: they hold
  short enumerated labels, never user text. Below 1024px a row is a card and every value may wrap.
- Only a deliberate scroll region may scroll sideways, and it is focusable and named (`appScrollRegion` does this for a box that
  scrolls only sometimes). `e2e/tests/15-containment.spec.ts` fails on any other sideways scroller; its one exemption is the tab
  strip of `app-tabs` (focusable tabs, scrolls on a narrow screen).
- `e2e/tests/15-containment.spec.ts` also fails when, at 1024px and wider, a number, date, code, action or chip cell is broken
  across lines or clipped. A new list, card, chip, dialog, toast or header adds its screen to that test (long names and a large
  amount in the data it creates).
