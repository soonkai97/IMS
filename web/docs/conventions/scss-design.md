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

| Function / mixin                                                                                                         | Examples                                                                                                                                                                                |
| ------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----- | ------------------------------ | ------------- | -------------------- | --------------------- | -------- | --------- | ------------------------------ |
| `color('name')`                                                                                                          | `surface`, `surface-2`, `text`, `text-muted`, `border`, `primary`, `on-primary`, `danger`, `status-posted`, `on-status-posted`. Returns a CSS variable, so light/dark switch at runtime |
| `space(0..9)`                                                                                                            | 4px, 8px, 16px, 24px ... (8px grid)                                                                                                                                                     |
| `font-size(1..6)`, `font-weight('medium')`, `font-family('sans')`                                                        | 16px body is `font-size(3)` (inputs must stay >= 16px)                                                                                                                                  |
| `border-radius('sm'                                                                                                      | 'md'                                                                                                                                                                                    | 'lg'  | 'pill')`, `border-width('thin' | 'thick'       | 'focus')`, `shadow(1 | 2)`                   |          |
| `height('touch'                                                                                                          | 'touch-min'                                                                                                                                                                             | 'bar' | 'tabbar')`, `width('nav'       | 'content-max' | 'dialog'             | ...)`, `z-index('nav' | 'dialog' | 'toast')` | touch = 48px, touch-min = 44px |
| `easing('smooth')`, `duration('fast')`                                                                                   |                                                                                                                                                                                         |
| `@include media-phone / media-tablet / media-below-desktop / media-desktop`                                              | breakpoints 600px and 1024px                                                                                                                                                            |
| `@include focus-ring`, `visually-hidden`, `reset-button`, `control-base`, `truncate-line`, `clamp-lines($n)`, `disabled` |                                                                                                                                                                                         |

## Naming: flat BEM, component specific

- Block = component purpose, prefixed by the library area: `ui-button`, `document-editor-bar`, `stock-list-page-filters`
  (never `card`, `list`).
- Element = block + `-name` (`line-editor-row`). Modifier = `&.--name` on the same selector.
- **No class nesting.** If a child depends on a parent modifier, set a CSS custom property on the parent and read it in the child.
- Property order: `@include`, custom properties, normal properties, pseudo-classes, modifiers.
- `declaration-no-important` is on; the two exceptions in `globals/_base.scss` are commented.

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
page scroll at any width.

- `html` sets `overflow-wrap: anywhere` (`globals/_containment.scss`), so any text breaks anywhere and flex or grid children
  holding it shrink. Do not undo it, and never hide a symptom with `overflow-x: hidden` or `clip` on `html` or `body`.
- A flex or grid child that holds user text needs `min-width: 0` when it also sets its own width, wraps or truncates.
- Text that must stay short uses `@include truncate-line` (one line, ellipsis) or `clamp-lines($n)`, plus a `title` with the full value
  and a way to see it in full without a pointer (the detail page, the edit dialog). A literal `white-space: nowrap` fails
  `npm run lint:styles`; `truncate-line` is the one place it is allowed.
- `app-table` columns have a `type` (`name`, `text`, `code`, `number`, `status`, `date`, `action`) that sets the width and the cut
  (see `table.types.ts`). Plain-text cells are one line with an ellipsis; chips, numbers, dates and buttons are never cut.
- Only a deliberate scroll region may scroll sideways: give it `role="region"`, `tabindex="0"`, an accessible name and
  `data-scroll-region`. `e2e/tests/14-containment.spec.ts` fails on any other sideways scroller.
- A new list, card, chip, dialog, toast or header adds its screen to that test (long names in the data it creates).
