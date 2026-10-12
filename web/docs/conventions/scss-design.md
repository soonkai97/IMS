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

| Function / mixin                                                                                    | Examples                                                                                    |
| --------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| `color('name')`                                                                                     | `surface`, `text`, `text-muted`, `border`, `accent`, `danger`. A CSS variable (light/dark)  |
| `space(0..9)`                                                                                       | 4px, 8px, 16px, 24px ... (8px grid); `space(1.5)` is 12px                                   |
| `font-size(1..6)`, `font-weight('medium')`, `font-family('sans')`                                   | 16px body is `font-size(3)` (inputs must stay >= 16px)                                      |
| `border-radius('sm' \| 'md' \| 'lg' \| 'sheet' \| 'pill')`                                          | 6, 8, 12, 16 (sheet top corners), pill                                                      |
| `border-width('thin' \| 'thick' \| 'ring' \| 'edge')`                                               | 1, 2, 2 (focus ring), 3 (accent edge of banner, toast, line). `'focus'` is the old `'edge'` |
| `shadow(1 \| 2 \| 3)`                                                                               | `--ims-shadow-n`, per theme: 1 sticky bars, 2 menus and toasts, 3 dialogs and sheets        |
| `height('touch' \| 'touch-min' \| 'bar' \| 'tabbar')`                                               | touch = 48px, touch-min = 44px; `width('nav' \| 'dialog' ...)`, `z-index('nav' \| 'toast')` |
| `duration('fast' \| 'base' \| 'slow')`, `easing('standard' \| 'out')`                               | motion tokens, see Motion below                                                             |
| `@include media-phone / media-tablet / media-below-desktop / media-desktop`                         | breakpoints 600px and 1024px                                                                |
| `@include focus-ring`, `visually-hidden`, `reset-button`, `control-base`, `nowrap-text`, `disabled` |                                                                                             |

## Icons

`app-icon` draws the in-house 24x24 path set in `icon.paths.ts` (no icon library). Sizes are 16, 20, 24 (default) and 40 px
(`<app-icon name="check" [size]="20" />`); nothing else. The stroke is fixed in screen pixels (`vector-effect: non-scaling-stroke`):
1.5px at 16, 1.75px at 20 and above. An icon is decorative (`aria-hidden`): the meaning is in the text or the button's
`aria-label`. A new icon is appended to `icon.paths.ts` in alphabetical order by the slice that needs it.

## Motion

Three durations (`fast` 120ms: hover, exits, fades; `base` 200ms: entrances; `slow` 280ms: sheet and dialog travel) and two easings
(`standard` for moves, `out` for arrivals), emitted as `--ims-dur-*` and `--ims-ease-*` by `globals/_motion.scss`. Components use
`duration()` and `easing()`, never a literal. Animate only `opacity` and `transform`; colours change instantly (except the existing
button fade, until the button slice).

- Reduced motion: `prefers-reduced-motion: reduce` sets every duration to 1ms and runs every animation once for 0.01ms, so no loop
  keeps going (an infinite animation with a shortened duration used to flicker). There is no in-app switch.
- Tests: `<html data-motion="off">` gives the same result for Playwright. `page.emulateMedia({ reducedMotion: 'reduce' })` works too.
- A loop is finite wherever possible: the skeleton shimmer sweeps 6 times (9s) and stops. Only the busy spinner and progress bars
  loop, and only while loading. Name loop keyframes `ims-loop-*`.
- Loading indicators appear through `delayedLoading(loading)` (`@ims/utils`): shown after 300ms of waiting, kept at least 400ms.
- `@angular/animations` is not used (deprecated in Angular 22); ESLint rejects the import. Use CSS transitions, `animate.enter`
  and `animate.leave`.

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
