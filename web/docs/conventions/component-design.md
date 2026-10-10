# Component design

Signal-first, OnPush, no wrapper divs. Selector prefix `app` (`app-button`, `app-document-actions`).

## Class layout (top to bottom)

1. `private readonly x = inject(X);` all dependencies, alphabetical, never constructor parameters.
2. Signal inputs, with alias and `$` prefix: `$config = input.required<Config>({ alias: 'config' });`
3. Optional `viewChild` / `contentChildren` signals.
4. Outputs, `$` prefix and alias: `$changed = output<Value>({ alias: 'changed' });` (template: `(changed)="..."`).
5. Derived state: `computed()`. Prefer it over getters, `combineLatest` and manual flags.
6. Own state: `signal()`, form controls.
7. Lifecycle hooks (only the ones used), then event handlers (`onSave`, `onRowClick`), then private methods.

## Rules

- `ChangeDetectionStrategy.OnPush` on every component (lint-enforced).
- Control flow only: `@if`, `@for` (with `track`), `@switch`. No `*ngIf`.
- **No function calls in templates** except signal reads. Derive values in `computed()`; bind modifiers one by one
  (`[class.--active]="$isActive()"`).
- Host classes through the `host` metadata (`'[class]': '$hostClass()'`, built with `toClassStr`), styled with `:host`. No wrapper `div`.
- Subscriptions: `takeUntilDestroyed(destroyRef)`; side effects in `tap`, nothing but `.subscribe()` at the end.
- Hover and focus styling is CSS (`:hover`, `:focus-visible`), never mouse event handlers.
- Pages never call the HTTP client: they use a feature store (`QueryLoader` for reads, `ApiCommand` for writes).
- Forms: typed reactive forms, one `app-form-field` per control (label above, error text under, linked with `aria-describedby`).
- Accessibility: every icon-only button has `ariaLabel`; dialogs go through `app-dialog` (native `<dialog>`: focus trap, Esc);
  errors are announced (`role="alert"`), success is polite. Touch targets are >= 44px (48px on phones).
- Business rules do not live in components. Permissions come from `PermissionService`; document buttons from `allowedActions`.

## Tests

Spectator `createComponentFactory({ component, detectChanges: false })`; Spectator types `props` by property name while Angular
binds the alias, so cast the props object (`as never`) at that boundary. Test behaviour that matters (what is rendered for which
input), not smoke creation.
