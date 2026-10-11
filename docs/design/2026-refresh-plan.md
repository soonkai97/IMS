# IMS 2026 design refresh - build plan

Author: fe-architect. Date: 2026-10-11. Status: ready for fe-framework (build), behavior-test-engineer (e2e), qa-* (re-checks), be-framework (section 3, all optional).
Merges `docs/design/2026-refresh-ux.md` (S1..S11, FR/RA/RQ/DK/DN/CP/FB/TB acceptance IDs) and `docs/design/2026-refresh-visual.md` (steps 1..11, tokens, contrast) into one ordered list.
Inputs also read: `docs/design/mvp-ux.md`, `docs/architecture/frontend.md`, `web/README.md`, `web/docs/conventions/*`, `docs/qa/retest-power-supervisor.md` (PR-01..PR-14), `docs/qa/fix-status-frontend.md`, `docs/team/PROTOCOL.md`, the real code under `web/libs/`, `web/tools/libs.mjs`, `e2e/`. `docs/qa/retest-edge-accessibility.md` does not exist yet; when it lands, its findings are triaged into R22/R24 or a new slice (nothing in this plan waits for it).

All product-owner defaults in both specs are accepted (UX Q1..Q12, visual Q1..Q10), plus these decisions folded in everywhere below:

| # | Decision | Lands in |
|---|---|---|
| D1 | Money shows exactly 2 decimals on screen, thousands separator, never abbreviated; no sub-cent input. Quantities keep each product's own precision: up to 4 decimals (`MAX_QTY_DECIMALS = 4`, the API limit), trailing zeros trimmed. The UX file says "3 decimals" (FR-05, D10); the code and API say 4. The code wins: 3 would silently round a stored 4-decimal quantity. | R04 |
| D2 | "RM" appears only in column headers and labels ("Unit cost (RM)"), never as a prefix on a value. | R04 |
| D3 | Login error banner clears when either field is edited and is not kept across a reload; it never shows together with empty-field messages. | R02 |
| D4 | Required = red asterisk (`aria-hidden`) + screen-reader-only "required" + `required`/`aria-required` + one legend "* means required" per form. | R02 |
| D5 | Staff never see cost or value; Clerk sees cost (`cost.view`) but never valuation (`valuation.view`). Decided by permission, never role name. Every new surface (Home widgets, palette, skeletons, aria-labels, titles) is checked for this. | R15, R17, R18, R19, R20 |
| D6 | Reports: the date-range error message is shown (PR-03); Product, Unit and Unit cost (with `cost.view`) are on screen and SKU/date never wrap (PR-06, PS-22). | R03 |

## 0. Where the two specs disagree, and the ruling

The UX file wins on behaviour, the visual file on look (their own rule). Where they contradict on a detail, this table is the single answer; implementers do not re-argue it.

| # | Topic | UX says | Visual says | Ruling |
|---|---|---|---|---|
| C1 | Skeleton animation | opacity pulse 1.0 to 0.55, no shimmer sweep (do-not-do 3) | shimmer gradient sweep | Pulse. Static 0.7 opacity under reduce-motion. No gradient anywhere. |
| C2 | Button pressed | colour step, no scale | `scale(0.98)` 80ms | No scale. Hover and pressed colours change instantly (accepted: no hover fade). Drops the `duration('press')` token. |
| C3 | Hover/focus colour transition | 120ms | instant | Instant. Only `opacity` and `transform` ever animate. |
| C4 | Density breakpoint | >= 1024px | fine pointer and >= 600px | Both: `@media (min-width: 1024px) and (pointer: fine)`. Below that the setting is ignored and its controls are hidden (DN-02, DN-06). Compact table text is 15px (visual step 2), which satisfies "14px, never smaller". |
| C5 | Where theme/density are stored | per user, in this browser, plus a device copy for first paint | key `ims.theme` / `ims.density`, per device | Per user key (`ims.pref.<userId>.theme`) plus the device copy (`ims.theme`, `ims.density`) that `theme-init.js` reads. At sign-out the device copy is reset to `system`/`comfortable`. |
| C6 | Toast position on phones | directly under the top bar (NV-21 fix, FB-09) | above the tab bar, shell publishes `--ims-bottom-offset` | Under the top bar (QA evidence). No `--ims-bottom-offset`. |
| C7 | Dialog / sheet motion | desktop fade 120ms only, sheet 200ms in / 120ms out | desktop fade + 8px translate 200ms, sheet 280ms | UX. No `duration('slow')` token. Palette fade 120ms in / 100ms out. |
| C8 | Row highlight | tint 120ms in, 1500ms hold, 1000ms out; reduce-motion tint 1.5s no fade | overlay opacity 1200ms | UX values. Colour is the existing `accent-soft`; the UX file's new `highlight` token is not created. |
| C9 | Palette trigger | placeholder "Search or run a command", 240px, 40px high | "Search or jump to...", 20rem | UX copy and width; visual styling. Scrim 35% (visual). |
| C10 | Palette code placement | `core` (needs identity and navigation) | `shared/ui` | Both: presentational `app-command-palette` and `app-kbd` in `shared/ui`; state, commands, recents, trigger and Ctrl+K in `core/navigation`. |
| C11 | `status-*` aliases | n/a | keep literal hex copies, script asserts equality | Aliases only (a copy can drift). The alias map points `status-posted` to `success-soft` and so on; the script asserts every alias target exists. |
| C12 | Theme / density control look | radio group with a "Now: Dark" sub-line | segmented control with icons | Radio-group semantics, segmented look (`variant="segmented"` on `app-radio-group`, added in R08), UX copy. |
| C13 | Progress bar loop | 1s | 1.2s | 1.2s, `transform` based. |

## 1. Ordered slice list

Sizes are changed frontend lines including specs, excluding lockfiles, `web/openapi/**` and the generated `api-client.ts`; the e2e folder is counted separately and is small. Every slice must stay under 1,200 (hard cap, split if it grows past 1,000 during the build). Total about 14,800 lines in 24 slices.

Legend: `UX` = slice in the UX file, `V` = step in the visual file. "Re-check" = the QA personas that must run afterwards (`novice` = qa-novice-warehouse-staff, 360px phone; `power` = qa-power-supervisor, desktop; `edge` = qa-edge-accessibility).

| Slice | Name | Covers | Size | Depends on | Wave |
|---|---|---|---|---|---|
| R01 | Palette, aliases, contrast gate | V1 | 310 | none | 1 |
| R02 | Required marker, legend, login behaviour | UX S1, D3, D4 | 600 | none | 1 |
| R03 | Reports fixes | PR-03, PR-06, PS-22, D6 | 350 | none | 1 |
| R04 | Money format and input rules | D1, D2, FR-05 | 450 | R02 | 2 |
| R05 | Non-colour token foundation and icons | V2, token parts of V3 and V4 | 600 | R01 | 2 |
| R06 | Preferences, theme per user, no flash, PWA colours | UX S2, V5 (theme half) | 700 | R01 | 2 |
| R07 | Reduce-motion contract, spinner, skeleton primitives, delay helper | UX S3 (part), V3 | 350 | R05 | 3 |
| R08 | Buttons and form controls (states, busy, success, segmented) | V6, UX 7.2 | 750 | R01, R02, R05 | 3 |
| R09 | Tables, lists, chips | V7 | 650 | R01, R05 | 3 |
| R10 | Shell, tab bar, action bar | V9 | 400 | R05, R06 | 3 |
| R11 | One failure, one announcement | UX S4 (part) | 850 | R08 | 4 |
| R12 | Density | UX S6, V4, V5 (density half) | 750 | R06, R09, R10 | 4 |
| R13 | Overlays: dialog, sheet, toast, banner, card, empty state | V8 | 600 | R07, R11 | 5 |
| R14 | Saved state, row highlight, live regions, error summary | UX S4 (rest) | 650 | R08, R11 | 5 |
| R15 | Skeleton shapes per screen, loading rules | UX S3 (rest), V8 (skeleton) | 800 | R07, R09, R12, R13 | 6 |
| R16 | Command palette component and `app-kbd` | V10 (a) | 500 | R05, R13 | 6 |
| R17 | Role-aware Home: Doers | UX S5 | 900 | R04, R15 | 7 |
| R18 | Command palette: logic, trigger, recents, actions | UX S8, V10 (b) | 750 | R06, R10, R12, R16 | 7 |
| R19 | Role-aware Home: Reviewer, Overseer, Admin | UX S7 | 900 | R17 | 8 |
| R20 | Palette search (documents, products) and phone button | UX S9 | 700 | R18 | 8 |
| R21 | Paging: First/Last and page size | UX S11 (part) | 500 | R12, R15 | 8 |
| R22 | Keyboard extras | UX S10 | 800 | R18, R21 | 9 |
| R23 | Date field `dd MMM yyyy` | UX S11 (part), NV-27, PS-15 | 600 | R03, R08 | 9 |
| R24 | Clean-up: remove aliases, rename call sites, docs | V11 | 300 | all others | 10 |

Not covered here on purpose (not part of this refresh): PR-01 (backend valuation defect), PR-02 / PS-11 / Q12 (Cancel on Approved stays unchanged), PR-04, PR-05, PR-07 (no "stock without cost" widget), PR-08, PS-14.

Every slice in addition to its own tests keeps FR-01..FR-09 true (UX section 0.2). Per-slice "Definition of done" is section 4; it is not repeated below.

### R01 Palette, aliases, contrast gate

- Goal: the whole app takes the new calm palette (light and dark) with zero component edits; the contrast gate now tests the new token set and the aliases, so every later slice is protected.
- Files: `web/libs/shared/styles/src/variables/_palette.var.scss` (new `$light`/`$dark` maps, new `$aliases` map old to new), `web/libs/shared/styles/src/variables/_color.var.scss` (`color()` resolves an alias to its canonical custom property; unknown name still `@error`), `web/scripts/check-contrast.mjs` (replace with the Appendix A pair list of the visual file, 81 pairs x 2 themes; it must also fail when an alias target does not exist, and read the palette maps like today), `web/.stylelintrc.json` (`color-no-hex` for every SCSS file except the palette), `web/apps/main/project.json` (initial budget, section 4.3), `e2e/tests/11-accessibility.spec.ts` (run the screens in both colour schemes through `page.emulateMedia`). `_themes.scss` is NOT touched: it already emits every key of the maps.
- Size: 310.
- Depends on: none.
- Acceptance:
  - `npm run contrast` prints 162 checks, 0 failing; deleting an alias target makes it exit 1.
  - `git diff --stat` of the PR shows no component `.scss`, `.html` or `.ts` change except the files above (proves the alias drop-in).
  - `npm run lint:styles` passes; adding `color: #fff;` to any component SCSS fails it.
  - Accent is blue `#2447c4` (light) / `#8fb3ff` (dark); Approved chip is the info tint, Submitted the accent tint, Reversed the warning tint.
  - FR-01: axe 0 serious/critical on the 11-accessibility screens in light AND dark.
  - `build:prod` passes under the new budget (warning 600 kB, error 650 kB).
- Tests: the contrast script is the test; extend e2e `11-accessibility` to loop `colorScheme: 'light' | 'dark'`.
- Re-check: edge (contrast, dark), power (status chips on lists).

### R02 Required marker, legend, login behaviour

- Goal: D4 and D3. The smallest Must item; ships first because it fixes a known accessibility defect.
- Files: `web/libs/shared/ui/src/lib/components/form-field/*` (asterisk span `aria-hidden`, sr-only word from `UI_TEXT.required`, remove the "(required)" text; the field publishes `required` to its control through `FORM_FIELD` in `form-field.token.ts`), `input/`, `select/`, `textarea/`, `radio-group/` (apply `required` or `aria-required`; radio `legend` carries the asterisk), new `web/libs/shared/ui/src/lib/components/form-legend/` (`app-form-legend`, `aria-hidden`, the text from `UI_TEXT.requiredLegend`), `web/libs/shared/ui/src/lib/tokens/ui-text.token.ts` and `web/libs/core/i18n/src/lib/services/ui-text.provider.ts`, the three catalogs (`form.requiredLegend`; reuse `ui.required`), and the forms that need `<app-form-legend />` as their first element: `libs/containers/authentication/.../login`, `documents/.../document-editor` and `line-editor` (and the Add item sheet), `catalog/.../product-edit`, `catalog-lists`, `warehousing/.../warehouses`, `partners`, `administration/.../users`, `settings`, and every dialog that has a required reason (`confirm-dialog`). Login: `login.component.ts/.html`.
- Decision (the API contract the UX file asked fe-architect for): the legend is an explicit `<app-form-legend />` placed by the page or dialog (not injected by `app-form-field`). A form with no required field simply does not place it. A unit test per container asserts "has a required field implies has the legend".
- Login rules (D3): `auth.login.reset()` on any `valueChanges` of either control, and before an invalid submit, so the banner and the field messages are never visible together; nothing about the error is kept across a reload (the state is in memory; add a test that proves no storage write). The session-expired warning banner is unchanged.
- Size: 600.
- Depends on: none (uses the old token names; works with or without R01).
- Acceptance: RQ-01 to RQ-06 (UX 3.3). In addition: Login shows no banner after typing a character in either field; Sign in with both fields empty shows the two field messages and no banner even if a banner was visible before; a failed sign-in with both fields filled shows only the banner; reload shows neither; the accessibility name of the username field is "Email or username required" (one occurrence of "required"); `(required)` no longer appears anywhere (`grep` the catalogs and templates).
- Tests: Spectator on form-field (required marker, sr-only text, attribute on control), radio-group legend, legend component, login (the four rules above); one container test per form (legend present); e2e new `14-required-marker` (login, receipt, issue, one dialog: accessible names, legend count) and update `08-session` for the banner rules; page object `LoginPage` locators already use `^Email or username`, keep them.
- Re-check: edge (screen reader names, 320px wrap RQ-06), novice (forms on phone).

### R03 Reports fixes

- Goal: D6. PR-03 (From after To shows "Something went wrong"), PR-06 and PS-22 (SKU and date wrap; Product, Unit and Unit cost not on screen).
- Files: `web/libs/containers/reports/src/lib/reports/reports.component.{ts,html,scss,spec.ts}`, `web/libs/features/reports/src/lib/services/reports.store.ts`, `web/libs/shared/utils/src/lib/forms/server-errors.ts` (only if the `errors.to` mapping helper needs a small addition), the three catalogs (`filter.rangeInvalid` = "From must be on or before To."). No change to `app-table`: the nowrap goes in the cell templates the Reports page already declares (`appCell`), styled from `reports.component.scss`, so R03 does not collide with the table lane.
- Behaviour: a client validator on the two date controls blocks the request and shows "From must be on or before To." under the To field; if the API answers 400 with `errors.to` the same field shows the server sentence; in both cases the table is cleared (no stale rows) and the pager is hidden. Columns for the movements table: Posted, Document (with the Reversal badge), Type, Status, Warehouse, Partner (may wrap to a second line), SKU, Product, Qty, Unit, Stock effect, Unit cost (only `cost.view`, the existing `columnWhen`), Amount (only `cost.view`). SKU, document number and date are `white-space: nowrap`.
- Size: 350.
- Depends on: none.
- Acceptance: with From 12 Oct 2026 and To 10 Oct 2026 no "Something went wrong" and no Retry; the To field shows the sentence (announced once, `aria-describedby`); the old "1-6 of 6" footer is gone; correcting the dates reloads the table. At 1280 wide the supervisor sees every column including Amount without horizontal scroll inside the region; at 1024 the region scrolls but SKU and date stay on one line (TB-01, TB-05). Product, Unit and Unit cost are visible to Supervisor and Owner and Admin; the Unit cost column is absent for any user without `cost.view`.
- Tests: reports component spec (range error client and server, columns per permission, cleared table); e2e new `15-reports-range` (supervisor: bad range message; wide table columns; no wrap measured by `boundingBox().height` of the SKU cell equal to one line).
- Re-check: power (PR-03, PR-06, PS-22 closed), edge.

### R04 Money format and input rules

- Goal: D1 and D2 everywhere on screen and in inputs.
- Files: `web/libs/core/i18n/src/lib/pipes/format.pipes.ts` (+spec: `AppMoneyPipe` already prints 2 decimals; change `null`/`undefined` to render nothing instead of `-` for money, and keep `-` for dates and quantities), `web/libs/shared/utils/src/lib/constants/format.const.ts` (comment only; `MONEY_DECIMALS = 2` stays), new `web/libs/shared/utils/src/lib/forms/decimals.validator.ts` (`maxDecimals(n)` + the message key `validation.maxDecimals` = "Enter an amount with at most 2 decimals."), `web/libs/containers/documents/src/lib/line-editor/line-editor.component.{html,ts}` (unit cost: `inputmode="decimal"`, `step="0.01"`, validator), `web/libs/containers/administration/src/lib/settings/settings.component.{html,ts}` (rule amount threshold: same), the catalogs (column headers and labels get "(RM)": unit cost, line cost, amount, avg cost, value, stock value, "Amount from"), plus an audit of every template that prints money (`inbox`, `document-list`, `document-editor`, `stock-list`, `reports`, `home`, `settings`) so none uses a raw number, `toFixed` or a `number` pipe.
- Size: 450.
- Depends on: R02 (same two form templates; merge R02 first to avoid a conflict in `line-editor.component.html` and `settings.component.html`).
- Acceptance: FR-05, TB-02. `12400` shows `12,400.00`, `119371.9` shows `119,371.90`, `0` shows `0.00`, one million shows `1,000,000.00` in en, zh and ms; no value is ever abbreviated; typing `12.345` or pasting `12.3456` in unit cost or amount-from shows the message and blocks Save/Submit; `10`, `10.5`, `10.50` are accepted; a quantity of `1.2345` still shows `1.2345`, `2.500` shows `2.5` (unchanged). A value the user may not see is not rendered (no `0.00`, no `-`, no empty column) - unchanged and re-asserted in the tests.
- Tests: pipe spec table (the values above, in en, zh and ms; null renders nothing), validator spec (the visual file's true minus sign U+2212 for negative money is a polish item that is not part of this plan; negative money keeps today's sign), line-editor and settings specs (blocked save), extend `13-cost-visibility` (Clerk unit cost input rejects 3 decimals).
- Re-check: power (numbers reconcile with the Excel export), edge (input errors).

### R05 Non-colour token foundation and icons

- Goal: land every non-colour token once, so later slices only consume them and the shared style files stop being merge hotspots.
- Files: `web/libs/shared/styles/src/variables/_shape.var.scss` (radius `md` 8px, `lg` 12px, `sheet` 16px; `ring` 2px and `edge` 3px border widths; `focus` stays an alias of `edge`; `shadow(1..3)` returning `var(--ims-shadow-n)`), `_font.var.scss` (`semibold` 600, `bold` alias), `_font-size.var.scss` (line heights per step), `_space.var.scss` (`space(1.5)`), `_layout.var.scss` (motion: `duration('hold')` 1500ms, `duration('loop')` 1400ms, `easing('standard'|'enter'|'exit'|'linear')`, `smooth` alias; `height('actionbar')` 64px; `width('palette')` 40rem; `z-index('palette')` 45; `height('touch')` becomes `var(--ims-control-height)`), `globals/_themes.scss` (shadow custom properties per theme; the density custom properties `--ims-control-height`, `--ims-control-pad-x`, `--ims-row-height`, `--ims-cell-pad-y`, `--ims-card-pad`, `--ims-stack-gap` with COMFORTABLE values equal to today, plus the complete compact block under `@media (min-width: 1024px) and (pointer: fine)` selected by `[data-density='compact']`; nothing sets that attribute until R12, so R12 never edits this file), `globals/_base.scss` (html `background: canvas`, `color-scheme: light dark`, body line height 1.5, tabular numerals rule; the reduce-motion block is NOT touched here, that is R07), `mixins/_a11y.mixin.scss` (`focus-ring` uses `ring`; new `numeric` mixin), `web/libs/shared/ui/src/lib/components/icon/*` (stroke fixed in screen pixels with `vector-effect: non-scaling-stroke`, 1.5px at 16, 1.75px at 20 and 24; sizes 16/20/24/40 only) and `icon.paths.ts` (add `monitor`, `command`, `density-comfortable`, `density-compact`, `lock`).
- Size: 600.
- Depends on: R01.
- Acceptance: comfortable layout is metrically unchanged where the tokens did not change (control height 48, tab bar 64, row 48 measured in the browser at 360 and 1280); radius and line height changes are the only visible differences; no horizontal scroll at 320 and 400% zoom on the screens of `10-phone`; every old function argument (`border-width('focus')`, `easing('smooth')`, `font-weight('bold')`) still compiles; FR-02 targets unchanged; tables' right-aligned cells and the Home stat values render with `font-variant-numeric: tabular-nums`; the five new icons render at 20px in a unit test of the icon registry.
- Tests: icon spec (every name in the paths file renders; size input accepts only the four sizes), Spectator class check for the numeric mixin users (table `--end` cell, stat-card value); e2e `10-phone` and `11-accessibility` unchanged and green.
- Re-check: novice (phone layout shift), edge (400% zoom).

### R06 Preferences, theme per user, no flash, PWA colours

- Goal: UX S2 (DK-01..DK-10) and the theme half of V5. Introduces the per-user preference store that density and recents reuse.
- Decision: a new library `core-preferences` (`@ims/core/preferences`), registered in `web/tools/libs.mjs` and scaffolded with `node tools/scaffold-libs.mjs`. It holds `UserPreferenceStore` (typed get/set per signed-in user id under `ims.pref.<userId>.<key>`, every access in `try/catch`, in-memory fallback when storage is blocked, `storage` event for cross-tab updates, a `clear(userId, key)` used at sign-out). `ThemeService` (stays in `core-theme`) is rebuilt on it. Four consumers (theme, density, recents, and the existing list scope) justify a lib; the alternative (putting the store in `core-identity`) mixes auth with UI settings. The existing `libs/features/documents/src/lib/services/list-scope.preference.ts` migrates to the store in this slice only if it is a trivial swap; otherwise it stays and is listed in R24.
- Files: new `web/libs/core/preferences/**`, `web/libs/core/theme/src/lib/theme.service.ts` (+spec), `web/apps/main/src/index.html` (`<meta name="color-scheme" content="light dark">`, two `theme-color` tags with `media="(prefers-color-scheme: ...)"` set to the `surface` of each theme, the blocking `<script src="assets/theme-init.js">` before the stylesheet), new `web/libs/shared/assets/src/assets/theme-init.js` (about 400 bytes, reads the device copy in a `try`, sets `data-theme` and the theme-color tags), `web/apps/main/ngsw-config.json` (add `/assets/theme-init.js` to the prefetched `app` group; the existing glob `/*.js` does not match a file under `assets/`), `web/libs/shared/assets/src/manifest.webmanifest` (`theme_color` `#ffffff`, `background_color` `#f4f5f7`; the install splash stays light), `web/libs/core/navigation/src/lib/components/shell.component.{html,ts,spec.ts}` (the account dialog's theme group becomes "Appearance": Use device setting with the "Now: Dark" sub-line, Light, Dark; this is the only shell edit in R06), `web/scripts/check-contrast.mjs` (assert the two `theme-color` hex values in `index.html` and the manifest colours equal the palette `surface` and `canvas`), the catalogs (`shell.appearance`, `theme.system`, `theme.systemNow`, `theme.light`, `theme.dark`).
- Behaviour notes the builder must follow: device-level copy is written on every change; at sign-out and on the login page the device copy is `system`; after sign-in the user's own preference is applied; "system" mode sets no attribute (CSS decides, so the OS switch is live); changing the preference updates other tabs within a second.
- Size: 700.
- Depends on: R01 (the surface hex values in `index.html`).
- Acceptance: DK-01..DK-10 (UX 4.5). Security: `security-engineer` confirms in `docs/security/web-headers.md` that a same-origin classic script is allowed by `script-src 'self'` (it is; no CSP change) and that the production build keeps `inlineCritical: false`; the service-worker precache lists `theme-init.js`. First paint check (DK-03): Playwright with `colorScheme: 'light'` and `ims.theme=dark` in storage: the `html` background at `domcontentloaded` is the dark `canvas` `rgb(14, 18, 23)`.
- Tests: Jest for the store (per user keys, blocked storage, cross-tab event, clear), ThemeService (system/light/dark, sign-out reset, user switch), shell account dialog; e2e new `16-appearance` (DK-01, DK-02, DK-03, DK-08 with two pages in one context).
- Re-check: edge (forced colors DK-10, storage blocked DK-09), novice (dark on phone).

### R07 Reduce-motion contract, spinner, skeleton primitives, delay helper

- Goal: the infinite-animation bug (the global reduce-motion rule leaves loops running) and the primitives the next slices need.
- Files: `web/libs/shared/styles/src/globals/_base.scss` (reduce-motion block: `animation: none` on the named loops, `animation-iteration-count: 1` and `transition-duration: 0.01ms` as the safety net; the two `!important` stay the only ones and keep their stylelint disable comment), `web/libs/shared/ui/src/lib/components/spinner/*` (rotation 0.8s linear; reduce-motion shows the `clock` icon and the word "Working" from `UI_TEXT`), `web/libs/shared/ui/src/lib/components/skeleton/*` (pulse 1.0 to 0.55, 1.4s; reduce-motion static 0.7; colour `surface-strong`; `aria-hidden` bars plus one sr-only "Loading"), new `web/libs/shared/utils/src/lib/reactive/delayed-loading.ts` (+spec) exporting `delayedLoading(loading: Signal<boolean>, { delay: 300, minShow: 400 }): Signal<boolean>` (true only after 300 ms of waiting, then at least 400 ms), `UI_TEXT.working` and the catalogs. Adoption of the helper on screens is R15.
- Size: 350.
- Depends on: R05 (motion tokens).
- Acceptance: FB-10 (primitives: no pulse, no rotation, "Working" visible, with `prefers-reduced-motion: reduce`); `delayedLoading` unit tests with fake timers: 200 ms answer never true, 350 ms answer true for >= 400 ms; no other `@keyframes` exists besides the three loops (grep).
- Tests: Jest (helper, spinner reduce-motion branch via a `matchMedia` stub); e2e new `17-reduced-motion` (`reducedMotion: 'reduce'`: computed `animation-name` is `none` on `app-skeleton` bars, the shell progress and the spinner).
- Re-check: edge.

### R08 Buttons and form controls

- Goal: V6 (button variants and states, form-field states, control states) plus the button half of UX 7.2.
- Files: `web/libs/shared/ui/src/lib/components/button/*` (variants primary/secondary/quiet/danger and danger-outline; rest, hover, pressed, focus, disabled, busy, success; remove `transition: background-color`; no scale), `form-field/*.scss` (error text with `alert` icon, error border plus inset ring, hint, disabled and read-only look), `input/`, `select/`, `textarea/`, `checkbox/`, `radio-group/` (adds `variant="segmented"`), `search-input/` scss, `web/.stylelintrc.json` (`declaration-property-value-disallowed-list` so `transition`/`transition-property` may not name color, background, border, shadow, filter, width, height, margin, padding or `all`), the catalogs and `UI_TEXT` (`working`, `saved`).
- Behaviour (UX 7.2): a click on a busy button is ignored; it becomes `aria-disabled` and `aria-busy` at once; the spinner and a fixed width appear after 150 ms; the label stays; focus stays; success state input (`[successLabel]` or a `state` input, builder's choice) shows a check and "Saved" for 1.5 s then reverts; hover and pressed colours are instant.
- Size: 750.
- Depends on: R01, R02 (shares `form-field`), R05.
- Acceptance: FR-02 (44px phone, 48px primary), FR-08 (error has icon + text), button busy does not change colour (contrast unchanged), disabled button text 4.5:1, a reason-disabled button stays focusable (`aria-disabled`), no `transition` on colour or background remains in any SCSS (lint rule passes).
- Tests: Spectator on button (all states, double click sends once, busy keeps focus, success reverts after 1.5 s with fake timers), form-field error markup, radio-group segmented keyboard (arrows move, Space selects); contrast pairs for the button states are already in R01.
- Re-check: novice (tap targets, busy), edge (keyboard).

### R09 Tables, lists, chips

- Goal: V7 and the table rules of UX 10.1. Opens the table lane (section 2.3).
- Files: `web/libs/shared/ui/src/lib/components/table/*` (header sentence case 13px `text-muted`, no uppercase, no zebra, sticky header inside the page scroll with `shadow(1)` when stuck, row hover, selected row style, focus ring inset, sort affordance is an icon not only colour, `ui-table-progress` rewritten with `transform`, rename `$compact` (phone card with end-aligned fields) to `$hasPhoneFields` so "compact" means only density), `status-chip/*` (status roles from the visual table, flags outlined, Low chip, count badge), `pager/*` (look only), `stat-card/*` (attention state: edge + icon + text), `tabs/*` scss, list pages only where a class needs the new names (`stock-list`, `document-list`, `low-stock`, `product-stock` templates for chip use).
- Size: 650.
- Depends on: R01, R05.
- Acceptance: TB-03 (visible sort indicator not colour-only), TB-06, phone cards unchanged in structure (NV-08 two lines, 48px target), status chips keep icon + text (FR-08, grayscale screenshot of Home, a list and a document), identifier cells never wrap and money/quantity columns are right-aligned with tabular figures, sentence-case headers.
- Tests: table spec (header markup, `aria-sort`, `$hasPhoneFields` rename does not change phone output), status-chip spec (icon + label for all six statuses and three flags), screenshots at 360 and 1280 reviewed by the fe-designer.
- Re-check: power (lists), novice (cards), edge.

### R10 Shell, tab bar, action bar

- Goal: V9. Keeps every existing class name (`shell-nav`, `shell-tabbar`, `shell-bar`) because the e2e page object finds them by class.
- Files: `web/libs/core/navigation/src/lib/components/shell.component.{html,scss,ts,spec.ts}` (nav active indicator, badges, tab bar five equal cells with the shape cue for the active tab, tab bar hidden while the on-screen keyboard is open using `visualViewport` under 75% of the window, `shell-progress` rewritten with `transform` 1.2 s, reduce-motion static bar), the action bar styles in `web/libs/containers/documents/src/lib/document-actions/*.scss` and `document-editor/*.scss` (styles only; no template or logic change, R14 owns those), `web/libs/shared/ui/src/lib/components/page-header/*.scss` if needed. No `--ims-bottom-offset` (ruling C6).
- Size: 400.
- Depends on: R05, R06 (R06 edited the same account dialog).
- Acceptance: FR-02 at 320 and 360 (five tab cells, each 64px, labels single line with full name in `aria-label`), FR-03 (no clipped controls at 320 and 400% zoom, `media-short` rule still makes bars flow), the action bar never hides the focused control, `aria-current="page"` kept, badge numbers still equal the lists (RA-08 existing checks).
- Tests: shell spec (tab bar entries per role unchanged, keyboard-open hiding with a `visualViewport` stub); e2e `10-phone` and `06-roles` unchanged and green.
- Re-check: novice, edge.

### R11 One failure, one announcement

- Goal: UX S4 (banners and toasts): the double announcement of EA-15/EA-26, dialogs keep the typed reason (PS-01 residual), toast rules.
- Files: `web/libs/core/http/src/lib/interceptors/error.interceptor.ts` (+spec; a call that the screen handles inline, flagged through the existing `HttpContext` mechanism, does not toast), `web/libs/core/notification/src/lib/services/toast.service.ts` (+spec: dedupe the same text within 2 s, max 2 visible, a third success replaces the oldest success, errors never replaced), `components/toast-host.component.*` (phone position directly under the top bar, desktop top right under the header, pause the 4 s timer on hover and focus), `services/confirm.service.ts` and `components/confirm-host.component.*` plus `web/libs/shared/ui/src/lib/components/confirm-dialog/*` (the dialog stays open on failure, keeps the reason, shows the message inside above the buttons with `role="alert"` and moves focus to it), `web/libs/shared/ui/src/lib/components/banner/*` (alert vs status role by "result of an action" vs "present at load"), and the screens that today raise a toast AND an inline message: `document-editor`, `document-actions` (reverse, reject, cancel, post, approve dialogs), `inbox`, `product-edit`, `warehouses`, `partners`, `users`, `settings`.
- Size: 850.
- Depends on: R08 (banner actions use the new button).
- Acceptance: FB-06, FB-07, FB-08, FB-09 (pause, max two, dedupe, never over the action bar at 360), FB-13; a business-rule violation produces exactly one announcement (inline alert, no toast with the same text); EA-15 and EA-26 re-test scenarios pass; a list load failure shows the table error panel only.
- Tests: toast service spec, confirm service spec (failure keeps the dialog open and the reason), error interceptor spec (flagged call does not toast), document-editor spec (reversal blocked: banner only), e2e new `18-feedback` (reverse blocked: dialog stays open with reason, one alert, count `getByRole('alert')` equals 1; two quick saves: one toast).
- Re-check: power (reverse, reject), edge (EA-15, EA-26), novice.

### R12 Density

- Goal: UX S6 + V4 + the density half of V5.
- Files: new `web/libs/core/theme/src/lib/density.service.ts` (+spec; built on `UserPreferenceStore` from R06; values `comfortable` | `compact`; device copy `ims.density`; reset at sign-out), `web/libs/shared/assets/src/assets/theme-init.js` (small edit: also sets `data-density`; the compact token block already exists from R05), `web/libs/shared/ui/src/lib/components/table/*` (row, cell padding, header, pager height, a toolbar slot with the density icon button "Density: Comfortable. Change." shown only at >= 1024px), `pager/*`, filter-bar gap in the list pages' SCSS, the document line grid in `web/libs/containers/documents/src/lib/line-editor/*.scss` (56px to 44px rows), the account dialog in `shell.component.html` ("Density" radio group, segmented, desktop only), the catalogs.
- Size: 750.
- Depends on: R06, R09, R10 (table lane and shell account dialog are serialized).
- Acceptance: DN-01..DN-07 (UX 5.4): at 1280 compact shows at least 30% more Stock rows in the same height (row 48 to 36 is 33%); at 359 to 1023 the control is absent and cards are identical; at 400% zoom the layout is cards and the setting is ignored; the setting survives reload and sign-out/in of the same user and does not leak to another user; switching keeps focus and scroll; compact never clips text that comfortable does not clip; compact keeps 24px minimum targets with an 8px gap; default for every role is comfortable (Q2 accepted).
- Tests: density service spec (per user, blocked storage, reset at sign-out), table spec (toolbar button only when the viewport matches; use a `matchMedia` stub), e2e new `19-density` (count rows in a fixed height, reload, other user, 400% zoom via viewport 320 CSS px).
- Re-check: power (long lists), edge (zoom).

### R13 Overlays: dialog, sheet, toast, banner, card, empty state

- Goal: V8 and UX 7.6 motion for overlays.
- Files: `web/libs/shared/ui/src/lib/components/dialog/*` (native `<dialog>` look: `surface-raised`, `shadow(3)`, scrim `overlay` 50%/60%, no blur; bottom sheet with top radius 16 and handle bar; desktop fade 120 ms in, phone slide 200 ms in / 120 ms out, none under reduce-motion), `toast/*` and `web/libs/core/notification/src/lib/components/toast-host.component.scss` (edge colour, icon, error variant tint), `banner/*` (tint, edge, icon), `card/*`, `empty-state/*` (icon in a 64px sunken disc, variants first-use / filtered-none / role-limited with `lock` / error), `confirm-dialog` scss.
- Size: 600.
- Depends on: R07 (motion fallbacks), R11 (the toast and banner behaviour it styles).
- Acceptance: dialog text only on `surface-raised` (contrast rows already in R01), focus trap, Esc, focus return unchanged (existing specs green), sheet at 320px fits with two buttons wrapping, FB-12 (every animation present is in the UX 7.6 table; grep), DK-06 (banner, toast, scrim legible in dark), forced-colors keeps borders (`border: 1px solid transparent`, never `border: 0`).
- Tests: existing dialog/confirm/toast specs stay green; add empty-state variants spec; screenshots light and dark at 320/1280 reviewed by fe-designer.
- Re-check: edge (DK-10), novice (sheets), power.

### R14 Saved state, row highlight, live regions, error summary

- Goal: the rest of UX S4: button "Saved" in place, NV-04 highlight, announcements, and the "{n} things to fix" banner.
- Files: the Save buttons and their handlers in `document-editor`, `product-edit`, `settings`, `users`, `warehouses`, `partners` (wire the R08 success state for actions that stay on the same screen; Submit/Post/Approve/Reject/Reverse/Cancel never use it; when the Saved state shows, the "Draft saved." toast is not shown, a polite live region says it once), `line-editor/*` and the Add item sheet (changed line gets `accent-soft` for 1.5 s with the UX 7.6 timings; reduce-motion applies and removes the tint with no fade; message "DEMO-001: now 2." stays), new `web/libs/shared/ui/src/lib/components/error-summary/*` (`role="alert"` banner "{n} things to fix", each item links to its field; used by the document editor and the larger forms), "Last saved" line on the draft editor, the catalogs.
- Size: 650.
- Depends on: R08, R11.
- Acceptance: FB-04, FB-05, FB-06, FB-11 (no optimistic server write: delay the API 2 s, status and totals change only after), NV-04, one live announcement per save; the error summary appears only with two or more invalid fields and focus goes to it then to the first invalid field on next Tab.
- Tests: Spectator for each Save flow (Saved shown, toast absent), line-editor highlight timing (fake timers), error-summary links; extend e2e `01-receive` (Save draft shows "Saved", no toast, one live message) and add `18-feedback` cases.
- Re-check: novice (NV-04, NV-21), edge.

### R15 Skeleton shapes per screen, loading rules

- Goal: UX 7.1 end to end: shapes per screen type, 300 ms / 400 ms timing, refetch rule, and no layout shift. Also FR-07 for skeletons.
- Files: `web/libs/shared/ui/src/lib/components/skeleton/*` (shape primitives: line, title, chip, block, row, card row, circle), `table/*` (table skeleton shape: real header, one bar per visible column; `$showSkeleton` uses `delayedLoading`; refetch keeps the old rows at 60% with the thin line), the screens: `document-editor` (header blocks + 3 lines, sticky bar real and disabled), `document-list`, `inbox`, `stock-list`, `low-stock`, `product-stock`, `product-edit`, `settings`, `reports`, `audit-log`; card list skeleton for the phone; pager hidden until total is known.
- Size: 800.
- Depends on: R07, R09, R12 (row heights follow density), R13.
- Acceptance: FB-01 (200 ms answer never shows a skeleton; 350 ms shows one for >= 400 ms), FB-02 (CLS 0 when content replaces it, measured in e2e with a `PerformanceObserver` for `layout-shift`), FB-03 (a skeleton for Staff has no cost or value column; for Clerk no valuation column), a refetch never replaces rows with a skeleton, pager hidden while loading; `aria-busy` on the region, one "Loading" status.
- Tests: table spec (columns hidden by `columnWhen` have no skeleton bar), per-screen spec for one skeleton shape, e2e new `17-reduced-motion` cases plus a CLS check in `18-feedback` (throttle the API through `page.route`).
- Re-check: novice (phone cards), power (tables), edge.

### R16 Command palette component and `app-kbd`

- Goal: V10 part (a): the presentational component with fake data and its look; no identity, no router, no API.
- Files: new `web/libs/shared/ui/src/lib/components/command-palette/*` (`app-command-palette`: inputs for groups, options, active index, query, states; outputs for query change, run, run-secondary, close; ARIA 1.2 combobox + listbox with `aria-activedescendant`, DOM focus stays in the field; input row 56px, group labels, result rows with `--ims-row-height`, active row `accent-soft` with an edge and a return hint, matched characters weight 600, footer hints, empty state, group error row with Retry, offline line; full-height sheet below 600px) and `kbd/*` (`app-kbd`), uses `app-dialog`; `web/libs/shared/ui/src/index.ts` exports; the catalogs for the UI words that live in the kit through `UI_TEXT`.
- Size: 500.
- Depends on: R05 (kbd, tokens), R13 (dialog look).
- Acceptance: CP-07 at component level (arrows move the active option without moving DOM focus, `aria-activedescendant` updates, Enter emits run, Esc closes and focus returns to the opener), accessible names "{primary}, {secondary}, {group}" (CP-12 names), option rows >= 44px on touch and 48 in the phone sheet (CP-14), no horizontal scroll at 320.
- Tests: Spectator on the component with fake groups (keyboard map from UX 6.4 including Home/End only at the caret ends, roles, names and live region), `app-kbd` spec. No demo route is added to the app; the axe scan of the palette happens in R18's e2e.
- Re-check: none yet (not reachable in the app).

### R17 Role-aware Home: Doers (Staff, Clerk)

- Goal: UX S5 (RA-01..RA-12 for Doers). Replaces the stat-tile Home for the Doer profile; the Reviewer profile keeps today's tiles until R19.
- Files: `web/libs/containers/home/src/lib/home/*` (profile chosen from permissions through a pure `homeProfileOf(has)`: Doer = `document.create` and not `document.approve`, Reviewer, Overseer, with unit tests including a custom permission set), new `web/libs/containers/home/src/lib/widgets/*` (widget frame with skeleton / error row with Retry / empty line; W1 greeting and scope line, W2 quick actions, W3 Returned to me, W4 My drafts, W5 Waiting for approval - mine, W7 Low stock, W11 Recently posted by me collapsed on phone), `web/libs/features/documents/src/lib/services/home-feed.store.ts` (one `mine=true` list call, page size 25, split client-side into returned drafts, drafts, submitted, posted), `web/libs/features/stock` (low-stock first 3 or 5 rows through the existing store), `stat-card` untouched, the catalogs, `e2e/src/pages/screens.ts` (Home page object).
- Data and call budget (RA-09): Doer Home makes 3 calls: `GET /api/documents/counts`, one `GET /api/documents?mine=true&pageSize=25`, `GET /api/stock/low-stock?pageSize=5` (its `total` is the count, so `/api/stock/summary` is not needed for a Doer). Headings use the counts as truth; rows are the newest 25 of the user's documents. A returned draft older than the newest 25 shows in the count but not in the rows; "See all" opens the Receipts or Issues list by the first row's type (accepted, UX Q5/Q6); the Returned chip alone is shown, no reason text (UX Q6). Backend improvements are optional (section 3).
- Cost rule (D5): Staff - no amount, no `0.00`, no word "value" in any widget, `aria-label` or `title`; Clerk - amounts only in W4 and W5; nobody on this profile sees a valuation.
- Size: 900.
- Depends on: R04, R15 (skeleton primitives and the delay helper).
- Acceptance: RA-01 (Doer order: W1, W2, W3, W5, W4, W7, W11; DOM order equals visual order, no CSS `order`), RA-02, RA-03, RA-05 (360x640: Receive and Issue visible without scrolling and >= 56px high; first Low stock row one scroll away), RA-06 (fail one request, the others stay), RA-07 (rows open the document; Tab reaches each row, Enter opens), RA-09 (3 calls), RA-10, RA-11 (no warehouse empty state kept), RA-12; W3/W4/W5 hidden at zero; W7 rows offer Receive for `document.create`.
- Tests: home spec per profile and per role (Staff, Clerk), no-money scan of the rendered DOM for Staff (regex on money-shaped text and on `aria-label`/`title`), failure isolation spec; e2e new `20-home-doer` and update `06-roles` and `13-cost-visibility` (Home steps use the new page object).
- Re-check: novice (primary), edge (FR-07, keyboard), power (Clerk view if time).

### R18 Command palette: logic, trigger, recents, actions

- Goal: UX S8 (CP-01..CP-03, CP-06, CP-07, CP-10, CP-11, CP-13, CP-14) without the document/product search.
- Files: new `web/libs/core/navigation/src/lib/palette/*` (`PaletteService`: open state, Ctrl+K and Cmd+K from anywhere with `preventDefault`, restores focus to the opener; `PaletteCommands`: Go to built from `MENU_ITEMS` filtered by permission, Actions: New receipt and New issue (`document.create`), Appearance (3), Density (2, only >= 1024px), Language (3), Show keyboard shortcuts (added in R22, omitted until then), Sign out; ranking and matching (label prefix, word prefix, substring; case and diacritics ignored; English names searchable in zh/ms; query capped at 100 characters); `RecentsStore` on `UserPreferenceStore` (last 8 documents and products, cleared at sign-out); the palette never offers Post, Approve, Reject, Cancel, Reverse, Submit, delete, import or export), `shell.component.{html,ts}` (trigger button: a real `<button>`, `aria-haspopup="dialog"`, `aria-keyshortcuts="Control+K Meta+K"`, name "Search or run a command, Control K"; 240x40 with the key hint at >= 1024px, 44px icon button from 600 to 1023px; the palette is mounted with `@defer` so its code is a lazy chunk), `web/libs/containers/documents` and `stock` product pages call `RecentsStore.add` when a document or product page opens, the catalogs, `e2e/src/pages/shell.ts`.
- IMPORTANT e2e collision: `Shell.account` is `page.getByRole('banner').getByRole('button')` ("the only button in the top bar"). The trigger adds a second button there, so this PR MUST change the locator in the same PR (use `getByRole('button', { name: <account label> })`, and keep language-independence the way `09-language` needs) and run all 13 existing scenarios.
- Phone (< 600px): the icon button is R20, so R18 shows no trigger on phones (UX 6.3, Q3 accepted but built after the desktop palette).
- Size: 750.
- Depends on: R06, R10, R12 (density actions), R16.
- Acceptance: CP-01 (Ctrl+K opens and closes from Home, a list, the editor and inside a text field; the browser omnibox does not open), CP-02 (trigger visible >= 1024px with hint; opens with Enter and Space), CP-03 (Staff: no Approvals, Reports, Users, Settings, Audit; typing "settings" returns no screen; Admin gets Settings), CP-06 (no amount for any role), CP-07, CP-10 (sign-out clears Recents; the next user sees none), CP-11, CP-13 (opening in a dirty editor does not show "Discard changes?"; navigating away through a result does), CP-14 at component level; bundle: the palette code is in a lazy chunk <= 40 kB raw and the initial total grows by <= 6 kB.
- Tests: palette service/commands specs per role (Staff, Clerk, Supervisor, Owner, Admin command lists as in UX 6.9), recents spec, shell spec (trigger, Ctrl+K), e2e new `22-palette` (keyboard path, role lists, recents cleared) and run the whole suite for the locator change.
- Re-check: power (primary), edge (keyboard, screen reader CP-12), novice (no change expected on phone).

### R19 Role-aware Home: Reviewer, Overseer, Admin

- Goal: UX S7. Completes RA-01 for all five roles.
- Files: `web/libs/containers/home/src/lib/home/*` and `widgets/*` (W6 Needs my approval - oldest first, filtered by `canAct`, count and rows, "All clear: nothing waiting for you." at zero, amount only with `cost.view`; W8 Posted today; W9 Stock value (`valuation.view`, exactly 2 decimals) and Products with stock; W10 Recent movements (signed Stock effect, Reversal badge, no money); W12 Rules in force (`settings.manage`, from `SystemSettingsStore`); W13 Go to; two-column grid from 1024px, content max 76rem), `web/libs/features/documents/.../home-feed.store.ts` (approvals preview `status=Submitted&requiresApproval=true&pageSize=50`, sorted oldest first in the client), `web/libs/features/reports` (movements first 5), `stat-card`, the catalogs, `e2e/src/pages/screens.ts`.
- Call budget (RA-09): Reviewer 6 calls (counts, summary, approvals, low-stock, mine, movements); Admin 7 with settings (cached); Owner fewer. The oldest-first order is client-side on up to 50 rows; if more than 50 are waiting the true oldest could be off the page (backend note B2).
- Size: 900.
- Depends on: R17.
- Acceptance: RA-01 for Supervisor, Owner and Admin in the exact order of UX 2.4, RA-04 (Owner: no Receive/Issue, no Posted today), RA-08 (W6 and W7 headings equal the sidebar badge and the list total), RA-09 (<= 7 calls), RA-10, FR-05 (Stock value reads `119,371.90`, never `119k`), D5 (Clerk Home has no valuation; Staff none), W6/W7 stay and say "All clear" at zero while W3/W4 hide at zero.
- Tests: home spec for Supervisor, Owner, Admin and a custom permission set (RA-02), call-count spec, e2e new `21-home-reviewer`, update `13-cost-visibility` (Supervisor tile) and `03-approval-override` if it asserted the old Home tile.
- Re-check: power (primary), edge.

### R20 Palette search and phone button

- Goal: UX S9. Documents and Products groups, states, and the phone search button (Q3 accepted).
- Files: `web/libs/core/navigation/src/lib/palette/*` (Documents group `GET /api/documents?search={q}&pageSize=5`, visible with `stock.read` and query >= 2 characters; Products group `GET /api/products?search={q}&active=true&pageSize=5` with `masterdata.read`; 300 ms debounce through `debouncedSignal`; ranking rules for document-number-looking queries and exact SKU/barcode which pre-selects the product and opens its stock page; Ctrl+Enter opens the product record for `masterdata.manage`; "Refine your search to see more."; per-group loading skeleton after 300 ms, per-group failure row with Retry and no toast, 403 omits the group, offline shows only local groups plus the offline line, 429 shows the server sentence; live region "{count} results" debounced 500 ms), the top bar icon button below 600px and the full-screen sheet (48px field with autofocus via `data-autofocus`, close button, tab bar hidden while open, Android back closes the sheet first), the catalogs.
- Size: 700.
- Depends on: R18.
- Acceptance: CP-04 (typing `RCV-2610-0007` lists that document first, Enter opens it, request debounced 300 ms and capped at 100 chars), CP-05, CP-08 (Documents call fails: one retry row, other groups and no toast), CP-09 (offline), CP-12, CP-14 on a 320px phone and at 400% zoom, FR-07 (palette rows carry no money for any role, results for Staff come only from their own warehouses because the server scopes them).
- Tests: palette search spec with a mocked ApiClient (ranking, debounce, failure, 403, offline), phone sheet spec, e2e extend `22-palette` and `10-phone` (open the sheet, find a document by number in one tap sequence).
- Re-check: power, novice (phone find-by-number), edge (CP-12 screen reader).

### R21 Paging: First/Last and page size

- Goal: UX 10.3 (PS-13, accepted).
- Files: `web/libs/shared/ui/src/lib/components/pager/*` (desktop: First, Previous, "Page 3 of 14", Next, Last and a page size select 25/50/100; phone keeps Previous/Next and the range; after a page change scroll to the top of the list and move focus to the table caption), every page that hosts a pager: `document-list`, `inbox` (no pager today, unchanged), `stock-list`, `low-stock`, `product-stock`, `products`, `catalog-lists`, `partners`, `users`, `audit-log`, `reports`; page size is a per-user preference (`UserPreferenceStore`), clamped to the API maximum (200 allowed, UI offers 25/50/100); `createPageState` in `libs/shared/utils` resets to page 1 when the size changes.
- Size: 500.
- Depends on: R12 (table/pager density), R15.
- Acceptance: TB-01..TB-06 unaffected; at 1280 First/Last/size select exist, at 360 they do not; changing size keeps filters and returns to page 1; last page then Last works with 14 pages (no off-by-one); focus lands on the caption after a page change; the range text stays `26-50 of 333`.
- Tests: pager spec, page-state spec, e2e extend with `24-paging-dates` (Last, size 50).
- Re-check: power (PS-13 closed), edge (focus).

### R22 Keyboard extras

- Goal: UX S10 (CP-15, CP-16, CP-17), Could priority.
- Files: new `web/libs/core/navigation/src/lib/shortcuts/*` (`ShortcutsService`: `/` focuses the screen search box, `?` opens the dialog, only when focus is not in an input, textarea, select or contenteditable and the "Single-key shortcuts" switch is On; Ctrl+S saves where a Save button exists, Ctrl+Enter triggers the primary action but never skips the Post confirm dialog), the "Keyboard shortcuts" dialog with the On/Off switch (default On at >= 1024px, per user), palette action "Show keyboard shortcuts", the document editor handlers in `web/libs/containers/documents/src/lib/document-editor/*`, `app-table` row arrows (Up/Down move between row links; Enter opens) in `table/*`, tooltips with key hints, the catalogs.
- Risk handled here: barcode scanners type like a keyboard and a barcode may contain `/`; the single-key shortcuts therefore never fire while an input is focused, are off below 1024px, and the switch exists. The scanner path (`07-scan`) must stay green.
- Size: 800.
- Depends on: R18, R21 (table lane).
- Acceptance: CP-15, CP-16, CP-17, `/` does nothing inside a field, `?` opens the dialog, the switch turns both off and is remembered per user, shortcuts are never the only way to do anything.
- Tests: shortcuts service spec (field guard, switch, Ctrl+S with and without a Save button), table arrow navigation spec, e2e new `23-shortcuts` and rerun `07-scan`.
- Re-check: edge (primary), power.

### R23 Date field `dd MMM yyyy`

- Goal: UX 10.4 (NV-27, PS-15, accepted): a text date field in the app format with a calendar button.
- Files: new `web/libs/shared/ui/src/lib/components/date-field/*` (`app-date-field`: typed text `dd MMM yyyy`, also accepts a pick from the native picker through a button, emits an ISO date string `yyyy-MM-dd` (the Kuala Lumpur calendar day, same as today's `from`/`to`), validation message "Enter a date such as 09 Oct 2026."), `web/libs/shared/utils/src/lib/dates/*` (parse/format helper with a month table for en, zh, ms), adoption in the filter bars of `reports`, `document-list`, `audit-log`, and any other `type="date"` input (grep `type="date"`), the Reports empty state action "Change filters" (S11), the catalogs.
- Size: 600.
- Depends on: R03 (range validator and message), R08 (form-field).
- Acceptance: the Reports, Receipts, Issues and Audit date filters accept `09 Oct 2026` and reject `31 Feb 2026`; From after To shows "From must be on or before To." (R03 behaviour kept); the field shows the same format in en, zh and ms; keyboard and screen reader: label, hint of the format, error linked with `aria-describedby`; no mm/dd/yyyy anywhere.
- Tests: parse/format spec (leap day, month names per locale, trailing spaces), date-field spec, reports and document-list filter specs, e2e `24-paging-dates` and `15-reports-range` updated for the new field.
- Re-check: novice (NV-27), power (PS-15), edge.

### R24 Clean-up

- Goal: V11. Remove the compatibility layer once nothing uses it.
- Files: every `*.scss` that still uses an old name (about 54 `color()` call sites and 21 uses of `border-width('focus')`, `easing('smooth')`, `font-weight('bold')`; counted today), `web/libs/shared/styles/src/variables/_palette.var.scss` (delete `$aliases`), `_color.var.scss`, `_shape.var.scss`, `_font.var.scss`, `_layout.var.scss` (delete the old aliases), `web/.stylelintrc.json` (disallow the old names), `web/docs/conventions/scss-design.md`, `docs/architecture/frontend.md` (theme row, `core-preferences`, palette and shortcuts, call budgets), `web/README.md` (preferences, theme-init), any leftover per-user preference helper (`list-scope.preference.ts` if not migrated).
- Size: 300.
- Depends on: all other slices merged and no other branch touching `.scss`.
- Acceptance: removing the alias map makes `color('primary')` throw at compile time, so a clean build proves nothing was missed; contrast and stylelint green; visual diff zero (the rename is mechanical); docs describe the final token names.
- Tests: compile is the test; full e2e suite; one last screenshot pass light/dark at 320/1280.
- Re-check: edge (final accessibility pass), power, novice (smoke).

## 2. Merge order, parallel work, and file collisions

### 2.1 Waves

Slices in the same wave touch disjoint files and may be built by separate agents at the same time. The orchestrator should keep at most three builds in flight (review load); the column "max" is what is possible, not what is advised.

| Wave | Slices (parallel) | Max | Gate before the next wave |
|---|---|---|---|
| 1 | R01, R02, R03 | 3 | all merged, contrast green |
| 2 | R04 (after R02 merges), R05, R06 | 3 | R05 and R06 merged |
| 3 | R07, R08, R09, R10 | 4 | all merged; QA smoke (novice phone + power desktop) on the recoloured app |
| 4 | R11, R12 | 2 | merged |
| 5 | R13, R14 | 2 | merged; fe-designer screenshot review light/dark |
| 6 | R15, R16 | 2 | merged |
| 7 | R17, R18 | 2 | merged; QA re-check of Home (Doer) and palette |
| 8 | R19, R20, R21 | 3 | merged |
| 9 | R22, R23 | 2 | merged |
| 10 | R24 | 1 | nothing else open on `.scss` |

Recommended minimum path if the owner wants value early: R02, R03, R04 (defect and rule fixes, no visual risk), then R01, R05, R06 (palette and dark mode), then R17 (Doer Home).

### 2.2 Dependency spine

R01 -> R05 -> {R07, R08, R09, R10} -> {R11, R12} -> {R13, R14} -> R15 -> {R17, R19}; R06 -> R10 -> R12 -> R18 -> {R20, R22}; R16 -> R18; R03 -> R23. Only R01, R02, R03 can start on day one.

### 2.3 Collision map (one owner per lane at a time)

| Hotspot | Slices that touch it, in the order they must merge | Rule |
|---|---|---|
| Shared form field (`libs/shared/ui/.../form-field`, `input`, `select`, `radio-group`) | R02, R08, R23 | Serial. R02 changes markup and the token, R08 only styles/states, R23 adds a sibling component and uses the field. |
| Table lane (`.../table/*`, `.../pager/*`, list page templates) | R09, R12, R15, R21, R22 (R23 edits the same list pages' filter bars, so it merges after R21) | Serial, one agent at a time, in that order. R03 deliberately avoids the table (cell templates only). R07 deliberately does not touch it (the delay helper is adopted by R15). |
| Shell (`libs/core/navigation/.../shell.component.*`) | R06 (account dialog), R10 (visual), R12 (density radio), R18 (trigger), R20 (phone button) | Serial in that order. Keep every existing class name (`shell-nav`, `shell-tabbar`, `shell-bar`). |
| Styles library (`libs/shared/styles/**`) | R01 (palette, color function), R05 (all non-colour tokens and the density blocks), R07 (`_base.scss` reduce-motion), R24 | R05 owns the other files so no later slice edits `_layout.var.scss`, `_shape.var.scss`, `_font*.var.scss`, `_space.var.scss`. R01 and R05 may run in different waves but never edit `_themes.scss` in the same wave; R01 does not edit it at all. |
| Overlay components (dialog, confirm-dialog, toast, banner, card, empty-state, `core-notification`) | R11 (behaviour), R13 (look) | R11 first. R13 must not change the toast service or confirm service API. |
| Document editor (`containers/documents/.../document-editor`, `document-actions`, `line-editor`) | R02 (legend), R04 (unit cost input), R10 (styles only), R11 (banner/toast), R14 (Saved, highlight, summary), R15 (skeleton), R22 (shortcuts) | Serial in that order within a wave boundary; R10 touches SCSS only. |
| i18n catalogs (`libs/core/i18n/src/lib/messages/{en,zh,ms}.messages.ts`) | every slice | Add keys inside the existing area groups (`form.`, `theme.`, `home.`, `palette.`, ...), never appended at the end of the file, so two parallel slices do not edit the same line. Every key goes into all three files in the same PR (the compile-time check fails otherwise); zh and ms drafts carry `TODO-review`. When two parallel PRs still conflict, the second to merge rebases and keeps both blocks. |
| `UI_TEXT` token and provider (`shared/ui/.../ui-text.token.ts`, `core/i18n/.../ui-text.provider.ts`) | R02, R07, R08, R16 | Append alphabetically; same rebase rule as the catalogs. |
| `icon.paths.ts` | R05 only | All five new icons land in R05; later slices only use the names. |
| e2e page objects (`e2e/src/pages/shell.ts`, `screens.ts`, `documents.ts`) | R02 (login), R18 (`Shell.account` locator), R17 and R19 (Home), R22 | Page objects are edited only by the slice that breaks them, in that PR. Scenario file numbers are reserved up front (below) so parallel agents never create the same file. |
| `web/apps/main/project.json`, `ngsw-config.json`, `index.html`, manifest | R01 (budget), R06 (all four) | R06 owns the last three. |
| `web/scripts/check-contrast.mjs` | R01 (rewrite), R06 (index.html/manifest assertions) | Serial. A new colour token enters only with its pairs in the same PR. |
| Lockfile | none expected | No new runtime dependency is planned; a PR that adds one needs fe-architect approval. |

Reserved e2e scenario files (new): `14-required-marker` (R02), `15-reports-range` (R03), `16-appearance` (R06), `17-reduced-motion` (R07, R15), `18-feedback` (R11, R14, R15), `19-density` (R12), `20-home-doer` (R17), `21-home-reviewer` (R19), `22-palette` (R18, R20), `23-shortcuts` (R22), `24-paging-dates` (R21, R23). Existing scenarios updated: `06-roles`, `08-session`, `10-phone`, `11-accessibility`, `13-cost-visibility`, `01-receive`, `03-approval-override`. The suite is manual or weekly (not on pull requests), so the slice author runs the affected scenarios locally before merge and `behavior-test-engineer` owns the shared page objects after each wave.

### 2.4 Who builds what

fe-framework builds every slice (lane `web/`). behavior-test-engineer writes the e2e files and page-object edits (lane `e2e/`) one wave behind, except the two forced page-object changes called out (R02 login, R18 `Shell.account`) which the slice author makes in the same PR. fe-designer reviews screenshots at the wave gates of waves 3, 5 and 10. fe-architect reviews each PR against `docs/architecture/frontend.md` and this plan.

## 3. Work that is not frontend (backend)

Nothing here blocks any slice. The refresh works against today's API; each item only improves exactness or safety. `docs/design/2026-refresh-ux.md` 2.6 listed the same gaps.

| ID | Need | Used by | Without it | Priority | Owner |
|---|---|---|---|---|---|
| B1 | `returned=true` filter on `GET /api/documents` (a Draft with `RejectionReason != null`, the same rule as `isReturned`) | R17 W3 | The client reads the newest 25 of the user's documents; a returned draft older than that is counted in the heading but not listed | Recommended | be-framework |
| B2 | An ascending order option on `GET /api/documents` (for example `sortBy=submittedAt&desc=false`; today it is `OrderByDescending(d => d.Id)` only) | R19 W6 "oldest first" | The client sorts the newest 50 waiting documents; with more than 50 waiting, the true oldest could be missed | Recommended | be-framework |
| B3 | `updatedAt` (and `returnedAt`) on `DocumentListItem` | R17 W3/W4 "age" and "saved time" | Rows show `createdAt`; slightly wrong for an old draft that was edited today | Optional | be-framework |
| B4 | Server-side 2-decimal rule for `unitCost` (receipt lines) and `amountThreshold` (approval rules): reject, with the sentence the UI uses, or round half-up and say so in the OpenAPI description | R04 | The UI blocks sub-cent input, but a script or old client can still post 12.345 | Recommended | be-architect decides, be-framework builds |
| B5 | Confirm that `GET /api/products?search=` matches SKU, name and barcode (the palette promises all three) and that `GET /api/documents?search=` matches number and partner (it does; the remark is PR-05) | R20 | The palette finds fewer items than its placeholder says | Verify only | be-framework |
| B6 | Already open, not part of this refresh but visible on the new Home: PR-01 (reversing a receipt leaves the average cost and so W9 Stock value wrong), PR-05, PR-08 | R19 W9 | The Stock value tile shows the wrong number after a receipt reversal | Existing finding | be-framework |

No new endpoint, no preferences endpoint (theme and density are per browser, Q1), no migration, no OpenAPI change is required by R01..R24. If B1 to B4 are built, the web client is regenerated with `npm run api:generate` and the change is a small follow-up (not counted in the 1,200-line cap).

Other non-frontend items: `security-engineer` confirms the CSP note for `theme-init.js` (R06) and that local recents contain no sensitive data and are cleared at sign-out (R18); `integration-test-engineer` has nothing to do.

## 4. Definition of done

### 4.1 Per slice (all must be true to merge)

Run from `web/` (Node 22.6 on the dev machine needs `NODE_OPTIONS=--experimental-require-module`, and Prettier `--end-of-line auto` on the Windows checkout; CI uses Node 22.22.3+).

| # | Check | Command / evidence |
|---|---|---|
| 1 | Lint (ESLint, module boundaries, Stylelint) | `npm run lint` |
| 2 | Format | `npm run format:check` |
| 3 | Unit tests | `npm test`; the slice's tests listed above exist and pass; the i18n completeness test passes (en, zh, ms) |
| 4 | Contrast | `npm run contrast` (any new colour pair is in the script in the same PR) |
| 5 | Production build within budget | `npm run build:prod`; no budget warning or error (4.3) |
| 6 | Dependency audit | `npm audit --omit=dev --audit-level=high` (as in `.github/workflows/ci.yml`) |
| 7 | E2E green | the affected scenarios plus `06-roles`, `10-phone`, `11-accessibility`, `13-cost-visibility` run against a throw-away API and the production build (steps in `e2e/README.md`); axe 0 serious/critical on every touched screen in light AND dark |
| 8 | Manual checks | 320x568 and 1280 at 400% zoom: no horizontal page scroll, no clipped or overlapped control (FR-03); a Staff and a Clerk walk-through of every new surface (FR-07/D5); grayscale screenshot for status meaning (FR-08) when chips or banners changed |
| 9 | Acceptance IDs | every ID named in the slice passes |
| 10 | Docs and hand-off | `docs/team/HANDOFFS.md` entry (PROTOCOL format) naming the next role; if a contract changed, `docs/architecture/frontend.md` updated (or listed for R24) |
| 11 | PR size | under about 1,200 changed frontend lines (excluding lockfile, `web/openapi/**`, generated `api-client.ts`); more than 1,000 during the build means stop and split |
| 12 | No scope creep | no change outside the slice's file list without a note in the PR; the collision rules of 2.3 followed |

### 4.2 Wave gates

After waves 3, 5 and 10: fe-designer reviews light/dark screenshots at 320 and 1280; qa-novice-warehouse-staff and qa-power-supervisor run a smoke of their main flows. After waves 7 and 8: the QA re-checks named per slice; findings go to `docs/qa` and are triaged before the next wave.

### 4.3 Bundle size budget

Measured today on main with `npm run build:prod` (Angular build output, raw size / estimated transfer):

| Initial chunk | Raw | Transfer |
|---|---|---|
| `chunk-DhIl4S_k.js` (framework and shared vendor code) | 496.61 kB | 112.50 kB |
| `main-3WXR6LQT.js` | 36.12 kB | 9.86 kB |
| `styles-OOVADWYC.css` | 5.79 kB | 1.12 kB |
| **Initial total** | **538.51 kB** | **123.48 kB** |

The current budget in `web/apps/main/project.json` is warning 700 kB / error 1.2 MB, which would hide a 60% regression. New budget (set in R01):

| Budget | Warning | Error | Note |
|---|---|---|---|
| `initial` | 600 kB | 650 kB | target after R24: at most 575 kB raw and 135 kB transfer (about +7%) |
| `anyComponentStyle` | 8 kB | 16 kB | unchanged |
| per slice | n/a | n/a | a slice adds at most 6 kB raw to the initial total; the palette (R16, R18, R20) and the Home widgets (R17, R19) are lazy (`@defer` and the lazy home route), so they do not count against it |
| lazy chunks | n/a | n/a | the palette chunk at most 40 kB raw; the Home chunk grows at most 25 kB; no other new lazy chunk above 40 kB |
| global styles | n/a | n/a | at most 9 kB raw (5.79 kB today) |

Each PR states the initial total before and after in its description. `theme-init.js` is a separate 400-byte file and is not in the initial script budget.

## 5. Risks and rollback

### 5.1 Risks

| # | Risk | Slice | Mitigation |
|---|---|---|---|
| K1 | The palette change recolours the entire app at once; a contrast or meaning regression hides in a screen no test opens | R01 | The alias drop-in touches no component; the 162-check gate; axe in both schemes on the 11-accessibility screens; fe-designer screenshots at the wave 3 gate |
| K2 | Radius, line height and icon stroke shift layouts | R05 | Comfortable token values equal today's; 320 and 400% zoom checks; `10-phone` stays green |
| K3 | Removing duplicate toasts (R11) removes the only announcement of a failure | R11 | Each failure keeps exactly one announcement (inline alert or panel); EA-15/EA-26 re-test; an e2e that counts `role=alert` equals 1 |
| K4 | `theme-init.js` is blocked by CSP or stale in the service worker, or the flash returns | R06 | Same-origin classic script (allowed by `script-src 'self'`); listed in `ngsw-config.json`; DK-03 test; the script ignores unknown or malformed storage |
| K5 | The palette trigger breaks `Shell.account` in all e2e scenarios | R18 | The locator change ships in the same PR; run all scenarios |
| K6 | Cost or value leaks through a new surface (a widget, palette row, skeleton column, `aria-label`, `title`) | R15, R17, R18, R19, R20 | Permission-driven rendering only (`*appCan`, `columnWhen`); DOM money scan in the Staff specs; `13-cost-visibility` extended per slice; qa-edge repeats the cost sweep |
| K7 | Home call budget (RA-09) or latency regress, or a returned draft is not listed (B1) | R17, R19 | Call-count spec; headings use `counts`; B1 and B2 offered to the backend; widgets fail independently |
| K8 | Money display rounds a stored value that has more decimals (average cost has 6) so qty x shown unit cost differs from the shown amount by a cent | R04 | Amounts shown are the server's totals; unit cost fields are inputs limited to 2 decimals; a derived average cost is never multiplied on screen; note to the owner |
| K9 | Single-key shortcuts fire from a barcode scanner (`/` is a valid barcode character) | R22 | Never while an editable control is focused; desktop only; off switch; `07-scan` rerun |
| K10 | Parallel PRs conflict in catalogs, `UI_TEXT`, `_layout.var.scss` | all | Section 2.3 rules; R05 pre-lands tokens; add keys inside area groups |
| K11 | Compact density at 1024px with a touch tablet | R12 | `pointer: fine` guard plus 1024px; control hidden otherwise; DN-02, DN-06 |
| K12 | Skeleton delay makes the app feel slower or flicker | R07, R15 | 300 ms delay only avoids flashes, 400 ms minimum only applies once shown; FB-01, FB-02 measured |
| K13 | Bundle growth | all | Budgets in 4.3, lazy palette, stated before and after in each PR |
| K14 | Edge-accessibility retest is not in yet; its findings may reshape R22/R24 | R22, R24 | Triage when it lands; add a slice rather than growing one |
| K15 | Machine-quality zh and ms texts for new copy | all | `TODO-review` marker and the native-speaker review already planned |

### 5.2 Rollback

- Every slice is one pull request that is green alone and independently revertable (`git revert` of the merge commit); no feature flags, because each slice is either invisible until later slices use it (R05, R07, R16) or a small, testable behaviour change.
- Palette: reverting R01 restores the old palette in one step as long as R24 has not merged. R24 is the only point of no return for token names; merge it only after the QA gates of waves 7 and 8 and one release of soak, and keep it a pure rename so it can itself be reverted.
- Storage: R06's per-user keys are new names (`ims.pref.<userId>.*`); the device keys `ims.theme` and `ims.density` keep the old meaning, so an older build still reads `ims.theme` correctly. A revert leaves unused keys in the browser, which is harmless.
- Service worker: a revert of R06 changes `index.html` and `theme-init.js`; the service worker update prompt delivers it on the next visit; the script is defensive so a stale copy cannot break first paint.
- R04 and R23 change what users type; the API still accepts what it accepted before, so a revert strands no data.
- If a slice fails its QA gate after merge, revert it, record the finding in `docs/qa`, and re-plan only that slice; later slices that depended on it wait (the dependency table in section 1 shows which).
