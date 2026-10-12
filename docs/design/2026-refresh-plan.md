# IMS 2026 design refresh - build plan, version 2

> **Superseded for scope and schedule (2026-10-12).** The product owner approved the lean plan in [`lean-scope.md`](lean-scope.md) ("no over-engineering"): 32 frontend slices (about 17,600 lines) in 16 waves instead of the 98 slices and 34 waves below. **This file is now the full reference, not the build list.** Build only what `lean-scope.md` section 3.1 lists; a slice below that is not on that list is deferred (trigger in `lean-scope.md` section 4) or dropped (section 5) and must not be built without the project-manager recording its trigger.
>
> What to read where: slice specs (sections 1.3 to 1.9) stay valid for the slices that are built, with the lean size, merges and trims of `lean-scope.md` section 2 (it wins on any difference). Replaced by `lean-scope.md`: the master list and sizes (1.2), the waves (2.1, now `lean-scope.md` 3.2), the fast path and cut list (2.6, 2.7), size and elapsed time (2.8, now `docs/pm/ROADMAP.md` section 5), the backend items and readiness (3.1, 3.3, now `lean-scope.md` 2.8 and 3.4), the definition of done (4.1, now `lean-scope.md` 3.5), the wave gates (4.2, now 4 QA rounds and 3 gates) and the owner questions (6, now `lean-scope.md` 8, answered). The roadmap is [`docs/pm/ROADMAP.md`](../pm/ROADMAP.md) version 2; its references in the text below to "ROADMAP section N" point to version 1 numbering and are historical.

Author: fe-architect. Date: 2026-10-11. Status: version 2, third revision (motion language, access programme, document corrections, public-id flip and the delivery roadmap folded in). Ready for fe-framework (build), behavior-test-engineer (e2e), qa-* (re-checks), be-framework and be-architect (section 3), security-engineer (A10, Q05, Q06, RP, G01), project-manager (calendar: `docs/pm/ROADMAP.md`).
Merges `docs/design/2026-refresh-ux.md` (S1..S11, FR/RA/RQ/DK/DN/CP/FB/TB acceptance IDs) and `docs/design/2026-refresh-visual.md` (steps 1..11, tokens, contrast) with the product owner's decision of 2026-10-11: **no component library and no bake-off; we keep our own `app-*` components, and the UI must be more advanced, more stable and more responsive than version 1 delivered.**
Inputs also read: `docs/design/mvp-ux.md`, `docs/architecture/frontend.md`, `web/README.md`, `web/docs/conventions/*`, `e2e/README.md`, `docs/qa/retest-power-supervisor.md` (PR-01..PR-14), `docs/qa/retest-edge-accessibility.md` (ER-01..ER-21, triaged in 0.3) and `docs/qa/retest-novice-warehouse-staff.md` (NR-01..NR-18, triaged in 0.4), `docs/qa/fix-status-frontend.md`, `docs/design/2026-refresh-login.md` and `docs/design/2026-refresh-login-visual.md` (sign-in page), `docs/architecture/adr-guid-keys.md` (Accepted), `docs/design/2026-refresh-motion.md` (section 7), `docs/design/roles-permissions-ux.md`, `docs/architecture/adr-roles-permissions.md`, `docs/design/document-corrections.md`, `docs/pm/ROADMAP.md`, `docs/team/PROTOCOL.md`, the real code under `web/libs/` (24 shared components, `app-table`, `app-product-picker`, `app-document-line-editor`, the shell, `core-http`, `core-pwa`), `web/apps/main/{project.json,ngsw-config.json}`, `.github/workflows/{ci,e2e}.yml`, `e2e/playwright.config.ts`, and a `dist` build (initial 538.51 kB raw, largest lazy chunk 72 kB).

## Changelog: version 1 to version 2

Version 1 was 24 slices (R01..R24, about 14,760 lines) that restyled the app. Version 2 keeps them (with the changes below), adds the sign-in redesign (R25), the session-ended slice (R26), four slices for the QA round 3 findings (R27 to R30) and three programmes, content containment (L00, an owner-reported defect), the motion language (8 slices M01..M08 and growth of 20 slices), the access programme (RP00..RP11), the document corrections (R28, R31, R32), the money-PR follow-up (R33), the public-id web flip (G01, G02) and three programmes, so the whole plan is 98 slices and about 68,800 lines. Ids never change meaning; a removed id is not reused.

| Item | Change | Why |
|---|---|---|
| R01..R08, R10, R11, R13, R14, R16, R18..R20, R23, R24 | Kept as written (small additions: ER ids in R04, R10, R11, R13, R14, R23; notes in R18, R19, R24; motion growth in R05, R07, R08, R10, R11, R13, R14 per 1.7.1) | Still right |
| R09 | **Changed**: only status chips, stat cards, tabs (300 lines, was 650; 500 with the motion growth). The table look and the pager look moved out | A02 rewrites the table; styling it twice is waste |
| R12 | **Changed**: only the density service, the account-dialog switch and the line-grid rows (500, was 750). Table rows, pager and filter-bar gap moved out | Tokens come with A02, A05, L02 |
| R15 | **Changed**: screens adopt the skeleton shapes (650, was 800); the table skeleton is built inside A02; adds ER-05 | Same reason |
| R17 | **Changed dependencies**: needs R04, R07, the id merge G02 and client batch CS4 (it carries ids); it left the fast path, R27 took its place | Owner: slices that carry ids wait for the integration branch; roadmap conflict C3 |
| R21 | **Removed**: merged into A05 (paging toolbar) | Same scope, plus the 10,000-row offset limit and the density button |
| R22 | **Changed**: shortcuts only (500, was 800); table arrow keys are now A04 | A04 does the full grid keyboard model |
| R25 | **New**: sign-in page redesign (`docs/design/2026-refresh-login.md` and `-login-visual.md`, hand-off of 2026-10-11) | The login specs ask for it (about 900 lines, sized 1,000 here) |
| R26 | **New**: re-sign-in in place (ER-01, login spec 4.11) | The Major finding of the edge retest; separate from the page redesign as the login spec asks |
| R27 | **New**: quantity entry and double-save safety (NR-01, NR-07 = ER-13, NR-08, NR-09) | Top priority of the novice retest; wave 5 |
| R28, R31, R32 | **New, decided**: document corrections (recall and the Edit button, send back and cancel on Approved, their e2e). R28 was a placeholder for NR-02; it now carries the decided design (`document-corrections.md`: DC-FE1 = R28, DC-FE2 = R31, DC-E2E = R32). Backend item B19 is replaced by B21 and B22 (DC-BE1, DC-BE2) | The owner's decision closes NR-02, PR-02, PS-11 and UX question Q3; the roadmap calls them DC-FE1/FE2/E2E and drops the old R28 id, this plan keeps the id for the same job |
| R29, R30 | **New**: phone document screens and phone lists/details (NR-03 to NR-05, NR-10 to NR-18, NV-25) | The remaining frontend-owned retest findings; R29 waits for a designer ruling; NR-11 moved to R31 |
| A01..A22 | **New**: programme 1, advanced UI (custom components on Angular CDK behaviour primitives only) | Owner: more advanced |
| Q01..Q12 | **New**: programme 2, stable (quality gate, error handling, offline, lint, contract tests, gallery, CI) | Owner: more stable |
| L00 | **New, wave 2**: content containment (long unbroken values must never cause horizontal scroll) | Owner-reported defect: the Partners list scrolls sideways; same cause as NR-10 |
| Q05 | Sized 750 (a draft had 1,050): the session-ended part is now R26 | Keeps each slice small |
| L01..L09 | **New**: programme 3, responsive (layout system, shell behaviours, container queries, print, forced colours, test matrix) | Owner: more responsive |
| Waves | **Re-sequenced** again: 34 waves, at most 3 frontend builds each (the roadmap runs 2 while both backend slots are busy); named serial lanes; the fast path is 10 slices (R17 left it, R27 joined) | Section 2 |
| M01..M08 and growth of 20 slices | **New**: the motion language (`2026-refresh-motion.md` section 7 applied; owner accepted every default of M1..M8). Supersedes rulings C1, C2, C3, C7, C13 and the motion lines of D8 by N16..N19. 4,550 new lines + 2,130 growth | Owner: interface that feels alive and stays fast |
| RP00..RP11 | **New**: roles and permissions screens (`roles-permissions-ux.md`), reconciled with `adr-roles-permissions.md` in 1.8.1 (per-person warehouse flag, two approval tiers, configurable and audited cost visibility with unchanged defaults), plus RP00 (the small permission-derived profile helper before R10) and RP11 (approval tier in Settings). 9,900 lines | Owner: configurable roles |
| R33 | **New**: follow-up of the money and QA backend PRs #44 to #46 (client batch CS1) | `docs/pm/ROADMAP.md` |
| G01, G02 | **New**: the web side of the accepted GUID decision (ADR PR-8 and PR-9). Slices marked H in the Ids column now wait for G02 (owner decision); R17 left the fast path for that reason | `adr-guid-keys.md` Accepted; roadmap conflicts C3, C5 |
| Backend | B1..B6 kept; B7..B20 new (all optional or with a stated fallback; B13 is required only for the stock-value chart; B18 is the account unlock that the login spec called B7; B20 asks for tighter text limits); B21, B22 (document corrections BE1, BE2) and B23 (audit filters) added; B19 removed; the roles ADR slices RP-0..RP-9 and the GUID PRs are tracked as readiness waves in 3.3 | Section 3 |
| Definition of done | **Added**: independent review before merge for every slice (PR-REVIEW section 8), specialists in 4.5; a long-value check (row 16), a motion row (17) and the generated-client batch rule (18); the effort estimate includes the review and a 25% rework allowance | Decisions of the product owner |
| Inputs that arrived during this revision | The sign-in specs, the second QA retests (edge and novice) and `docs/architecture/adr-guid-keys.md` (now Accepted): ids become opaque strings | Rulings C14, C15, N13; sections 0.3, 0.4, 1.9, 1.10; risk K29 |
| Consistency with `docs/pm/ROADMAP.md` | Sections 2.4 (client batches CS1..CS5), 2.8 (capacity and estimate), 3.3 (readiness) and 1.10 (ids) follow the roadmap; conflicts C3 to C7, C13 to C15 of its section 8.3 are resolved here (see HANDOFFS) | Project-manager is the calendar owner |
| Tracking | v1 total 14,760 lines, 24 slices; v2 total shown in section 1.1 | |

## Decisions already taken (kept) and new ones

All product-owner defaults in both specs are accepted (UX Q1..Q12, visual Q1..Q10), plus these decisions folded in everywhere below:

| # | Decision | Lands in |
|---|---|---|
| D1 | Money shows exactly 2 decimals on screen, thousands separator, never abbreviated; no sub-cent input. Quantities keep each product's own precision: up to 4 decimals (`MAX_QTY_DECIMALS = 4`, the API limit), trailing zeros trimmed. The UX file says "3 decimals" (FR-05, D10); the code and API say 4. The code wins: 3 would silently round a stored 4-decimal quantity. Applies to charts and exports too (A19, A10). | R04 |
| D2 | "RM" appears only in column headers and labels ("Unit cost (RM)"), never as a prefix on a value. | R04 |
| D3 | Login error banner clears when either field is edited and is not kept across a reload; it never shows together with empty-field messages. | R02 |
| D4 | Required = red asterisk (`aria-hidden`) + screen-reader-only "required" + `required`/`aria-required` + one legend "* means required" per form. | R02 |
| D5 | Staff never see cost or value; Clerk sees cost (`cost.view`) but never valuation (`valuation.view`). Decided by permission, never role name. Every new surface (Home widgets, charts, palette, notifications, drawer, exports, saved views, paste, print, skeletons, aria-labels, titles) is checked for this; the table in section 4.4 lists the rule per new surface. | R15, R17..R20, A09..A20, L07 |
| D6 | Reports: the date-range error message is shown (PR-03); Product, Unit and Unit cost (with `cost.view`) are on screen and SKU/date never wrap (PR-06, PS-22). | R03, L04 |
| D7 | **No component library.** Our `app-*` components stay. Angular CDK is allowed as behaviour primitives only (section 1.3). | A01 |
| D8 | Density only at >= 1024px with a fine pointer, old token names as aliases until R24, bundle growth at most 6 kB raw per slice unless the slice states and justifies more (rulings C1..C15 below). The earlier motion decisions of D8 (skeleton pulse, no press scale, instant hover) are **superseded** by N16..N19 (the owner accepted the motion language). | R05..R08, R24 |

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
| C14 | Desktop toast position (new in v2, from ER-02) | top right under the header | n/a | Toasts cover the Save button of the page header (ER-02). On >= 1024px the stack sits bottom right; when the floating bulk-action bar (A09) is visible it sets `--ims-floating-bar-height` on `html` and the toast host lifts by that amount. Phones keep ruling C6 (directly under the top bar). |
| C15 | Theme chosen on the sign-in page (new in v2, from the login spec 4.7) | C5: device copy, per user | n/a | A theme or language chosen on the sign-in page is applied at once from the device copy and becomes the preference of the user who signs in next; the sign-out reset to `system` stays. One added sentence to C5, no contradiction. |

Rulings C1, C2, C3, C7, C13 and the motion lines of D8 are superseded by N16 to N19 (0.2): shimmer replaces the pulse (C1), press scale 0.97 returns (C2), hover fades through an overlay (C3), dialogs spring and sheets slide with new durations (C7), the top bar trickles instead of looping (C13). The earlier table rows stay for history; N16 wins.

### 0.2 New rulings in version 2

Same rule: implementers do not re-argue these.

| # | Topic | Ruling |
|---|---|---|
| N1 | Component library | None. No bake-off. A lint rule (Q07) forbids importing any UI library (`@angular/material`, `primeng`, `ng-zorro-antd`, `@ng-bootstrap`, ...), so the decision cannot drift. |
| N2 | Angular CDK | Allowed, behaviour only, and only inside `libs/shared/ui` (lint-enforced, Q07; the module-boundary rule keeps pages away from it). Used packages: `a11y` (live announcer, key managers, focus trap for non-`<dialog>` panels), `overlay` (positioning), `listbox`, `scrolling` (virtual scroll), `drag-drop` (column reorder), `clipboard` (copy). Not used: `table`, `menu`, `layout`, `dialog`, `stepper`. We take the CDK's structural Sass mixins only (a few hundred bytes of CSS) and never its theme or prebuilt CSS. Public inputs and outputs of our wrappers are plain types: no CDK type leaks out of `@ims/ui`. |
| N3 | Table markup | `app-table` becomes an ARIA table built on CSS grid (`role=table\|rowgroup\|row\|columnheader\|cell`, `role=grid` when keyboard navigation or editing is on) instead of a native `<table>`, because sticky header, pinned column, resize and virtual scroll are unreliable on native tables. It is the biggest accessibility risk of the plan (K16); A02 carries the contract tests and a qa-edge re-check. Existing inputs (`columns`, `rows`, `rowLink`, `sort`, `appCell`, `columnWhen`) keep working so no list page breaks. |
| N4 | Paging | Server-side paging stays the default everywhere (25/50/100). Continuous (virtual) scrolling is opt-in per table, uses seek paging (`afterId`/`nextCursor`) and exists only where the API supports it (documents, ledger, audit today). The API refuses offset pages deeper than 10,000 rows and caps a page at 200 (`Limits.MaxOffsetRows`, `MaxPageSize`); the UI says so instead of failing. |
| N5 | Container queries | A component adapts to the width of its container, not the window. Viewport media queries are allowed only for the shell, the page frame and user-environment features (`pointer`, `hover`, `orientation`, `prefers-*`, `forced-colors`, `print`). Q07 lints it. |
| N6 | Where e2e runs | PR gate = Playwright against the production build with a stubbed API (no database, about 10 minutes). Real-API scenarios stay nightly (smoke on 3 engines) and weekly (full, Chromium). Section 1.4. |
| N7 | Visual baselines | Stored on an orphan branch `visual-baselines`, written only by a manual workflow after review. Not in the repo history, not in LFS. Section 1.4, Q02. |
| N8 | Bulk actions | Read-only or draft-creating only (export, copy, "create a receipt from these low-stock products"). Bulk approve, reject, cancel, post or reverse are NOT in this plan (override reasons, self-approval and per-document `allowedActions` make them a product decision, open question P2). |
| N9 | Offline | Safe reads may be served from the service-worker cache; no command (save, submit, approve, post, reverse, cancel) is ever queued; while offline command buttons say why they are disabled. |
| N10 | Chart numbers | Axis labels and tooltips follow FR-05: money with 2 decimals, thousands separator, no "k" abbreviation. Charts never sum quantities across units (movements are counted as documents). |
| N11 | Icons | `icon.paths.ts` is append-only and alphabetical; each slice that needs an icon lists it and appends it (R05 no longer owns every icon). |
| N14 | Review gate | Every slice passes an independent review before merge (PR-REVIEW section 8, decided by the product owner), in addition to CI: definition of done row 15 and the table in 4.5. |
| N15 | Content containment | Content never causes horizontal page scroll at any width. Only the table's own scroll region and `pre` / code blocks may scroll sideways, and they are focusable and labelled (`appScrollRegion`). Long values are wrapped (`overflow-wrap: anywhere`), clamped with a real "Show more" control, or ellipsised with the full value reachable by keyboard; hiding the symptom with `overflow-x: hidden` on `html` or `body` is forbidden. A literal `white-space: nowrap` is a lint error. Built in L00 (wave 2); every later component follows it. |
| N16 | Motion language | `docs/design/2026-refresh-motion.md` is the motion rule; its section 1.2 rows replace C1, C2, C3, C7 (durations), C13 and the motion lines of D8, visual section 4, UX 7.6 and 11 item 3, and login visual section 11. Answers M1..M8 are accepted with the defaults of that file. |
| N17 | Animated properties | Animate `opacity` and `transform` (plus the named `clip-path` and `grid-template-rows` exceptions); colour, shadow and size effects are overlays. Enforced by `check-motion.mjs` and stylelint (M01). |
| N18 | No animation library | No library and no `@angular/animations` (deprecated in Angular 22, removed by M01); `animate.enter`, `animate.leave`, CSS and the Web Animations API only. |
| N19 | `data-motion` modes | `data-motion` = full, lite, reduced or off is the single switch; reduced can only be requested (by the device or the in-app setting), never bypassed. Skeletons shimmer for 6 sweeps then stop; press scale 0.97; hover fades by overlay (not on table rows); route and theme transitions use view transitions. |
| N20 | Roles and permissions | One role per person plus exceptions; "sees every warehouse" is a per-person flag, not a role setting; two approval tiers are two permissions; cost and valuation visibility is configurable by administrators with a reason and an audit entry, system-role defaults unchanged (Staff none, Clerk cost, others cost and valuation). The web never compares a role name. Reconciliation table: 1.8.1. |
| N21 | Public ids and the generated client | The GUID decision is accepted. Slices that carry ids (mark H) wait for G02; the client is regenerated only in the roadmap's five batches CS1..CS5 by one agent (2.4). |
| N22 | Document corrections | A mistake before posting is fixed by going back to Draft: recall ("Edit") by the submitter, send back and cancel on Approved by a reviewer with a reason. Recall asks nothing when no one else is affected. |
| N23 | Capacity | The plan allows three frontend builds per wave; while both backend slots are busy the orchestrator runs two (roadmap section 4). The order does not change, only the calendar. |
| N12 | Preferences | Theme, density, saved views, page size and recents are stored per user in this browser (`UserPreferenceStore`, R06). Roaming across devices needs backend item B9 and a product decision (P1). |
| N13 | Opaque ids | All new code treats ids (documents, products, warehouses, users, cursors) as opaque strings: never `Number(id)`, never compared numerically, never used as an array index, never assumed to be sortable. The ADR `adr-guid-keys.md` (Proposed) would make every id a UUID string and `afterId` an opaque `cursor`; code written to this rule survives the flip as a type-only change. Stored device state that holds ids (recents, saved views, notification "seen" set, selection) validates its shape on read. A06, A09, A11, A16, A18, R18 and Q06 (cache version bump) are the slices that touch ids. |

### 0.3 Edge-accessibility retest (ER-01..ER-21) triage

`docs/qa/retest-edge-accessibility.md` landed after version 1. Frontend findings are assigned to a slice (the slice's acceptance names the ER id); backend findings go to section 3.

| ER | Severity | Assigned to |
|---|---|---|
| ER-01 session ends while the editor is dirty (Major) | Major | R26 (in-place re-sign-in, form kept, no "Discard changes?" loop), after R25 |
| ER-02 toasts cover Save | Minor | R11 + ruling C14 |
| ER-03 focus after Save, ER-04 empty submit focus and 409 on field, ER-19 Large-document hint | Minor/Polish | R14 |
| ER-13 double Save (same cause as NR-07) | Minor | R27 |
| ER-05 `/stock/:productId` does not reload | Minor | R15 |
| ER-09 Account button name | Minor | R10 |
| ER-10 repeated row-action names | Minor | A04 |
| ER-11 duplicate announcements, panel copy | Minor | R11 (lists), R25 (sign-in banner and toast) |
| ER-12 quantity minimum | Minor | R04 |
| ER-16 titles, h1 of login | Polish | R25 (login `h1` "Sign in"), A22 (record titles, in-route not-found) |
| ER-17 date focus ring | Polish | R23 |
| ER-18 tabs without `aria-controls` | Polish | R09 |
| ER-20 closed dialog `aria-labelledby` | Polish | R13 |
| ER-15 | Minor | backend RP-3 closes it (access snapshot, 401 on deactivation, rights fresh within seconds); the web side is RP07 (banner "Your access was changed") |
| ER-06, ER-07, ER-08, ER-14, ER-21 | Minor/Polish | backend or security (section 3.2); ER-14 also constrains Q06 |

### 0.4 Novice retest (NR-01..NR-18, open NV items) and open edge items: triage

`docs/qa/retest-novice-warehouse-staff.md` and the still-open items of the edge retest are assigned like this. Decisions given with the request: NV-27 is decided (use the `dd MMM yyyy` date field, R23); NV-28 stays a draft until the native review (marker `TODO-review`, risk K15); NR-02 is being decided by the owner through the designers (placeholder R28).

| Finding | Severity | Slice |
|---|---|---|
| NR-01 quantity field must select its value on focus (also the Low stock shortcut) | Major | **R27** (wave 5, first of the retest items) |
| NR-02 a Submitted document cannot be returned to draft | Major | **R28** (recall, Edit button, Post dialog), R31 (send back, cancel on Approved) and R32 (e2e); decided in `docs/design/document-corrections.md`; backend B21, B22 |
| NR-11 stock-changed banner contradicts the page; PR-02 and PS-11 Cancel on Approved | Minor/Major | R31 |
| NR-03 saved-draft action bar wraps to two rows, NR-04 Submitted view order, NR-12 next step after Post, NR-16 stepper and line card | Minor/Polish | R29 (after a ruling from ui-ux-designer) |
| NR-05 Low stock "Short by" label, NR-13 Add item hints, NR-14 search placeholder, NR-15 link targets, NR-17 product page chip and ledger rows, NR-18 announcer and wording, NV-25 Back with the sheet open | Minor/Polish | R30 (NR-10 unbroken names is L00) |
| NR-06 login banner and "(required)" | Minor | R02 (as planned) and R25 |
| NR-07 double tap Save sends two requests (same cause as ER-13) | Minor | R27 |
| NR-08 decimal Backspace and caret | Minor | R27 |
| NR-09 "cost" word in the Staff remove dialog | Minor | R27 |
| NV-07 reload mid-edit loses work | Major (open, partly) | not built by decision: no draft is written to browser storage (cost data on shared devices); the browser warns before leaving; a session ending is R26. Owner question P9 |
| NV-08 Home tiles not tappable, no line count on cards | Major (open, partly) | R17 (Doer Home rows are links) and R19; the line count on the card needs the optional `lineCount` field (A08 API note) |
| NV-10 footer a third of the screen | Minor (open, partly) | R29 (same as NR-03, NR-04) |
| NV-27 date format | Polish, decided | R23 (`dd MMM yyyy`) |
| NV-28 zh wording of Submit & post | Polish, decided | stays as drafted until the native review (K15) |
| EA-06, EA-07 | Minor (open, partly) | R14 (ER-03, ER-04), plus "Keep editing" returns focus to the control (R14) |
| EA-15, EA-26 | Minor/Polish | R11 (panel copy, one announcement) |
| EA-20 | Minor | R15 (ER-05) |
| EA-24 | Minor | R25 (one alert, translated wait); the limiter is backend (S7) |
| EA-29 | Polish | R10 (Account button name) and R23 (date focus ring) |
| EA-30 same product on two lines | Polish | owner question P10; the UI merges on add and on paste |

Backend-owned findings are listed once, in section 3.2.

## 1. Slices

### 1.1 How to read the slice list

Sizes are changed frontend lines including specs, excluding lockfiles, `web/openapi/**` and the generated `api-client.ts`. For Q and L slices the size also counts the e2e specs and tooling scripts they add (each slice says the split). Every slice must stay under 1,200 lines (hard cap); the builder splits it when it passes 1,000 during the build. The repository's PR guard already rejects a pull request over 1,500 changed lines (`MAX_PR_CHANGED_LINES`, `scripts/guard/lib.mjs`).

Legend: `UX` = slice in the UX file, `V` = step in the visual file. "Re-check" = the QA personas that must run afterwards (`novice` = qa-novice-warehouse-staff, 360px phone; `power` = qa-power-supervisor, desktop; `edge` = qa-edge-accessibility). "API" = the backend fields or endpoints the slice needs (item ids refer to section 3); "none" means today's API is enough. A slice never waits for a backend item unless it says so; where an item is missing the slice states the fallback.

Seven programmes, one id series each (access uses RP, the public-id flip uses G):

| Prefix | Programme | Slices | Lines |
|---|---|---|---|
| R | Refresh (version 1 scope, sign-in page and re-sign-in, QA round 3, document corrections, money-PR follow-up) | 32 | 19,750 |
| A | Advanced UI (custom components on Angular CDK primitives) | 22 | 17,720 |
| Q | Stable (quality gate, errors, offline, lint, contract tests, gallery, CI) | 12 | 8,220 |
| L | Responsive (content containment, layout system, shell, container queries, print, forced colours, matrix) | 10 | 7,500 |
| M | Motion (8 new slices; the growth of 20 slices is inside the other rows) | 8 | 4,550 |
| RP | Access: roles and permissions screens (RP00..RP11) | 12 | 9,900 |
| G | Public-id web flip (G01, G02) | 2 | 1,200 |
| | **Total** | **98** | **68,840** |

Version 1 was 24 slices and 14,760 lines; the previous revision of this plan was 73 slices and 49,260 lines. Every slice, in addition to its own tests, keeps FR-01..FR-09 true (UX section 0.2) and the definition of done in section 4.

Not covered here on purpose (not part of this refresh): PR-01 (backend valuation defect), PR-02 / PS-11 / Q12 (Cancel on Approved stays unchanged), PR-04, PR-05, PR-07 (no "stock without cost" widget), PR-08, PS-14, and any state-changing bulk action (N8).

### 1.2 Master list (id, size, dependencies, wave)

| Slice | Name | Programme | Size | Depends on | Wave | Ids |
|---|---|---|---|---|---|---|
| R01 | Palette, aliases, contrast gate | Refresh | 310 | none | 1 | - |
| R02 | Required marker, legend, login behaviour | Refresh | 600 | none | 1 | - |
| R03 | Reports fixes | Refresh | 350 | none | 7 | T |
| R04 | Money format and input rules | Refresh | 450 | R02 | 2 | - |
| R05 | Non-colour token foundation and icons | Refresh | 660 | R01 | 2 | - |
| R06 | Preferences, theme per user, no flash, PWA colours | Refresh | 700 | R01 | 6 | T |
| R07 | Reduce-motion contract, spinner, skeleton primitives, delay helper | Refresh | 450 | R05 | 3 | - |
| R08 | Buttons and form controls | Refresh | 900 | R01, R02, R05 | 4 | - |
| R09 | Status chips, stat cards, tabs (changed) | Refresh | 500 | R01, R05, M02 | 11 | - |
| R10 | Shell, tab bar, action bar | Refresh | 520 | R05, RP00 | 4 | - |
| R11 | One failure, one announcement | Refresh | 930 | R08 | 6 | T |
| R12 | Density (changed) | Refresh | 500 | R06, R10 | 11 | - |
| R13 | Overlays: dialog, sheet, toast, banner, card, empty state | Refresh | 850 | R07, R11, M01 | 8 | - |
| R14 | Saved state, row highlight, live regions, error summary | Refresh | 780 | R08, R11 | 14 | T |
| R15 | Skeleton shapes per screen, loading rules (changed) | Refresh | 800 | R07, R09, R12, R13, A02 | 16 | T |
| R16 | Command palette component and app-kbd | Refresh | 500 | R05, R13 | 16 | - |
| R17 | Role-aware Home: Doers (dependency changed) | Refresh | 900 | R04, R07, G02 | 14 | H |
| R18 | Command palette: logic, trigger, recents, actions | Refresh | 750 | R06, R10, R12, R16, G02 | 19 | H |
| R19 | Role-aware Home: Reviewer, Overseer, Admin | Refresh | 900 | R17, G02 | 17 | H |
| R20 | Palette search and phone button | Refresh | 700 | R18, G02 | 20 | H |
| R22 | Keyboard shortcuts (changed) | Refresh | 500 | R18, A04 | 31 | T |
| R23 | Date field dd MMM yyyy | Refresh | 600 | R03, R08 | 19 | - |
| R24 | Clean-up | Refresh | 300 | all | 34 | - |
| R25 | Sign-in page redesign (from the login spec) | Refresh | 1,000 | R01, R02, R05, R06, R08, R11, A01 | 8 | - |
| R26 | Re-sign-in in place (ER-01) | Refresh | 450 | R25, R14, R11 | 17 | - |
| R27 | QA round 3 (a): quantity entry and double-save safety | Refresh | 450 | R04, R08 | 5 | T |
| R28 | Document corrections (a): recall, Edit button, Post dialog | Refresh | 750 | R11, R27 | 7 | T |
| R29 | QA round 3 (c): phone document screens | Refresh | 700 | R10, R14, R27, R28 | 24 | T |
| R30 | QA round 3 (d): phone lists, details, wording | Refresh | 600 | A13, R15, A02 | 25 | T |
| R31 | Document corrections (b): send back, cancel on Approved, shortage banner | Refresh | 650 | R28, R13 | 9 | T |
| R32 | Document corrections e2e (scenario 30) | Refresh | 500 | R31 | 26 | - |
| R33 | Follow-up of the money and QA backend fixes (CS1): ledger balance, one low-stock rule, search | Refresh | 200 | none | 2 | T |
| A01 | CDK foundation and app-popover | Advanced UI | 650 | R05 | 5 | - |
| A02 | Table core: ARIA grid, sticky header, pinned first column | Advanced UI | 1,000 | R05, L01 | 5 | - |
| A03 | Table column controls: visibility, resize, reorder | Advanced UI | 1,000 | A01, A02 | 7 | - |
| A04 | Table keyboard grid navigation, row-action names | Advanced UI | 800 | A02, A03 | 9 | - |
| A05 | Paging toolbar: First/Last/page size, density button (replaces R21) | Advanced UI | 600 | A02, R06, R12 | 13 | - |
| A06 | Saved views and multi-sort | Advanced UI | 900 | A03, A05, R06 | 15 | T |
| A07 | Filter chips bar and URL state | Advanced UI | 900 | A06, G02 | 17 | H |
| A08 | Row expansion: document lines | Advanced UI | 620 | A04, L04 | 20 | T |
| A09 | Bulk selection and floating action bar | Advanced UI | 840 | A04, G02 | 21 | H |
| A10 | Export CSV / Excel of the current view | Advanced UI | 600 | A09 | 22 | - |
| A11 | Continuous (virtual) scroll mode, 10,000+ rows | Advanced UI | 1,000 | A04, A05, G02 | 24 | H |
| A12 | Combobox: async search, recents, scan mode | Advanced UI | 840 | A01, R08 | 15 | - |
| A13 | Combobox adoption: product picker, partner | Advanced UI | 500 | A12 | 21 | T |
| A14 | Inline-editable lines grid: core | Advanced UI | 1,100 | A02, A04, A12, R04, R14 | 23 | T |
| A15 | Lines grid: paste, undo, editor integration | Advanced UI | 900 | A13, A14 | 25 | T |
| A16 | Side drawer: document peek | Advanced UI | 920 | A01, R13, A02, G02 | 19 | H |
| A17 | Document timeline | Advanced UI | 640 | A16 | 27 | - |
| A18 | Notifications panel | Advanced UI | 760 | A01, R10, R17, G02 | 28 | H |
| A19 | SVG chart primitives | Advanced UI | 1,000 | R05, L01, L06 | 30 | - |
| A20 | Home dashboard charts | Advanced UI | 900 | A19, R19 | 31 | T |
| A21 | Empty-state illustrations | Advanced UI | 450 | R13 | 30 | - |
| A22 | Breadcrumbs and consistent page header | Advanced UI | 800 | R10, L01 | 32 | - |
| Q01 | Stub-API harness and screen registry | Stable | 900 | none | 1 | T |
| Q02 | Visual regression and baseline storage | Stable | 700 | Q01, R06 | 10 | - |
| Q03 | axe on the matrix and three-engine smoke | Stable | 600 | Q01 | 10 | - |
| Q04 | Bundle and lazy-chunk budgets | Stable | 350 | R01 | 18 | - |
| Q05 | Global error handling, route errors, GET retry | Stable | 750 | R11 | 26 | - |
| Q06 | Offline banner and safe read cache | Stable | 800 | Q05, R06, G02 | 27 | H |
| Q07 | Lint and strictness: tokens, inline styles, CDK/library imports | Stable | 700 | R05 | 22 | - |
| Q08 | Contract tests: form controls (jest-axe) | Stable | 700 | R08 | 28 | - |
| Q09 | Contract tests: remaining components | Stable | 800 | R09, R13 | 29 | - |
| Q10 | Development gallery /__gallery | Stable | 760 | R09, R13 | 23 | - |
| Q11 | CI wiring, triage rules, flip visual gate | Stable | 400 | Q01, Q02, Q03 | 16 | - |
| Q12 | Interaction, CLS and Lighthouse budgets | Stable | 760 | Q01, A11, Q10 | 27 | - |
| L00 | Content containment: long values never cause horizontal scroll | Responsive | 600 | none | 3 | - |
| L01 | Layout system foundation | Responsive | 800 | R05 | 4 | - |
| L02 | Layout system applied to the screens | Responsive | 1,000 | L01, R10, A01 | 13 | - |
| L03 | Shell behaviours: tab bar, collapsible rail, sidebar, keyboard | Responsive | 1,000 | L01, R10, M03 | 13 | - |
| L04 | Table-to-card by container width, column priorities | Responsive | 900 | L01, A02 | 12 | - |
| L05 | Touch and pointer rules, text zoom and reflow fixes | Responsive | 600 | L01, R08, L08 | 30 | - |
| L06 | Forced colours and high contrast | Responsive | 600 | R05, R13 | 29 | - |
| L07 | Print stylesheets for receipt and issue | Responsive | 700 | R14, R04 | 32 | - |
| L08 | Responsive test matrix automation | Responsive | 800 | Q01, L01 | 15 | - |
| L09 | Large screens, foldables, docked drawer | Responsive | 500 | L03, A16 | 32 | - |
| M01 | Motion foundation | Motion | 650 | R05, R07 | 6 | - |
| M02 | Motion primitives | Motion | 900 | M01 | 8 | - |
| M03 | Route and theme transitions, top bar | Motion | 650 | M01, R06, R10 | 12 | - |
| M04 | Home motion | Motion | 400 | M02, R17 | 18 | - |
| M05 | Sign-in motion | Motion | 300 | M02, R25 | 12 | - |
| M06 | Table, list and editor motion | Motion | 700 | A03, A04, R14, M02, R30, A15 | 28 | - |
| M07 | Motion test harness | Motion | 600 | Q01, M01 | 9 | - |
| M08 | Pull to refresh on phone lists (Could) | Motion | 350 | R15, R30, M02 | 33 (Could) | - |
| RP00 | Permission-derived profile helper (P01-lite) | Access | 300 | none | 3 | T |
| RP01 | Access foundations: permission catalog store, derived profile, guards | Access | 800 | R06, R10, R04 | 14 | T |
| RP02 | Roles list, Add role, retire, delete | Access | 900 | RP01, R08, R13, A02, A04, A01 | 18 | H |
| RP03 | Role editor core: checklist, dirty state, guards | Access | 1,100 | RP02, R11, R14, R26 | 20 | H |
| RP04 | Role editor panels: preview, people, changes, review and save | Access | 1,100 | RP03 | 21 | H |
| RP05 | Person's access page: role, warehouses, effective list | Access | 1,150 | RP03, A16 | 22 | H |
| RP06 | Exceptions: give or take away, expiry | Access | 850 | RP05, R08 | 25 | H |
| RP07 | Safety rules and live rights change | Access | 850 | RP05, R11, R26, R10 | 23 | H |
| RP08 | Compare roles and people (Could) | Access | 900 | A02, A04, RP05 | 31 | H |
| RP09 | History links, activity, audit sentences | Access | 650 | RP05 | 26 | H |
| RP10 | Roles and access e2e (scenarios 40 to 45) | Access | 900 | RP07 | 29 | H |
| RP11 | Approval tier in the Settings rule form | Access | 400 | R08, RP01 | 24 | T |
| G01 | Public-id web flip (GUID plan PR-8) | Public ids | 900 | none | 10 | H |
| G02 | Public-id e2e fixes and merge of the integration branch (GUID plan PR-9) | Public ids | 300 | G01 | 11 | H |

Slices marked H in the Ids column also depend on G02 (the dependency column shows it). Backend readiness is a second constraint (3.3): RP01 starts no earlier than wave 13, RP02 16, RP03 19, RP11 19, RP07 19, RP04 20, RP05 21, RP06 21, RP08 22, RP09 22; G01 no earlier than 9; R28 no earlier than 5; R33 no earlier than 2.

### 1.3 Programme R: Refresh (R01..R33)

These are the version 1 slices. The text is unchanged except where a title says "(v2)" or a line starts with "v2" or names an ER finding. Dependencies and waves are the ones in the master list above, not the ones in the older prose. R21 is removed. R25 and R26 are new; R27 to R30 are the QA round 3 slices; R28, R31 and R32 are the decided document corrections; R33 is the follow-up of the money PRs. R25 and R26 are new (they close the hand-off of the sign-in specs and the Major finding ER-01). The R blocks have no "API" line: their backend needs are B1 to B6 and B18 in section 3 (B1 for R17, B2 and B6 for R19, B4 for R04, B5 for R20, B18 for R25). The sizes in each block are authoritative.

#### R01 Palette, aliases, contrast gate

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

#### R02 Required marker, legend, login behaviour

- Goal: D4 and D3. The smallest Must item; ships first because it fixes a known accessibility defect.
- Files: `web/libs/shared/ui/src/lib/components/form-field/*` (asterisk span `aria-hidden`, sr-only word from `UI_TEXT.required`, remove the "(required)" text; the field publishes `required` to its control through `FORM_FIELD` in `form-field.token.ts`), `input/`, `select/`, `textarea/`, `radio-group/` (apply `required` or `aria-required`; radio `legend` carries the asterisk), new `web/libs/shared/ui/src/lib/components/form-legend/` (`app-form-legend`, `aria-hidden`, the text from `UI_TEXT.requiredLegend`), `web/libs/shared/ui/src/lib/tokens/ui-text.token.ts` and `web/libs/core/i18n/src/lib/services/ui-text.provider.ts`, the three catalogs (`form.requiredLegend`; reuse `ui.required`), and the forms that need `<app-form-legend />` as their first element: `libs/containers/authentication/.../login`, `documents/.../document-editor` and `line-editor` (and the Add item sheet), `catalog/.../product-edit`, `catalog-lists`, `warehousing/.../warehouses`, `partners`, `administration/.../users`, `settings`, and every dialog that has a required reason (`confirm-dialog`). Login: `login.component.ts/.html`.
- Decision (the API contract the UX file asked fe-architect for): the legend is an explicit `<app-form-legend />` placed by the page or dialog (not injected by `app-form-field`). A form with no required field simply does not place it. A unit test per container asserts "has a required field implies has the legend".
- Login rules (D3): `auth.login.reset()` on any `valueChanges` of either control, and before an invalid submit, so the banner and the field messages are never visible together; nothing about the error is kept across a reload (the state is in memory; add a test that proves no storage write). The session-expired warning banner is unchanged.
- Size: 600.
- Depends on: none (uses the old token names; works with or without R01).
- Acceptance: RQ-01 to RQ-06 (UX 3.3). In addition: Login shows no banner after typing a character in either field; Sign in with both fields empty shows the two field messages and no banner even if a banner was visible before; a failed sign-in with both fields filled shows only the banner; reload shows neither; the accessibility name of the username field is "Email or username" and the control exposes the required state, so "required" is announced once (ruling of the R02 review; UX 3.3 items 1 and 2); `(required)` no longer appears anywhere (`grep` the catalogs and templates).
- Tests: Spectator on form-field (required marker, sr-only text, attribute on control), radio-group legend, legend component, login (the four rules above); one container test per form (legend present); e2e new `14-required-marker` (login, receipt, issue, one dialog: accessible names, legend count) and update `08-session` for the banner rules; page object `LoginPage` locators already use `^Email or username`, keep them.
- Re-check: edge (screen reader names, 320px wrap RQ-06), novice (forms on phone).

#### R03 Reports fixes

- Goal: D6. PR-03 (From after To shows "Something went wrong"), PR-06 and PS-22 (SKU and date wrap; Product, Unit and Unit cost not on screen).
- Files: `web/libs/containers/reports/src/lib/reports/reports.component.{ts,html,scss,spec.ts}`, `web/libs/features/reports/src/lib/services/reports.store.ts`, `web/libs/shared/utils/src/lib/forms/server-errors.ts` (only if the `errors.to` mapping helper needs a small addition), the three catalogs (`filter.rangeInvalid` = "From must be on or before To."). No change to `app-table`: the nowrap goes in the cell templates the Reports page already declares (`appCell`), styled from `reports.component.scss`, so R03 does not collide with the table lane.
- Behaviour: a client validator on the two date controls blocks the request and shows "From must be on or before To." under the To field; if the API answers 400 with `errors.to` the same field shows the server sentence; in both cases the table is cleared (no stale rows) and the pager is hidden. Columns for the movements table: Posted, Document (with the Reversal badge), Type, Status, Warehouse, Partner (may wrap to a second line), SKU, Product, Qty, Unit, Stock effect, Unit cost (only `cost.view`, the existing `columnWhen`), Amount (only `cost.view`). SKU, document number and date are `white-space: nowrap`.
- Size: 350.
- Depends on: none.
- Acceptance: with From 12 Oct 2026 and To 10 Oct 2026 no "Something went wrong" and no Retry; the To field shows the sentence (announced once, `aria-describedby`); the old "1-6 of 6" footer is gone; correcting the dates reloads the table. At 1280 wide the supervisor sees every column including Amount without horizontal scroll inside the region; at 1024 the region scrolls but SKU and date stay on one line (TB-01, TB-05). Product, Unit and Unit cost are visible to Supervisor and Owner and Admin; the Unit cost column is absent for any user without `cost.view`.
- Tests: reports component spec (range error client and server, columns per permission, cleared table); e2e new `15-reports-range` (supervisor: bad range message; wide table columns; no wrap measured by `boundingBox().height` of the SKU cell equal to one line).
- Re-check: power (PR-03, PR-06, PS-22 closed), edge.

#### R04 Money format and input rules

- Goal: D1 and D2 everywhere on screen and in inputs.
- Files: `web/libs/core/i18n/src/lib/pipes/format.pipes.ts` (+spec: `AppMoneyPipe` already prints 2 decimals; change `null`/`undefined` to render nothing instead of `-` for money, and keep `-` for dates and quantities), `web/libs/shared/utils/src/lib/constants/format.const.ts` (comment only; `MONEY_DECIMALS = 2` stays), new `web/libs/shared/utils/src/lib/forms/decimals.validator.ts` (`maxDecimals(n)` + the message key `validation.maxDecimals` = "Enter an amount with at most 2 decimals."), `web/libs/containers/documents/src/lib/line-editor/line-editor.component.{html,ts}` (unit cost: `inputmode="decimal"`, `step="0.01"`, validator), `web/libs/containers/administration/src/lib/settings/settings.component.{html,ts}` (rule amount threshold: same), the catalogs (column headers and labels get "(RM)": unit cost, line cost, amount, avg cost, value, stock value, "Amount from"), plus an audit of every template that prints money (`inbox`, `document-list`, `document-editor`, `stock-list`, `reports`, `home`, `settings`) so none uses a raw number, `toFixed` or a `number` pipe.
- Size: 450.
- Depends on: R02 (same two form templates; merge R02 first to avoid a conflict in `line-editor.component.html` and `settings.component.html`).
- Acceptance: FR-05, TB-02. `12400` shows `12,400.00`, `119371.9` shows `119,371.90`, `0` shows `0.00`, one million shows `1,000,000.00` in en, zh and ms; no value is ever abbreviated; typing `12.345` or pasting `12.3456` in unit cost or amount-from shows the message and blocks Save/Submit; `10`, `10.5`, `10.50` are accepted; a quantity of `1.2345` still shows `1.2345`, `2.500` shows `2.5` (unchanged). ER-12 (edge retest, v2): one quantity rule that matches the API (minimum `0.0001`, at most 4 decimals): `0.0001` is accepted, `0.00001` says "Use at most 4 decimals.", and an invalid line is left out of the totals line. A value the user may not see is not rendered (no `0.00`, no `-`, no empty column) - unchanged and re-asserted in the tests.
- Tests: pipe spec table (the values above, in en, zh and ms; null renders nothing), validator spec (the visual file's true minus sign U+2212 for negative money is a polish item that is not part of this plan; negative money keeps today's sign), line-editor and settings specs (blocked save), extend `13-cost-visibility` (Clerk unit cost input rejects 3 decimals).
- Re-check: power (numbers reconcile with the Excel export), edge (input errors).

#### R05 Non-colour token foundation and icons

- Goal: land every non-colour token once, so later slices only consume them and the shared style files stop being merge hotspots.
- Files: `web/libs/shared/styles/src/variables/_shape.var.scss` (radius `md` 8px, `lg` 12px, `sheet` 16px; `ring` 2px and `edge` 3px border widths; `focus` stays an alias of `edge`; `shadow(1..3)` returning `var(--ims-shadow-n)`), `_font.var.scss` (`semibold` 600, `bold` alias), `_font-size.var.scss` (line heights per step), `_space.var.scss` (`space(1.5)`), `_layout.var.scss` (motion: `duration('hold')` 1500ms, `duration('loop')` 1400ms, `easing('standard'|'enter'|'exit'|'linear')`, `smooth` alias; `height('actionbar')` 64px; `width('palette')` 40rem; `z-index('palette')` 45; `height('touch')` becomes `var(--ims-control-height)`), `globals/_themes.scss` (shadow custom properties per theme; the density custom properties `--ims-control-height`, `--ims-control-pad-x`, `--ims-row-height`, `--ims-cell-pad-y`, `--ims-card-pad`, `--ims-stack-gap` with COMFORTABLE values equal to today, plus the complete compact block under `@media (min-width: 1024px) and (pointer: fine)` selected by `[data-density='compact']`; nothing sets that attribute until R12, so R12 never edits this file), `globals/_base.scss` (html `background: canvas`, `color-scheme: light dark`, body line height 1.5, tabular numerals rule; the reduce-motion block is NOT touched here, that is R07), `mixins/_a11y.mixin.scss` (`focus-ring` uses `ring`; new `numeric` mixin), `web/libs/shared/ui/src/lib/components/icon/*` (stroke fixed in screen pixels with `vector-effect: non-scaling-stroke`, 1.5px at 16, 1.75px at 20 and 24; sizes 16/20/24/40 only) and `icon.paths.ts` (add `monitor`, `command`, `density-comfortable`, `density-compact`, `lock`).
- Size: 660.
- Depends on: R01.
- Acceptance: comfortable layout is metrically unchanged where the tokens did not change (control height 48, tab bar 64, row 48 measured in the browser at 360 and 1280); radius and line height changes are the only visible differences; no horizontal scroll at 320 and 400% zoom on the screens of `10-phone`; every old function argument (`border-width('focus')`, `easing('smooth')`, `font-weight('bold')`) still compiles; FR-02 targets unchanged; tables' right-aligned cells and the Home stat values render with `font-variant-numeric: tabular-nums`; the five new icons render at 20px in a unit test of the icon registry.
- Motion (programme M, N16 to N19): the motion tokens (CSS properties and SCSS functions, Reduced and Off blocks, `shine`) so that R07 and R08 can use them in wave 3 (+60). Respect `data-motion`; tokens only.
- Tests: icon spec (every name in the paths file renders; size input accepts only the four sizes), Spectator class check for the numeric mixin users (table `--end` cell, stat-card value); e2e `10-phone` and `11-accessibility` unchanged and green.
- Re-check: novice (phone layout shift), edge (400% zoom).

#### R06 Preferences, theme per user, no flash, PWA colours

- Goal: UX S2 (DK-01..DK-10) and the theme half of V5. Introduces the per-user preference store that density and recents reuse.
- Decision: a new library `core-preferences` (`@ims/core/preferences`), registered in `web/tools/libs.mjs` and scaffolded with `node tools/scaffold-libs.mjs`. It holds `UserPreferenceStore` (typed get/set per signed-in user id under `ims.pref.<userId>.<key>`, every access in `try/catch`, in-memory fallback when storage is blocked, `storage` event for cross-tab updates, a `clear(userId, key)` used at sign-out). `ThemeService` (stays in `core-theme`) is rebuilt on it. Four consumers (theme, density, recents, and the existing list scope) justify a lib; the alternative (putting the store in `core-identity`) mixes auth with UI settings. The existing `libs/features/documents/src/lib/services/list-scope.preference.ts` migrates to the store in this slice only if it is a trivial swap; otherwise it stays and is listed in R24.
- Files: new `web/libs/core/preferences/**`, `web/libs/core/theme/src/lib/theme.service.ts` (+spec), `web/apps/main/src/index.html` (`<meta name="color-scheme" content="light dark">`, two `theme-color` tags with `media="(prefers-color-scheme: ...)"` set to the `surface` of each theme, the blocking `<script src="assets/theme-init.js">` before the stylesheet), new `web/libs/shared/assets/src/assets/theme-init.js` (about 400 bytes, reads the device copy in a `try`, sets `data-theme` and the theme-color tags), `web/apps/main/ngsw-config.json` (add `/assets/theme-init.js` to the prefetched `app` group; the existing glob `/*.js` does not match a file under `assets/`), `web/libs/shared/assets/src/manifest.webmanifest` (`theme_color` `#ffffff`, `background_color` `#f4f5f7`; the install splash stays light), `web/libs/core/navigation/src/lib/components/shell.component.{html,ts,spec.ts}` (the account dialog's theme group becomes "Appearance": Use device setting with the "Now: Dark" sub-line, Light, Dark; this is the only shell edit in R06), `web/scripts/check-contrast.mjs` (assert the two `theme-color` hex values in `index.html` and the manifest colours equal the palette `surface` and `canvas`), the catalogs (`shell.appearance`, `theme.system`, `theme.systemNow`, `theme.light`, `theme.dark`).
- Behaviour notes the builder must follow: device-level copy is written on every change; at sign-out and on the login page the device copy is `system`; after sign-in the user's own preference is applied; "system" mode sets no attribute (CSS decides, so the OS switch is live); changing the preference updates other tabs within a second.
- Size: 700.
- Depends on: R01 (the surface hex values in `index.html`).
- Acceptance: DK-01..DK-10 (UX 4.5). Security: `security-engineer` confirms in `docs/security/web-headers.md` that a same-origin classic script is allowed by `script-src 'self'` (it is; no CSP change) and that the production build keeps `inlineCritical: false`; the service-worker precache lists `theme-init.js`. First paint check (DK-03): Playwright with `colorScheme: 'light'` and `ims.theme=dark` in storage: the `html` background at `domcontentloaded` is the dark `canvas` `rgb(14, 18, 23)`.
- Tests: Jest for the store (per user keys, blocked storage, cross-tab event, clear), ThemeService (system/light/dark, sign-out reset, user switch), shell account dialog; e2e new `16-appearance` (DK-01, DK-02, DK-03, DK-08 with two pages in one context).
- Re-check: edge (forced colors DK-10, storage blocked DK-09), novice (dark on phone).

#### R07 Reduce-motion contract, spinner, skeleton primitives, delay helper

- Goal: the infinite-animation bug (the global reduce-motion rule leaves loops running) and the primitives the next slices need.
- Files: `web/libs/shared/styles/src/globals/_base.scss` (reduce-motion block: `animation: none` on the named loops, `animation-iteration-count: 1` and `transition-duration: 0.01ms` as the safety net; the two `!important` stay the only ones and keep their stylelint disable comment), `web/libs/shared/ui/src/lib/components/spinner/*` (rotation 0.8s linear; reduce-motion shows the `clock` icon and the word "Working" from `UI_TEXT`), `web/libs/shared/ui/src/lib/components/skeleton/*` (pulse 1.0 to 0.55, 1.4s; reduce-motion static 0.7; colour `surface-strong`; `aria-hidden` bars plus one sr-only "Loading"), new `web/libs/shared/utils/src/lib/reactive/delayed-loading.ts` (+spec) exporting `delayedLoading(loading: Signal<boolean>, { delay: 300, minShow: 400 }): Signal<boolean>` (true only after 300 ms of waiting, then at least 400 ms), `UI_TEXT.working` and the catalogs. Adoption of the helper on screens is R15.
- Size: 450.
- Depends on: R05 (motion tokens).
- Acceptance: FB-10 (primitives: no pulse, no rotation, "Working" visible, with `prefers-reduced-motion: reduce`); `delayedLoading` unit tests with fake timers: 200 ms answer never true, 350 ms answer true for >= 400 ms; no other `@keyframes` exists besides the three loops (grep).
- Motion (programme M, N16 to N19): shimmer replaces the pulse (6 sweeps then static), `ims-loop-*` names, the spinner exists only while busy, the reduce-motion block uses the token mapping; the acceptance text "no other keyframes besides the three loops" is replaced by MO-01..MO-09 (+100). Respect `data-motion`; tokens only.
- Tests: Jest (helper, spinner reduce-motion branch via a `matchMedia` stub); e2e new `17-reduced-motion` (`reducedMotion: 'reduce'`: computed `animation-name` is `none` on `app-skeleton` bars, the shell progress and the spinner).
- Re-check: edge.

#### R08 Buttons and form controls

- Goal: V6 (button variants and states, form-field states, control states) plus the button half of UX 7.2.
- Files: `web/libs/shared/ui/src/lib/components/button/*` (variants primary/secondary/quiet/danger and danger-outline; rest, hover, pressed, focus, disabled, busy, success; remove `transition: background-color`; no scale), `form-field/*.scss` (error text with `alert` icon, error border plus inset ring, hint, disabled and read-only look), `input/`, `select/`, `textarea/`, `checkbox/`, `radio-group/` (adds `variant="segmented"`), `search-input/` scss, `web/.stylelintrc.json` (`declaration-property-value-disallowed-list` so `transition`/`transition-property` may not name color, background, border, shadow, filter, width, height, margin, padding or `all`), the catalogs and `UI_TEXT` (`working`, `saved`).
- Behaviour (UX 7.2): a click on a busy button is ignored; it becomes `aria-disabled` and `aria-busy` at once; the spinner and a fixed width appear after 150 ms; the label stays; focus stays; success state input (`[successLabel]` or a `state` input, builder's choice) shows a check and "Saved" for 1.5 s then reverts; hover and pressed colours are instant.
- Size: 900.
- Depends on: R01, R02 (shares `form-field`), R05.
- Acceptance: FR-02 (44px phone, 48px primary), FR-08 (error has icon + text), button busy does not change colour (contrast unchanged), disabled button text 4.5:1, a reason-disabled button stays focusable (`aria-disabled`), no `transition` on colour or background remains in any SCSS (lint rule passes).
- Motion (programme M, N16 to N19): button hover overlay, press 0.97, three-layer label stack, busy, success morph and tick, shake token; checkbox, radio and switch micro-interactions; focus underline; the stylelint transition rule becomes the allow-list of the motion file 4.1 (supersedes "no pressed scale" and "no transition on colour") (+150). Respect `data-motion`; tokens only.
- Tests: Spectator on button (all states, double click sends once, busy keeps focus, success reverts after 1.5 s with fake timers), form-field error markup, radio-group segmented keyboard (arrows move, Space selects); contrast pairs for the button states are already in R01.
- Re-check: novice (tap targets, busy), edge (keyboard).

#### R09 Status chips, stat cards, tabs (v2: changed, was "Tables, lists, chips")

- Goal: the chip, stat-card and tabs half of V7. The table half (header look, sticky header, row states, sort affordance, progress line) moved into A02, which rewrites the table anyway; the pager look moved into A05. Styling a table that the next slices replace would be waste.
- Files: `web/libs/shared/ui/src/lib/components/status-chip/*` (status roles from the visual table, flags outlined, Low chip, count badge), `stat-card/*` (attention state: edge + icon + text), `tabs/*` (scss, plus ER-18: the Mine/All control becomes a segmented control with `aria-pressed` by reusing `app-radio-group variant="segmented"` from R08, or gets `aria-controls` and a `tabpanel`; builder's choice, default segmented), and the list-page templates only where a class needs the new chip names (`stock-list`, `document-list`, `low-stock`, `product-stock`).
- Size: 500.
- Depends on: R01, R05.
- Acceptance: status chips keep icon + text for all six statuses and three flags (FR-08, grayscale screenshot of Home, a list and a document); the stat-card attention state is not colour-only; ER-18 closed (the Mine/All control has no orphan `role=tab`); TB-06 unchanged.
- Motion (programme M, N16 to N19): sliding indicator for tabs and the segmented control (new dependency M02), badge bump, chip swap pop, stat-card lift (+200). Respect `data-motion`; tokens only.
- Tests: status-chip spec (icon + label for all statuses and flags), stat-card spec, tabs/segmented keyboard spec (arrows move, Space selects); screenshots at 360 and 1280 reviewed by the fe-designer.
- Re-check: power (lists), edge.

#### R10 Shell, tab bar, action bar

- Goal: V9. Keeps every existing class name (`shell-nav`, `shell-tabbar`, `shell-bar`) because the e2e page object finds them by class.
- Files: `web/libs/core/navigation/src/lib/components/shell.component.{html,scss,ts,spec.ts}` (nav active indicator, badges, tab bar five equal cells with the shape cue for the active tab, tab bar hidden while the on-screen keyboard is open using `visualViewport` under 75% of the window, `shell-progress` rewritten with `transform` 1.2 s, reduce-motion static bar), the action bar styles in `web/libs/containers/documents/src/lib/document-actions/*.scss` and `document-editor/*.scss` (styles only; no template or logic change, R14 owns those), `web/libs/shared/ui/src/lib/components/page-header/*.scss` if needed. No `--ims-bottom-offset` (ruling C6).
- Size: 520.
- Depends on: R05, RP00 (the role-name maps are replaced before the tab bar is rebuilt) (v2: no longer R06. R10 restyles the shell in wave 4 and R06 then edits the account dialog in wave 6; only the merge order matters).
- Acceptance: ER-09 (v2: the Account button's accessible name starts with its visible text, for example "Account, Supervisor"), FR-02 at 320 and 360 (five tab cells, each 64px, labels single line with full name in `aria-label`), FR-03 (no clipped controls at 320 and 400% zoom, `media-short` rule still makes bars flow), the action bar never hides the focused control, `aria-current="page"` kept, badge numbers still equal the lists (RA-08 existing checks).
- Motion (programme M, N16 to N19): tab bar pill and icon pop, sidebar indicator, markup for the top-bar progress trickle (the component is M03) (+120). Respect `data-motion`; tokens only.
- Tests: shell spec (tab bar entries per role unchanged, keyboard-open hiding with a `visualViewport` stub); e2e `10-phone` and `06-roles` unchanged and green.
- Re-check: novice, edge.

#### R11 One failure, one announcement

- Goal: UX S4 (banners and toasts): the double announcement of EA-15/EA-26, dialogs keep the typed reason (PS-01 residual), toast rules.
- Files: `web/libs/core/http/src/lib/interceptors/error.interceptor.ts` (+spec; a call that the screen handles inline, flagged through the existing `HttpContext` mechanism, does not toast), `web/libs/core/notification/src/lib/services/toast.service.ts` (+spec: dedupe the same text within 2 s, max 2 visible, a third success replaces the oldest success, errors never replaced), `components/toast-host.component.*` (phone position directly under the top bar, desktop top right under the header, pause the 4 s timer on hover and focus), `services/confirm.service.ts` and `components/confirm-host.component.*` plus `web/libs/shared/ui/src/lib/components/confirm-dialog/*` (the dialog stays open on failure, keeps the reason, shows the message inside above the buttons with `role="alert"` and moves focus to it), `web/libs/shared/ui/src/lib/components/banner/*` (alert vs status role by "result of an action" vs "present at load"), and the screens that today raise a toast AND an inline message: `document-editor`, `document-actions` (reverse, reject, cancel, post, approve dialogs), `inbox`, `product-edit`, `warehouses`, `partners`, `users`, `settings`.
- Size: 930.
- Depends on: R08 (banner actions use the new button).
- Acceptance: FB-06, FB-07, FB-08, FB-09 (pause, max two, dedupe, never over the action bar at 360), FB-13; ER-02 (v2, ruling C14: on desktop a toast never covers a page-header action), ER-11 (v2: the list error panel uses the same mapped sentence as the toast, and a form-level failure raises one alert, not banner plus toast); a business-rule violation produces exactly one announcement (inline alert, no toast with the same text); EA-15 and EA-26 re-test scenarios pass; a list load failure shows the table error panel only.
- Motion (programme M, N16 to N19): toast `animate.enter` / `animate.leave`, FLIP of the survivors, `inert` while leaving (+80). Respect `data-motion`; tokens only.
- Tests: toast service spec, confirm service spec (failure keeps the dialog open and the reason), error interceptor spec (flagged call does not toast), document-editor spec (reversal blocked: banner only), e2e new `18-feedback` (reverse blocked: dialog stays open with reason, one alert, count `getByRole('alert')` equals 1; two quick saves: one toast).
- Re-check: power (reverse, reject), edge (EA-15, EA-26), novice.

#### R12 Density (v2: changed; table, pager and filter-bar parts moved)

- Goal: UX S6 + V4 + the density half of V5: the service, the switch, and the places that are not the table. The table needs no work here: A02 (wave 5) reads `--ims-row-height` and the other density tokens from the start, and R05 already holds the compact token block.
- Files: new `web/libs/core/theme/src/lib/density.service.ts` (+spec; built on `UserPreferenceStore` from R06; values `comfortable` | `compact`; device copy `ims.density`; reset at sign-out), `web/libs/shared/assets/src/assets/theme-init.js` (small edit: also sets `data-density`), the account dialog in `shell.component.html` ("Density" radio group, segmented, desktop only), the document line grid in `web/libs/containers/documents/src/lib/line-editor/*.scss` (56px to 44px rows), the catalogs. Moved out: the table-toolbar "Density: Comfortable. Change." button and the pager density (A05, wave 9), the filter-bar gap (L02, from `--ims-stack-gap`).
- Size: 500.
- Depends on: R06, R10.
- Acceptance: DN-01..DN-07 (UX 5.4): at 1280 compact shows at least 30% more Stock rows in the same height (row 48 to 36 is 33%, measured on the A02 table); at 359 to 1023 the control is absent and cards are identical; at 400% zoom the layout is cards and the setting is ignored; the setting survives reload and sign-out/in of the same user and does not leak to another user; switching keeps focus and scroll; compact never clips text that comfortable does not clip; compact keeps 24px minimum targets with an 8px gap; default for every role is comfortable (Q2 accepted).
- Tests: density service spec (per user, blocked storage, reset at sign-out), account dialog spec (group only when the viewport matches; `matchMedia` stub), e2e new `19-density` (count rows in a fixed height, reload, other user, 400% zoom via viewport 320 CSS px).
- Re-check: power (long lists), edge (zoom).

#### R13 Overlays: dialog, sheet, toast, banner, card, empty state

- Goal: V8 and UX 7.6 motion for overlays.
- Files: `web/libs/shared/ui/src/lib/components/dialog/*` (native `<dialog>` look: `surface-raised`, `shadow(3)`, scrim `overlay` 50%/60%, no blur; bottom sheet with top radius 16 and handle bar; desktop fade 120 ms in, phone slide 200 ms in / 120 ms out, none under reduce-motion), `toast/*` and `web/libs/core/notification/src/lib/components/toast-host.component.scss` (edge colour, icon, error variant tint), `banner/*` (tint, edge, icon), `card/*`, `empty-state/*` (icon in a 64px sunken disc, variants first-use / filtered-none / role-limited with `lock` / error), `confirm-dialog` scss.
- Size: 850.
- Depends on: R07 (motion fallbacks), R11 (the toast and banner behaviour it styles).
- Acceptance: dialog text only on `surface-raised` (contrast rows already in R01), focus trap, Esc, focus return unchanged (existing specs green), sheet at 320px fits with two buttons wrapping, FB-12 (every animation present is in the UX 7.6 table; grep), DK-06 (banner, toast, scrim legible in dark), forced-colors keeps borders (`border: 1px solid transparent`, never `border: 0`), ER-20 (v2: a closed dialog has no dangling `aria-labelledby`; the title is always rendered or the attribute is set on open).
- Motion (programme M, N16 to N19): dialog, sheet and drawer-ready CSS (`@starting-style`), scrim, sheet swipe handle, banner disclosure, empty-state entrance (new dependency M01); the acceptance "every animation present is in the UX 7.6 table" is replaced by MO-09 (+250). Respect `data-motion`; tokens only.
- Tests: existing dialog/confirm/toast specs stay green; add empty-state variants spec; screenshots light and dark at 320/1280 reviewed by fe-designer.
- Re-check: edge (DK-10), novice (sheets), power.

#### R14 Saved state, row highlight, live regions, error summary

- Goal: the rest of UX S4: button "Saved" in place, NV-04 highlight, announcements, and the "{n} things to fix" banner.
- Files: the Save buttons and their handlers in `document-editor`, `product-edit`, `settings`, `users`, `warehouses`, `partners` (wire the R08 success state for actions that stay on the same screen; Submit/Post/Approve/Reject/Reverse/Cancel never use it; when the Saved state shows, the "Draft saved." toast is not shown, a polite live region says it once), `line-editor/*` and the Add item sheet (changed line gets `accent-soft` for 1.5 s with the UX 7.6 timings; reduce-motion applies and removes the tint with no fade; message "DEMO-001: now 2." stays), new `web/libs/shared/ui/src/lib/components/error-summary/*` (`role="alert"` banner "{n} things to fix", each item links to its field; used by the document editor and the larger forms), "Last saved" line on the draft editor, the catalogs.
- Size: 780.
- Depends on: R08, R11.
- Acceptance: FB-04, FB-05, FB-06, FB-11 (no optimistic server write: delay the API 2 s, status and totals change only after), NV-04, one live announcement per save; the error summary appears only with two or more invalid fields and focus goes to it then to the first invalid field on next Tab. Edge retest (v2): ER-03 (focus stays on the invoking button after Save draft, and goes to the banner after a stale save), ER-04 (an empty submit on the product, partner and user forms moves focus to the first invalid field; a 409 duplicate SKU, barcode or partner name is shown on its field with `aria-invalid`), ER-13 (built in R27, which runs first; R14 only re-tests that three clicks in one tick create one draft), "Keep editing" in the Discard dialog returns focus to the control that had it (EA-07), ER-19 (a Large document shows "A Large document is approved by the Owner." next to the actions).
- Motion (programme M, N16 to N19): row highlight variants (user edit and data update), error-summary disclosure (+80). Respect `data-motion`; tokens only.
- Tests: Spectator for each Save flow (Saved shown, toast absent), line-editor highlight timing (fake timers), error-summary links; extend e2e `01-receive` (Save draft shows "Saved", no toast, one live message) and add `18-feedback` cases.
- Re-check: novice (NV-04, NV-21), edge.

#### R15 Skeleton shapes per screen, loading rules (v2: changed; the table skeleton is built in A02)

- Goal: UX 7.1 end to end: shapes per screen type, 300 ms / 400 ms timing, refetch rule, and no layout shift. Also FR-07 for skeletons.
- Files: `web/libs/shared/ui/src/lib/components/skeleton/*` (shape primitives: line, title, chip, block, row, card row, circle), `table/*` (only the binding of the table's own skeleton to `delayedLoading` and the refetch rule: old rows at 60% with the thin line; the skeleton markup itself, a real header and one bar per visible column, is A02), the screens: `document-editor` (header blocks + 3 lines, sticky bar real and disabled), `document-list`, `inbox`, `stock-list`, `low-stock`, `product-stock` (also ER-05, v2: the page reads `paramMap` so moving from `/stock/1` to `/stock/3` reloads the product and its ledger), `product-edit`, `settings`, `reports`, `audit-log`; card list skeleton for the phone; pager hidden until total is known.
- Size: 800.
- Depends on: R07, R09, R12, R13, A02.
- Acceptance: FB-01 (200 ms answer never shows a skeleton; 350 ms shows one for >= 400 ms), FB-02 (CLS 0 when content replaces it, measured in e2e with a `PerformanceObserver` for `layout-shift`), FB-03 (a skeleton for Staff has no cost or value column; for Clerk no valuation column), a refetch never replaces rows with a skeleton, pager hidden while loading; `aria-busy` on the region, one "Loading" status; ER-05 closed.
- Motion (programme M, N16 to N19): skeleton to content crossfade and reveal on every screen, no entrance on refetch (+150). Respect `data-motion`; tokens only.
- Tests: table spec (columns hidden by `columnWhen` have no skeleton bar), per-screen spec for one skeleton shape, e2e new `17-reduced-motion` cases plus a CLS check in `18-feedback` (throttle the API through `page.route`); Q12 later turns the CLS check into a permanent budget.
- Re-check: novice (phone cards), power (tables), edge.

#### R16 Command palette component and `app-kbd`

- Goal: V10 part (a): the presentational component with fake data and its look; no identity, no router, no API.
- Files: new `web/libs/shared/ui/src/lib/components/command-palette/*` (`app-command-palette`: inputs for groups, options, active index, query, states; outputs for query change, run, run-secondary, close; ARIA 1.2 combobox + listbox with `aria-activedescendant`, DOM focus stays in the field; input row 56px, group labels, result rows with `--ims-row-height`, active row `accent-soft` with an edge and a return hint, matched characters weight 600, footer hints, empty state, group error row with Retry, offline line; full-height sheet below 600px) and `kbd/*` (`app-kbd`), uses `app-dialog`; `web/libs/shared/ui/src/index.ts` exports; the catalogs for the UI words that live in the kit through `UI_TEXT`.
- Size: 500.
- Depends on: R05 (kbd, tokens), R13 (dialog look).
- Acceptance: CP-07 at component level (arrows move the active option without moving DOM focus, `aria-activedescendant` updates, Enter emits run, Esc closes and focus returns to the opener), accessible names "{primary}, {secondary}, {group}" (CP-12 names), option rows >= 44px on touch and 48 in the phone sheet (CP-14), no horizontal scroll at 320.
- Tests: Spectator on the component with fake groups (keyboard map from UX 6.4 including Home/End only at the caret ends, roles, names and live region), `app-kbd` spec. No demo route is added to the app; the axe scan of the palette happens in R18's e2e.
- Re-check: none yet (not reachable in the app).

#### R17 Role-aware Home: Doers (Staff, Clerk)

- Goal: UX S5 (RA-01..RA-12 for Doers). Replaces the stat-tile Home for the Doer profile; the Reviewer profile keeps today's tiles until R19.
- Files: `web/libs/containers/home/src/lib/home/*` (profile chosen from permissions through a pure `homeProfileOf(has)`: Doer = `document.create` and not `document.approve`, Reviewer, Overseer, with unit tests including a custom permission set), new `web/libs/containers/home/src/lib/widgets/*` (widget frame with skeleton / error row with Retry / empty line; W1 greeting and scope line, W2 quick actions, W3 Returned to me, W4 My drafts, W5 Waiting for approval - mine, W7 Low stock, W11 Recently posted by me collapsed on phone), `web/libs/features/documents/src/lib/services/home-feed.store.ts` (one `mine=true` list call, page size 25, split client-side into returned drafts, drafts, submitted, posted), `web/libs/features/stock` (low-stock first 3 or 5 rows through the existing store), `stat-card` untouched, the catalogs, `e2e/src/pages/screens.ts` (Home page object).
- Data and call budget (RA-09): Doer Home makes 3 calls: `GET /api/documents/counts`, one `GET /api/documents?mine=true&pageSize=25`, `GET /api/stock/low-stock?pageSize=5` (its `total` is the count, so `/api/stock/summary` is not needed for a Doer). Headings use the counts as truth; rows are the newest 25 of the user's documents. A returned draft older than the newest 25 shows in the count but not in the rows; "See all" opens the Receipts or Issues list by the first row's type (accepted, UX Q5/Q6); the Returned chip alone is shown, no reason text (UX Q6). Backend improvements are optional (section 3).
- Cost rule (D5): Staff - no amount, no `0.00`, no word "value" in any widget, `aria-label` or `title`; Clerk - amounts only in W4 and W5; nobody on this profile sees a valuation.
- Size: 900.
- Depends on: R04, R07, G02 (the Home rows carry ids), client batch CS4 (B1 to B3, with fallbacks). v2: it was in the fast path in wave 4; it now follows the id merge (roadmap conflict C3). The widget frame uses the R07 skeleton primitives and `delayedLoading`; per-screen skeleton shapes stay in R15.
- Acceptance: RA-01 (Doer order: W1, W2, W3, W5, W4, W7, W11; DOM order equals visual order, no CSS `order`), RA-02, RA-03, RA-05 (360x640: Receive and Issue visible without scrolling and >= 56px high; first Low stock row one scroll away), RA-06 (fail one request, the others stay), RA-07 (rows open the document; Tab reaches each row, Enter opens), RA-09 (3 calls), RA-10, RA-11 (no warehouse empty state kept), RA-12; W3/W4/W5 hidden at zero; W7 rows offer Receive for `document.create`.
- Tests: home spec per profile and per role (Staff, Clerk), no-money scan of the rendered DOM for Staff (regex on money-shaped text and on `aria-label`/`title`), failure isolation spec; e2e new `20-home-doer` and update `06-roles` and `13-cost-visibility` (Home steps use the new page object).
- Re-check: novice (primary), edge (FR-07, keyboard), power (Clerk view if time).

#### R18 Command palette: logic, trigger, recents, actions

- Goal: UX S8 (CP-01..CP-03, CP-06, CP-07, CP-10, CP-11, CP-13, CP-14) without the document/product search.
- Files: new `web/libs/core/navigation/src/lib/palette/*` (`PaletteService`: open state, Ctrl+K and Cmd+K from anywhere with `preventDefault`, restores focus to the opener; `PaletteCommands`: Go to built from `MENU_ITEMS` filtered by permission, Actions: New receipt and New issue (`document.create`), Appearance (3), Density (2, only >= 1024px), Language (3), Show keyboard shortcuts (added in R22, omitted until then), Sign out; ranking and matching (label prefix, word prefix, substring; case and diacritics ignored; English names searchable in zh/ms; query capped at 100 characters); `RecentsStore` on `UserPreferenceStore` (last 8 documents and products, cleared at sign-out); the palette never offers Post, Approve, Reject, Cancel, Reverse, Submit, delete, import or export), `shell.component.{html,ts}` (trigger button: a real `<button>`, `aria-haspopup="dialog"`, `aria-keyshortcuts="Control+K Meta+K"`, name "Search or run a command, Control K"; 240x40 with the key hint at >= 1024px, 44px icon button from 600 to 1023px; the palette is mounted with `@defer` so its code is a lazy chunk), `web/libs/containers/documents` and `stock` product pages call `RecentsStore.add` when a document or product page opens, the catalogs, `e2e/src/pages/shell.ts`.
- IMPORTANT e2e collision: `Shell.account` is `page.getByRole('banner').getByRole('button')` ("the only button in the top bar"). The trigger adds a second button there, so this PR MUST change the locator in the same PR (use `getByRole('button', { name: <account label> })`, and keep language-independence the way `09-language` needs) and run all 13 existing scenarios.
- Phone (< 600px): the icon button is R20, so R18 shows no trigger on phones (UX 6.3, Q3 accepted but built after the desktop palette).
- v2 note: A18 (notifications bell) and R20 (phone search button) add more buttons to the top bar later; the locator written here (`getByRole('button', { name })`) must not depend on the count of buttons in the banner, so those slices need no page-object change.
- Size: 750.
- Depends on: R06, R10, R12 (density actions), R16.
- Acceptance: CP-01 (Ctrl+K opens and closes from Home, a list, the editor and inside a text field; the browser omnibox does not open), CP-02 (trigger visible >= 1024px with hint; opens with Enter and Space), CP-03 (Staff: no Approvals, Reports, Users, Settings, Audit; typing "settings" returns no screen; Admin gets Settings), CP-06 (no amount for any role), CP-07, CP-10 (sign-out clears Recents; the next user sees none), CP-11, CP-13 (opening in a dirty editor does not show "Discard changes?"; navigating away through a result does), CP-14 at component level; bundle: the palette code is in a lazy chunk <= 40 kB raw and the initial total grows by <= 6 kB.
- Tests: palette service/commands specs per role (Staff, Clerk, Supervisor, Owner, Admin command lists as in UX 6.9), recents spec, shell spec (trigger, Ctrl+K), e2e new `22-palette` (keyboard path, role lists, recents cleared) and run the whole suite for the locator change.
- Re-check: power (primary), edge (keyboard, screen reader CP-12), novice (no change expected on phone).

#### R19 Role-aware Home: Reviewer, Overseer, Admin

- Goal: UX S7. Completes RA-01 for all five roles.
- Files: `web/libs/containers/home/src/lib/home/*` and `widgets/*` (W6 Needs my approval - oldest first, filtered by `canAct`, count and rows, "All clear: nothing waiting for you." at zero, amount only with `cost.view`; W8 Posted today; W9 Stock value (`valuation.view`, exactly 2 decimals) and Products with stock; W10 Recent movements (signed Stock effect, Reversal badge, no money); W12 Rules in force (`settings.manage`, from `SystemSettingsStore`); W13 Go to; two-column grid from 1024px, content max 76rem), `web/libs/features/documents/.../home-feed.store.ts` (approvals preview `status=Submitted&requiresApproval=true&pageSize=50`, sorted oldest first in the client), `web/libs/features/reports` (movements first 5), `stat-card`, the catalogs, `e2e/src/pages/screens.ts`.
- Call budget (RA-09): Reviewer 6 calls (counts, summary, approvals, low-stock, mine, movements); Admin 7 with settings (cached); Owner fewer. v2: the chart widgets of A20 load with `@defer (on viewport)` and are outside this first-load budget (at most 3 more calls once scrolled into view). The oldest-first order is client-side on up to 50 rows; if more than 50 are waiting the true oldest could be off the page (backend note B2).
- Size: 900.
- Depends on: R17.
- Acceptance: RA-01 for Supervisor, Owner and Admin in the exact order of UX 2.4, RA-04 (Owner: no Receive/Issue, no Posted today), RA-08 (W6 and W7 headings equal the sidebar badge and the list total), RA-09 (<= 7 calls), RA-10, FR-05 (Stock value reads `119,371.90`, never `119k`), D5 (Clerk Home has no valuation; Staff none), W6/W7 stay and say "All clear" at zero while W3/W4 hide at zero.
- Tests: home spec for Supervisor, Owner, Admin and a custom permission set (RA-02), call-count spec, e2e new `21-home-reviewer`, update `13-cost-visibility` (Supervisor tile) and `03-approval-override` if it asserted the old Home tile.
- Re-check: power (primary), edge.

#### R20 Palette search and phone button

- Goal: UX S9. Documents and Products groups, states, and the phone search button (Q3 accepted).
- Files: `web/libs/core/navigation/src/lib/palette/*` (Documents group `GET /api/documents?search={q}&pageSize=5`, visible with `stock.read` and query >= 2 characters; Products group `GET /api/products?search={q}&active=true&pageSize=5` with `masterdata.read`; 300 ms debounce through `debouncedSignal`; ranking rules for document-number-looking queries and exact SKU/barcode which pre-selects the product and opens its stock page; Ctrl+Enter opens the product record for `masterdata.manage`; "Refine your search to see more."; per-group loading skeleton after 300 ms, per-group failure row with Retry and no toast, 403 omits the group, offline shows only local groups plus the offline line, 429 shows the server sentence; live region "{count} results" debounced 500 ms), the top bar icon button below 600px and the full-screen sheet (48px field with autofocus via `data-autofocus`, close button, tab bar hidden while open, Android back closes the sheet first), the catalogs.
- Size: 700.
- Depends on: R18.
- Acceptance: CP-04 (typing `RCV-2610-0007` lists that document first, Enter opens it, request debounced 300 ms and capped at 100 chars), CP-05, CP-08 (Documents call fails: one retry row, other groups and no toast), CP-09 (offline), CP-12, CP-14 on a 320px phone and at 400% zoom, FR-07 (palette rows carry no money for any role, results for Staff come only from their own warehouses because the server scopes them).
- Tests: palette search spec with a mocked ApiClient (ranking, debounce, failure, 403, offline), phone sheet spec, e2e extend `22-palette` and `10-phone` (open the sheet, find a document by number in one tap sequence).
- Re-check: power, novice (phone find-by-number), edge (CP-12 screen reader).

#### R21 Paging: First/Last and page size (v2: REMOVED, merged into A05)

Same scope, now A05 (paging toolbar), which adds the density button and the 10,000-row offset limit. The id R21 is not reused.

#### R22 Keyboard shortcuts (v2: changed; table arrow keys are now A04)

- Goal: UX S10 (CP-15, CP-16, CP-17), Could priority.
- Files: new `web/libs/core/navigation/src/lib/shortcuts/*` (`ShortcutsService`: `/` focuses the screen search box, `?` opens the dialog, only when focus is not in an input, textarea, select or contenteditable and the "Single-key shortcuts" switch is On; Ctrl+S saves where a Save button exists, Ctrl+Enter triggers the primary action but never skips the Post confirm dialog), the "Keyboard shortcuts" dialog with the On/Off switch (default On at >= 1024px, per user; the dialog also lists the grid keys of A04 and the lines-grid keys of A14 when they exist), palette action "Show keyboard shortcuts", the document editor handlers in `web/libs/containers/documents/src/lib/document-editor/*`, tooltips with key hints, the catalogs. Removed from v1: row arrow navigation in `table/*` (A04 owns the full grid keyboard model).
- Risk handled here: barcode scanners type like a keyboard and a barcode may contain `/`; the single-key shortcuts therefore never fire while an input is focused, are off below 1024px, and the switch exists. The scanner path (`07-scan`) must stay green.
- Size: 500.
- Depends on: R18, A04.
- Acceptance: CP-15, CP-16, CP-17, `/` does nothing inside a field, `?` opens the dialog, the switch turns both off and is remembered per user, shortcuts are never the only way to do anything.
- Tests: shortcuts service spec (field guard, switch, Ctrl+S with and without a Save button), e2e new `23-shortcuts` and rerun `07-scan`.
- Re-check: edge (primary), power.

#### R23 Date field `dd MMM yyyy`

- Goal: UX 10.4 (NV-27, PS-15, accepted): a text date field in the app format with a calendar button.
- Files: new `web/libs/shared/ui/src/lib/components/date-field/*` (`app-date-field`: typed text `dd MMM yyyy`, also accepts a pick from the native picker through a button, emits an ISO date string `yyyy-MM-dd` (the Kuala Lumpur calendar day, same as today's `from`/`to`), validation message "Enter a date such as 09 Oct 2026."), `web/libs/shared/utils/src/lib/dates/*` (parse/format helper with a month table for en, zh, ms), adoption in the filter bars of `reports`, `document-list`, `audit-log`, and any other `type="date"` input (grep `type="date"`), the Reports empty state action "Change filters" (S11), the catalogs.
- Size: 600.
- Depends on: R03 (range validator and message), R08 (form-field).
- Acceptance: the Reports, Receipts, Issues and Audit date filters accept `09 Oct 2026` and reject `31 Feb 2026`; From after To shows "From must be on or before To." (R03 behaviour kept); the field shows the same format in en, zh and ms; keyboard and screen reader: label, hint of the format, error linked with `aria-describedby`; no mm/dd/yyyy anywhere; ER-17 (v2: the focus ring stays visible on the calendar button, `:focus-within` on the field wrapper).
- Tests: parse/format spec (leap day, month names per locale, trailing spaces), date-field spec, reports and document-list filter specs, e2e `24-paging-dates` and `15-reports-range` updated for the new field.
- Re-check: novice (NV-27), power (PS-15), edge.

#### R24 Clean-up

- Goal: V11. Remove the compatibility layer once nothing uses it.
- Files: every `*.scss` that still uses an old name (about 54 `color()` call sites and 21 uses of `border-width('focus')`, `easing('smooth')`, `font-weight('bold')`; counted today), `web/libs/shared/styles/src/variables/_palette.var.scss` (delete `$aliases`), `_color.var.scss`, `_shape.var.scss`, `_font.var.scss`, `_layout.var.scss` (delete the old aliases), `web/.stylelintrc.json` (disallow the old names), `web/docs/conventions/scss-design.md`, `docs/architecture/frontend.md` (theme row, `core-preferences`, palette and shortcuts, call budgets), `web/README.md` (preferences, theme-init), any leftover per-user preference helper (`list-scope.preference.ts` if not migrated).
- Size: 300.
- Depends on: all other slices (R, A, Q, L) merged and no other branch touching `.scss`. v2 additions to its file list: the legacy allow-list of Q07 (`web/.stylelint-legacy.txt`) must be empty or deleted, `web/docs/conventions/responsive.md` and the gallery notes are final, and `docs/architecture/frontend.md` gets the new sections listed in section 1.6.
- Acceptance: removing the alias map makes `color('primary')` throw at compile time, so a clean build proves nothing was missed; contrast and stylelint green; visual diff zero (the rename is mechanical); docs describe the final token names.
- Tests: compile is the test; full e2e suite; one last screenshot pass light/dark at 320/1280.
- Re-check: edge (final accessibility pass), power, novice (smoke).

#### R25 Sign-in page redesign (v2: new, from the login specs)

- Goal: the page every user sees first. Scope: sections 1 to 6 of `docs/design/2026-refresh-login.md` and the whole of `docs/design/2026-refresh-login-visual.md`: split page from 1024px (brand panel + form), one column below, `h1` "Sign in", logo lockup from the live `app.name`, language and appearance as two header menus, one banner above the button, lockout and rate-limit texts built on the client from `retryAfterMinutes` / `retryAfterSeconds`, version footer, no remembered username.
- Files: `web/libs/containers/authentication/src/lib/login/*` (layout, form, banner, password show/hide and caps-lock hint, footer), new `web/libs/shared/ui/src/lib/components/logo/*` (`app-logo`: inline mark in token colours, decorative, plus live text), `components/menu/*` (`app-menu`: a button that opens an `app-popover` with `menuitemradio` items, a sheet on phone; used for Language and Appearance here and reusable for the account and table menus), `web/libs/core/identity` (`lastUsername` held in memory after a session expiry only; the login error kept as data, `{ kind, minutes | seconds }`, not as a sentence; the login call flagged with the R11 "handled inline" context so it raises no toast), `web/libs/core/theme` (a theme or language chosen on the sign-in page is applied from the device copy, ruling C15), the brand aliases (`brand`, `on-brand`, `brand-disc`, `on-brand-disc`, `brand-pattern`) appended to `_palette.var.scss` with their pairs and an SVG-hex assertion in `web/scripts/check-contrast.mjs` (same PR), two entries appended to the `$widths` map in `_layout.var.scss` (`auth-form` 28rem, `auth-panel-min` 22rem; the only R05 file this slice touches), `APP_VERSION` as a build-time `define` in `web/apps/main/project.json`, brand assets (`favicon.svg` := the adaptive mark, `icons/*` PWA PNGs regenerated with the maskable scale of the visual file; the SVGs are drawn by fe-designer in `docs/design/assets/`), the catalogs (the copy of login spec section 3 in en, zh, ms), `e2e/tests/25-login.spec.ts` and the `LoginPage` page object, `08-session` and `11-accessibility` updated. `viewport-fit=cover` and `interactive-widget` in `index.html` come from L03, which R25 does not need.
- API: B18 (administrator unlock of a locked account) for the second line of the lockout banner; without it the banner shows the first line only (when the user may try again), as the login spec says.
- Size: 1,000 (split into R25a layout, logo, brand and R25b behaviour, menus, copy when it passes 1,000 during the build). Depends on: R01, R02, R05, R06, R08, R11, A01.
- Acceptance: LG-01..LG-48 of the login spec (7.1 to 7.4) pass; the R02 rules (D3, D4) stay true; ER-11 (a failed sign-in or a 429 raises one alert, not banner plus toast) and ER-16 (`h1` and tab title say "Sign in"); no username, password or token is written to web storage (storage dump in the e2e, the security-engineer confirms); the theme chosen on the page becomes the next user's preference (C15); initial bundle growth <= 6 kB (the logo and menu are small, the brand panel is CSS); contrast for every pair of the visual file in light and dark; at 320, 400% zoom and in forced colours the page has no horizontal scroll and keeps its borders.
- Tests: login spec (the four D3 rules plus lockout/429 text per language from numbers), `app-logo` and `app-menu` specs (keyboard: arrows, Esc, focus return, `menuitemradio` state), `APP_VERSION` spec, e2e `25-login` (case list of login spec 7.5), existing `08-session` and `11-accessibility` pass.
- TODO (minor finding of the R02 review): after a successful sign-in, the page stays enabled while the route loads, so a second Enter or click can start a second sign-in; keep the busy state (readonly fields, ignored submit) until navigation has finished (login spec 4.9 item 5). R02 only covers the time the request runs.
- Re-check: novice (primary: sign in on a phone, gloves-size targets), edge (screen-reader script of login spec 6.3, forced colours), power.

#### R26 Re-sign-in in place (ER-01) (v2: new, from login spec 4.11)

- Goal: when the session ends while a document form has unsaved work, the user signs in again in place and loses nothing. Closes the Major finding ER-01.
- Files: `web/libs/containers/documents` (the unsaved-changes guard and `document-editor`: when the session has ended the "Discard changes?" dialog is not shown, no navigation happens), `web/libs/core/identity` (`AuthService` exposes `$sessionEnded` with the reason and keeps `lastUsername`), new `app-reauth-dialog` in `containers/authentication` (title "Your session expired", text "Sign in again to keep your work.", the same two fields as R25 with the username prefilled and the password focused, the same banner rules, a Sign in button, and a quiet "Leave without saving" that asks for confirmation before it discards), replay of the failed request once for the same user; a different user discards the draft without showing it and goes to their Home; the clean-screen path (login spec 4.9): redirect with `reason=expired` and the sentence "Your session expired. Sign in again." on the sign-in page, the catalogs, e2e `26-resign-in`.
- API: none.
- Size: 450. Depends on: R25, R14, R11.
- Acceptance: with a dirty receipt and a 401 on refresh (the e2e makes the refresh call fail) the user sees the dialog, signs in, keeps all lines, presses Save once and the document saves; the old "Discard changes?" loop cannot be reproduced; focus returns to the control that had it; nothing is written to `localStorage` or `sessionStorage` (cost data would remain on a shared device); a different user cannot see the previous user's lines; one `role=alert` per failure.
- Tests: dialog spec (states, banner rules, focus), guard spec (session ended vs normal navigation), identity spec; e2e `26-resign-in` (same user, different user, wrong password, leave without saving).
- Re-check: edge (ER-01 primary), power, novice.

#### R27 QA round 3 (a): quantity entry and double-save safety (v2: new, from the novice and edge retests)

- Goal: remove the two ways a hurried worker posts ten times too much or creates two saves. Highest priority of the retest: NR-01 is Major.
- Files: `web/libs/shared/ui/src/lib/components/input/*` (new input `selectOnFocus`, default true for `type="number"` and `inputmode="decimal"`: the whole value is selected on focus and on tap, so typing replaces it; `select()` is called again after the focus event of a programmatic `focus()`), `web/libs/containers/documents/src/lib/line-editor/*` (quantity and unit-cost fields use it; NR-08: the field value is not rewritten while the user is typing, only on blur, and the caret is kept, so Backspace works after a decimal point; NR-09: the remove-line dialog says "Its quantity will be lost." and adds "and cost" only for users with `cost.view`; NR-16 part: the footer total ignores a quantity above the maximum as it ignores 0 and negatives), the Low stock "New receipt" shortcut path in `document-editor` (the quantity field is focused and selected, NR-01 case B), `document-editor.component.ts` and `document-actions` (NR-07 / ER-13: every command is guarded by one in-flight flag set synchronously in the click handler and by `exhaustMap`, so a second tap in the same tick or 60 to 150 ms later sends nothing; no second "Draft saved." toast and no false 409 "Someone else changed this document"), the catalogs.
- API: none.
- Size: 450. Depends on: R04, R08.
- Acceptance: R27.1 NR-01: tap the quantity field with "1" in it and type 12 gives 12 (also 50 on the Low stock shortcut, and "3" then "12" gives 12), on a real phone keyboard (qa-novice). R27.2 NR-08: type 1.5 then Backspace three times leaves an empty field; "2." then "5" gives 2.5. R27.3 NR-07 and ER-13: two taps 60 ms apart, and three clicks in one tick, on Save draft of an existing draft send exactly one PUT and show one toast and no stale banner; on a new draft exactly one POST. R27.4 NR-09: a Staff user never sees the word "cost" in the remove dialog. R27.5 no regression of `07-scan` and `02-issue-shortfall`.
- Tests: input spec (`selectOnFocus`, caret, blur write-back), line-editor spec (typing sequence with fake key events), editor spec (double save: same tick, 60 ms apart, with fake timers), remove-dialog spec per permission; e2e `01-receive` extended (quantity replace, double tap Save).
- Re-check: novice (primary, on a real phone if available), edge (ER-13), power.

#### R28 Document corrections (a): recall, Edit button, Post dialog (v2: replaces the NR-02 placeholder; source `docs/design/document-corrections.md`, decided)

- Goal: a mistake seen before posting is fixed by going back to Draft, never by Cancel and retyping. Closes NR-02 (Major). New command **recall** (button label "Edit"): the submitter takes their own Submitted document back to Draft, with the same document number and the same lines.
- Files: `libs/containers/documents/src/lib/document-actions/*` (the `recall` action from `allowedActions` only; the Edit button is primary on a document waiting for approval and secondary next to Post otherwise; "Cancel document" stays under "..."), `document-editor` (after a recall the form reloads with the new concurrency token and opens as a Draft), the Post dialog (buttons become [Edit items] [Post]; the label "Back" only when the reader cannot recall; Esc, outside tap and system Back leave a Submitted page that has Edit next to Post, so no read-only dead end exists), confirmation rule (ask only when someone else's work is affected: no dialog for an approval-free document, one short dialog for a document waiting for approval), toast "Back to draft.", hints for a creator who cannot recall ("ask {name}"), the catalogs (en, zh, ms), component tests.
- API: B21 (DC-BE1: domain `Recall`, policy `MayRecall`, `allowedActions`, audit `document.recalled`) and B22 (DC-BE2: `POST /api/documents/{id}/recall`, idempotent, `If-Match`). The client is regenerated by the batch rule of 2.4.
- Size: 750. Depends on: R11, R27, B22 (earliest wave 5).
- Acceptance: the submitter of a wrong-quantity receipt taps Edit items in the Post dialog and lands in the editor on the same document with the same lines; a repeated tap sends one request (R27 lock); Edit is absent when `allowedActions` lacks `recall`; Staff and Clerk see no cost; one `role=alert` per failure; focus returns to the control that had it.
- Tests: action visibility per status and role from `allowedActions`, Post dialog button rules, recall flow with a mocked client; e2e in R32. Re-check: novice (primary), power, edge.

#### R29 QA round 3 (c): phone document screens (v2: new, needs a ruling from ui-ux-designer first)

- Goal: the phone views of a document use the screen well. The designer rules on NR-03, NR-04, NR-12, NR-16 (routing section of the novice retest) before the build; this slice builds the ruling.
- Files: `web/libs/containers/documents/src/lib/document-editor/*` and `document-actions/*` (NR-03: the saved-draft action bar is one row at 360: the "More actions" button becomes an icon button with a visible label in the summary line, Save draft and Submit & post share the row; this refines R10's styles and must not change the class names; NR-04: on a phone the Items tab comes first, empty Supplier and Remark rows are collapsed, people and times are one line; NR-12: a Posted document shows "New receipt" / "New issue" and "Back to list" for roles with `document.create`), `line-editor/*` (NR-16: outlined steppers, the unit aligned with the field, a two-row line card with name and quantity on one row and Remove as an icon with an accessible name, so five lines are about 600 px not 1,150), the catalogs.
- API: none.
- Size: 700. Depends on: R10, R14, R27, R28 (Edit is the primary bar button of a waiting document) and the designer's ruling. NR-11 moved to R31.
- Acceptance: R29.1 at 360 the action bar of a draft is one row of at most 64 px plus the tab bar (measured; was 153 px). R29.2 a Submitted document shows its items without scrolling past header rows. R29.3 the E5 banner no longer contradicts the page (one message, one next step). R29.4 FR-02 targets stay >= 44 px (48 for primary controls). R29.5 the desktop lines grid (A14) is not affected: below the `roomy` band the card editor keeps this layout.
- Tests: editor spec per state (draft, submitted, posted, E5), screenshots at 360 and 320 reviewed by fe-designer; `10-phone` and `13-cost-visibility` stay green.
- Re-check: novice (primary), edge (320, 400% zoom).

#### R30 QA round 3 (d): phone lists, details and wording (v2: new)

- Goal: the remaining frontend-owned findings of the novice retest, grouped so they land in one table-lane slice after the table work.
- Files: `low-stock` (NR-05: the phone card draws "Short by 99" with its label and keeps one structure for every row), `product-picker` / Add item sheet (NR-13: "Showing the first 8. Keep typing." and, on a miss, "Not found? Ask a supervisor to add it."; refines A13), `document-list` (NR-14: placeholder "Number or partner" or a full-width search box), link targets of 44 px (NR-15: the "IMS" brand link, product-ledger document links, Products SKU links), `product-stock` (NR-17: the same Low chip as the list; ledger rows as two lines, document and change, date and type), the route announcer (NR-18: the live region follows the title when a new document is saved), the Back button with the Add item sheet open and a dirty form (NV-25: Back closes the sheet first, the "Discard changes?" dialog never stacks on it; the sheet pushes a history state), the catalogs (NR-18 wording: the ms/zh words for Receive and Issue are made consistent between the tab and the Home button; the texts stay `TODO-review` for the native reviewer, decision NV-28).
- API: none.
- Size: 600. Depends on: A13, R15, A02 and the designer's ruling for NR-05, NR-13, NR-17.
- Acceptance: R30.1 NR-05, NR-13, NR-14, NR-17 as described; R30.2 NR-15: every link target measured at 360 is >= 44 px high or inside a >= 44 px row; R30.4 NV-25: Esc and Back close the sheet before anything else; R30.5 NR-18: after Save draft the announcer text equals the new title.
- Tests: component specs for each change, the long-value fixtures of L00, e2e `10-phone` extended.
- Re-check: novice (primary), edge.

#### R31 Document corrections (b): send back, cancel on Approved, shortage banner (v2: new)

- Goal: closes PR-02 and PS-11 and UX question Q3. **Send back** (button "Send back"): on an Approved document, a person who may review it withdraws the approval and returns it to the creator as a Returned draft, reason required (at least 5 characters). **Cancel** is allowed on Approved for the same people (Supervisor or Admin; Owner or Admin for Large), reason required. The creator cannot pull back an Approved document alone: the approver undoes it, and the creator sees who to ask.
- Files: dialogs for Send back and Cancel-on-Approved with the reason field (the dialog keeps the typed reason on failure, R11), the shortage banner and the Approve-dialog warning when a line is already short (PR-02), the E5 copy variants (this slice owns NR-11: while the stock-changed banner shows, the sub-label "Ready to post" is hidden and Post is not offered), hint `doc.hint.draftNotMine`, the catalogs, component tests.
- API: B21, B22 (`POST /api/documents/{id}/send-back`, `cancel` with a required reason when Approved). Size: 650. Depends on: R28.
- Acceptance: the document's creator sees the Returned badge, banner and Home count unchanged after a send back; the Owner can send back or cancel an Approved Large document although the Owner cannot post; a reason below 5 characters is refused with the usual sentence; nobody but the listed reviewers sees the buttons (they come from `allowedActions`).
- Re-check: power (primary), novice, edge.

#### R32 Document corrections e2e (v2: new)

- Files: `e2e/tests/30-document-corrections.spec.ts` (the scenarios of section 6 of the corrections file: recall of an approval-free document, recall of a document waiting for approval, send back, cancel on Approved, idempotent repeats, role matrix), page-object additions, a row in `e2e/README.md`.
- API: none. Size: 500. Depends on: R31. Built by behavior-test-engineer.

#### R33 Follow-up of the money and QA backend fixes (v2: new, from `docs/pm/ROADMAP.md`, client batch CS1)

- Goal: use what PRs #44, #45 and #46 changed in the API: the ledger shows the balance after each entry (PS-14), one low-stock rule (the Stock "Low stock only" toggle, the Low stock list and Home agree, PR-04), the document search finds a remark word (PR-05), and a reversed document shows who reversed it.
- Files: `features/stock` ledger model and the `product-stock` ledger rows (balance column; one more line on the phone card), the `stock-list` toggle wired to the single rule, `document-editor` reversed-by line, the catalogs, and the client regeneration batch CS1 as the first commit (merged alone; nobody else regenerates in that window), specs.
- API: #44, #45, #46 merged (money rounding, balance after, low-stock fields, `reversedBy`, search index).
- Size: 200. Depends on: none (earliest wave 2, after the three PRs are on `main`). Lane: T.
- Acceptance: the ledger shows "balance after" in en, zh and ms; Stock, Low stock and Home show the same number of low products; a remark word is found by the document search; money columns stay at exactly 2 decimals (R04); Staff see no value in the new column (it is a quantity).
- Re-check: power (primary), novice.

### 1.4 Programme A: Advanced UI (A01..A22)

Goal: a data-heavy warehouse UI that feels like a modern product (powerful tables, fast data entry, drill-down without losing context, glanceable dashboards), built from our own `app-*` components. No library: behaviour that is hard to get right (overlay positioning, listbox key handling, virtual scroll, drag, live announcements, copy) comes from Angular CDK; markup, tokens and look are ours.

#### 1.4.1 Why CDK, what it costs, where it loads

| CDK package | Used for | Estimated raw size when used (A01 measures and replaces these) | Loaded by |
|---|---|---|---|
| `overlay` + `portal` + `platform` | `app-popover`: column menu, combobox panel, filter popover, notifications panel, tooltips | 25 to 30 kB | lazy: first list/editor route; the shell's bell panel is `@defer (on interaction)` |
| `a11y` | `LiveAnnouncer`, `ActiveDescendantKeyManager`, `FocusTrap` for non-`<dialog>` panels | 10 to 15 kB | with the overlay chunk |
| `listbox` | combobox options (A12) | 6 to 8 kB | with the combobox |
| `scrolling` | virtual scroll for continuous mode (A11) | 20 to 25 kB | own lazy chunk, loaded when the user switches a table to continuous mode |
| `drag-drop` | column reorder by drag (A03) | 35 to 40 kB | own lazy chunk, loaded on the first drag; the menu path (Move up/down) needs none of it |
| `clipboard` | "Copy" actions (A09, A15) | about 2 kB | with the bulk bar |

Justification: writing and then testing our own overlay positioning, listbox, virtual scroll and drag handling is about 3,000 lines of accessibility-sensitive code and would delay everything; the CDK is maintained with Angular (same major as `@angular/core`), tree-shakable, MIT, and has no styles. Estimated total if every part loaded: about 100 to 130 kB raw (about 30 kB gzip). In practice a user loads 30 to 60 kB raw on a list screen and nothing at all on Login, Home and the phone flows. Rules: (1) A01 adds at most 6 kB raw to the initial total (platform helpers) and proves it in its PR; (2) every other CDK part is behind a lazy route, `@defer` or dynamic `import()`; (3) Q04 enforces the chunk limits of section 4.3; (4) CDK is imported only inside `libs/shared/ui` (Q07 lint) and is never exposed through our public API. Known trap handled in A01: an overlay container appended to `<body>` is hidden behind a native modal `<dialog>` (top layer), so A01 ships a `DialogAwareOverlayContainer` (like the CDK's own `FullscreenOverlayContainer`) that re-parents the container into the open dialog.

#### 1.4.2 Which screens get which table feature

| Feature | Screens (table ids) |
|---|---|
| Column menu, resize, reorder, pinned first column (A02, A03) | all tables with five or more columns: `receipts`, `issues`, `approvals`, `stock`, `low-stock`, `ledger`, `products`, `movements`, `audit`, `partners`, `users` |
| Keyboard grid (A04), paging toolbar (A05) | all of the above |
| Saved views (A06), filter chips + URL state (A07) | `receipts`, `issues`, `stock`, `low-stock`, `products`, `movements`, `audit` |
| Multi-sort (A06) | `receipts`, `issues`, `stock`, `ledger`, `movements`, only once B8 exists; single sort until then |
| Row expansion (A08) | `receipts`, `issues`, `approvals` (document lines) |
| Selection and bulk bar (A09), export (A10) | `receipts`, `issues`, `stock`, `low-stock`, `products`, `movements` (export only) |
| Continuous scroll (A11) | `receipts`, `issues`, `ledger`, `audit` now (seek paging exists); `stock`, `products`, `movements` after B7 |

The phone keeps cards (L04). Column menu, resize, reorder, bulk bar and continuous mode are not offered below the `roomy` table band (640 px container); saved views, filters, sort, paging and export remain.

#### A01 CDK foundation and `app-popover`

- Goal: add the behaviour primitives once, and one popover that everything floating uses.
- Files: `web/package.json` (+ lockfile; `@angular/cdk` on the 22.x line matching `@angular/core`), `web/eslint.config.mjs` (restrict `@angular/cdk/*` imports to `shared-ui`), `web/libs/shared/styles/src/globals/_cdk.scss` + `globals/index.scss` (only the structural mixins, for example `@include cdk.overlay()`; never a prebuilt stylesheet), new `web/libs/shared/ui/src/lib/components/popover/*` (`app-popover` and `appPopoverTrigger`: connected position below or above with flip, 8px viewport margin, closes on Esc, outside pointer, scroll of a scrollable ancestor and route change; returns focus to the trigger; sets `aria-haspopup`, `aria-expanded`, `aria-controls`; `role` input `menu|listbox|dialog|region`; optional same-width-as-trigger; internal scroll with `max-height`; no animation under reduce-motion), `dialog-aware-overlay-container.ts`, `UiAnnouncerService` (thin wrapper over `LiveAnnouncer`, polite/assertive), `web/libs/shared/ui/src/index.ts`, `web/scripts/bundle-report.mjs` (prints per-chunk sizes; replaced by Q04's checker).
- API: none.
- Size: 650. Depends on: R05.
- Acceptance: A01.1 the PR description lists measured raw/gzip size per CDK part and the initial total before and after (initial growth <= 6 kB). A01.2 a popover opened inside a native modal `<dialog>` renders above it; the first Esc closes the popover only, the second closes the dialog (Chromium, Firefox, WebKit). A01.3 focus returns to the trigger; `aria-expanded` is true only while open. A01.4 an import of `@angular/cdk/*` from any other project fails lint.
- Motion (programme M, N16 to N19): popover and menu pop, `animate.enter` / `animate.leave` usage (+100). Respect `data-motion`; tokens only.
- Tests: popover spec (open/close paths, flip with stubbed rects, focus return), container spec, announcer spec; e2e stub `popover-in-dialog.spec.ts` on the three engines (the slice writes it; it joins the three-engine run when Q03 lands in wave 10).
- Re-check: edge (focus return, Esc order).

#### A02 Table core: ARIA grid, sticky header, pinned first column

- Goal: replace the internals of `app-table` (ruling N3) and absorb the table half of V7 and UX 10.1: the base that A03..A11 and L04 build on.
- Files: `web/libs/shared/ui/src/lib/components/table/*` (rewrite `table.component.{ts,html,scss}`; `table.types.ts` gains `priority?: 1 | 2 | 3` (default 2, used by L04), `pin?: 'start'` (first column only), `width?`, `minWidth?`, `lockVisible?` (identifier and status columns); `cell.directive.ts` unchanged), new `table-model.ts` (pure functions: columns to grid template, header model), `web/libs/shared/ui/src/lib/components/skeleton/` is NOT touched (R07 edits it in wave 3, R15 in wave 11; the table renders its own skeleton rows with the `skeleton` token). Markup: CSS grid with `role=table|rowgroup|row|columnheader|cell`, `aria-colcount`, `aria-rowcount`, `aria-rowindex`; the host is `container-type: inline-size; container-name: ims-table` and carries `data-mode="table|card"` from the `appContainerWidth` directive of L01. Look: sentence-case 13px muted header, no zebra, sticky header under the top bar (`position: sticky; top: var(--ims-sticky-top)` with `shadow(1)` when stuck), pinned first column sticky with an edge shadow while scrolled, row hover and selected style, inset focus ring, sort icon (not colour-only), progress line with `transform`, identifier cells `nowrap`, numbers right-aligned with tabular figures, rows use `--ims-row-height` and `--ims-cell-pad-y` (density works from day one), `$compact` renamed `$hasPhoneFields`.
- API: none.
- Size: 1,000 (about 650 source, 350 spec). Depends on: R05, L01.
- Acceptance: A02.1 every existing table consumer compiles and renders unchanged in content; phone cards keep structure (NV-08 two lines, 48px target). A02.2 TB-01, TB-03, TB-06: no wrap of SKU or document no, visible sort indicator, no headers over an empty table. A02.3 the header stays visible after scrolling the page 600px at 1280 and 768 (top equals the bar height); the pinned first column stays while the Reports region scrolls horizontally. A02.4 `aria-rowcount` and `aria-rowindex` are correct; axe has no serious violation on Receipts, Stock, Reports in light and dark. A02.5 CLS 0 when rows replace the skeleton; a column hidden by `columnWhen` has no skeleton bar and no header. A02.6 the L00 containment rules (column `type`, fixed layout, ellipsis and clamp) are carried over unchanged into the new grid: a 500-character unbroken name in a cell or a phone card never widens the page, and the L00 containment test stays green.
- Tests: migrated table spec plus pinned/sticky classes, aria counts, skeleton columns by permission, `$hasPhoneFields` output unchanged; a screenshot set (360, 768, 1280, light and dark) reviewed by fe-designer.
- Re-check: power (lists), novice (cards), edge (screen-reader tree on three tables: this is the highest-risk markup change, K16).

#### A03 Table column controls: visibility, resize, reorder

- Goal: users choose, size and order columns; the choice is remembered.
- Files: table toolbar slot and `table-columns-menu/*`, `table-resize.ts`, `table-reorder.ts` (menu path) and `table-reorder-dnd.ts` (CDK drag-drop, lazy `import()`), `TableLayout` model and its persistence in `UserPreferenceStore` (`table.<tableId>.layout`: visible keys, order, widths; every read in `try/catch`, unknown keys dropped), icons appended to `icon.paths.ts` (`columns`, `grip`, `rotate-ccw`), the catalogs.
- Behaviour: a "Columns" button opens an `app-popover` with a checkbox per column (identifier and status columns are locked on), Move up/Move down buttons per item (the always-available keyboard path), "Reset columns". Resize: a handle on each header edge (pointer drag, min 64px, max 480px, double-click resets); the handle is a focusable `role=separator` with `aria-valuenow`; Left/Right change by 8px (Shift 32px), Home/End min/max, Esc cancels. Drag to reorder loads the CDK chunk on first use; the pinned first column cannot move. Every change is announced through `UiAnnouncerService` ("Column Product width 240 pixels", "Product moved to position 3 of 8"). Not offered below the `roomy` band.
- API: none.
- Size: 1,000. Depends on: A01, A02.
- Acceptance: A03.1 hide/show/reorder works with the keyboard alone and survives reload for the same user, not for another. A03.2 a column the user may not see (`columnWhen`) never appears in the menu. A03.3 resize works with pointer and keyboard, never below the minimum, and does not cause horizontal page scroll (the table scrolls in its own region). A03.4 the drag chunk is not loaded until the first drag (network panel) and is <= 45 kB raw. A03.5 announcements are polite and not duplicated.
- Tests: menu, resize (keyboard map), reorder (menu path), persistence specs; stub e2e `table-columns.spec.ts` (hide, resize by keyboard, reload).
- Re-check: power (primary), edge (keyboard).

#### A04 Table keyboard grid navigation and row-action names

- Goal: a table you can drive without a mouse (UX 6.10) and ER-10.
- Files: `table/*` (roving tabindex, key handler `table-keys.ts`), per-screen row-action labels in `users`, `partners`, `settings` (approval rules), `audit-log`, `low-stock` templates and catalogs (`aria-label` "Edit user {name}", "Show details of {document no}", "New receipt for {sku}"), a hidden instruction line referenced by `aria-describedby`.
- Behaviour (ARIA grid pattern): the table body is one Tab stop; Arrow keys move between cells; Home/End row ends; Ctrl+Home/End first/last cell; PageUp/PageDown move by a visible page; Enter on the primary cell follows the row link; Enter/F2 on a cell with a widget focuses the widget and Esc returns to the cell; Space toggles selection when A09 is on; Tab leaves the grid; header cells are in the grid (Enter/Space sorts). Focus survives a reload of the same page (restored by row key) and a sort change. The grid role is only used when navigation is on; a table with no link or action stays `role=table`.
- API: none.
- Size: 800 (about 400 table, 150 per-screen labels, 250 specs). Depends on: A02, A03.
- Acceptance: A04.1 the full key map above, no keyboard trap, one Tab stop. A04.2 ER-10 closed: no two row actions on a screen share an accessible name (axe-style script over Users, Partners, Settings, Audit, Low stock). A04.3 focus stays on the same logical cell after sort or reload. A04.4 R22 can document the keys (no conflict with single-key shortcuts: those are off inside the grid).
- Tests: key-map spec (every key), focus restore spec, label specs for the five screens; stub e2e `table-keyboard.spec.ts`.
- Re-check: edge (primary), power.

#### A05 Paging toolbar: First/Last, page size, density button (replaces R21)

- Goal: UX 10.3 (PS-13) plus the table toolbar's density button. Server paging stays the default (N4).
- Files: `table/*` toolbar slot (density button "Density: Comfortable. Change." only at >= 1024px with a fine pointer, using `DensityService` from R12), `pager/*` (look, density rows, First/Previous/"Page 3 of 14"/Next/Last and a page size select 25/50/100 on desktop; phone keeps Previous/Next and the range; after a page change scroll to the top of the table region and move focus to the caption; a page beyond the 10,000-row offset limit disables Next/Last and says "Only the first 10,000 rows can be paged. Narrow the filters." with a link to continuous mode where A11 offers it), `createPageState` in `libs/shared/utils` (reset to page 1 when the size changes), every page that hosts a pager (`document-list`, `stock-list`, `low-stock`, `product-stock`, `products`, `catalog-lists`, `partners`, `users`, `audit-log`, `reports`); page size is a per-user, per-table preference clamped to the API maximum.
- API: none (`page`, `pageSize` exist; offset limit is `Limits.MaxOffsetRows` = 10,000, see B7 for lifting it with seek paging).
- Size: 600. Depends on: A02, R06, R12.
- Acceptance: A05.1 at 1280 First/Last/size select exist, at 360 they do not; changing size keeps filters and returns to page 1; Last then Previous works with 14 pages (no off-by-one); focus lands on the caption; the range text stays `26-50 of 333`. A05.2 beyond 10,000 rows the message appears instead of a 400. A05.3 the density button changes density and keeps scroll and focus (DN rules from R12).
- Tests: pager spec, page-state spec, density button spec; stub e2e `table-paging.spec.ts`.
- Re-check: power (PS-13 closed), edge (focus).

#### A06 Saved views and multi-sort

- Goal: a "view" = the table layout (A03) + sort + filters + page size, named, per user, stored with the theme and density preferences.
- Files: `TableView` model (versioned JSON, `v: 1`; invalid or newer data is ignored), `table-views/*` (Views menu: Default, saved views, "Save current view as...", rename, delete with confirm, set as default, reset), storage in `UserPreferenceStore` under `views.<tableId>` (at most 8 per table), multi-sort UI in the table header (click = sort by this column; Shift+click or the header menu "Then by" adds up to 3; a small order number on each active header; a sort summary line "Sorted by Date (newest first), then SKU"; `aria-sort` on the primary column and a screen-reader sentence for the others), pages pass `multiSort` only when the endpoint supports it.
- API: B8 (multi-column sort). Without B8 the multi-sort UI is built and tested against a fake endpoint but stays off on every screen (a client-side sort of one page of a server-paged list would be wrong); saved views still work with single sort. B9 (roaming) is optional; without it views are per browser (N12).
- Size: 900. Depends on: A03, A05, R06.
- Acceptance: A06.1 a view restores columns, order, widths, sort, filters and page size after reload and on another tab; applying a view never exposes a column the user lacks (`columnWhen` wins); another user on the same browser sees none of it. A06.2 deleting the last view falls back to Default; a corrupt entry is ignored without an error toast. A06.3 view names are 1 to 40 characters, unique per table, announced on save. A06.4 keyboard: menu, rename and delete reachable; delete asks for confirmation.
- Tests: view model spec (round trip, migration, invalid input), menu spec, multi-sort header spec; stub e2e `table-views.spec.ts`.
- Re-check: power (primary), edge.

#### A07 Filter chips bar and URL state

- Goal: see and clear what is filtering the list; make the state shareable and back-button safe.
- Files: new `web/libs/shared/ui/src/lib/components/filter-bar/*` (`app-filter-chips`: chips "Warehouse: Main WH x", "Status: Submitted x", "Search: widget x", "From: 01 Oct 2026 x"; chips are buttons with an accessible "Remove filter Warehouse: Main WH"; a "Clear all" button when any chip exists; a polite summary "3 filters active, 25 results"), `FilterChip` model and `filterChip()` helper in `@ims/utils`, `TableUrlState` helper (filters, sort, page, page size to query parameters; `replaceUrl` for typing, push for page changes; the URL wins over a saved view on load), adoption on `receipts`, `issues`, `stock`, `low-stock`, `products`, `movements`, `audit`.
- API: none (uses today's filters).
- Size: 900. Depends on: A06.
- Acceptance: A07.1 a chip exists for exactly the active non-default filters (the Mine/All scope is not a chip and "Clear all" does not change it). A07.2 removing a chip or Clear all reloads once and returns focus to the filter bar. A07.3 reload, Back and a pasted link restore the same list; an unknown or invalid parameter is ignored. A07.4 no chip, `title` or announcement carries a cost or value (D5). A07.5 chips wrap at 320 and are 44px high on touch.
- Tests: chip and helper specs, URL round trip, five page specs; stub e2e `table-filters.spec.ts`.
- Re-check: power (primary), novice (filters on phone), edge.

#### A08 Row expansion: document lines

- Goal: see a document's lines in the list without leaving it.
- Files: `table/*` (expander cell: a button with `aria-expanded` and `aria-controls`, a detail row region; Right/Left arrows expand/collapse on the expander in grid mode; at most 5 open), `containers/documents/.../document-list` and `approvals/.../inbox` (detail template: lines with SKU, product, qty and unit; unit cost and amount only with `cost.view`; triggers for approvals), `features/documents` (lines are fetched from `GET /api/documents/{id}` on first expand and cached for the session; invalidated when the list reloads), skeleton lines and a row-level error with Retry. L04 already provides the generic "hidden columns" disclosure; this slice adds the domain detail.
- API: none (document detail exists). Optional: a `lineCount` on `DocumentListItem` to label the button "Show 12 lines" (not required).
- Size: 620. Depends on: A04, L04.
- Acceptance: A08.1 expanding loads once, collapse/expand is instant after; failure shows a retry in the detail row, not a toast. A08.2 Staff expansion has no unit cost or amount; Clerk shows cost, never valuation. A08.3 keyboard and screen reader: the expander is named "Show lines of {document no}". A08.4 not offered in continuous mode (A11) and on phone cards (cards use a "Show lines" disclosure of the same content, 48px target).
- Motion (programme M, N16 to N19): row expansion by grid rows (page-size tables only) (+120). Respect `data-motion`; tokens only.
- Tests: expander spec, detail rendering per permission, cache spec; stub e2e `table-expand.spec.ts`.
- Re-check: power, novice (card disclosure), edge.

#### A09 Bulk selection and floating action bar

- Goal: select rows, act on them safely (N8).
- Files: `table/*` (selection column: header tri-state checkbox "Select page", row checkboxes named "Select {primary}", `aria-selected`; selection survives paging and sorting, clears on a filter change with an announcement; at most 500 explicit ids), `bulk-bar/*` (`role=region` named "Bulk actions": count, page-provided actions, "Clear selection", Esc clears; it sits at the bottom of the table region, adds bottom padding so the focused row is never covered, and publishes `--ims-floating-bar-height` for the toast host, ruling C14), `BulkAction` model (id, label, icon, `visible(permissions)`, `run(selection)`), shipped actions: Export selected (A10), Copy numbers/SKUs (CDK clipboard, one line per item), Low stock: "Create a receipt from these" (opens `/receipts/new?products=1,2,3`; the editor prefills lines at quantity 0 and creates nothing until the user saves; small change in `document-editor` to read the parameter, `document.create` only), icons appended (`check-square`, `copy`).
- API: none. Selection of "all matching" is not offered in this slice (export of all matching uses the server export, A10).
- Size: 840. Depends on: A04.
- Acceptance: A09.1 keyboard: Space toggles the focused row, Shift+Arrow extends, Ctrl+A selects the page. A09.2 the bar never covers the focused control (FR-03 at 400% zoom) and is not offered on phone cards. A09.3 "Create a receipt" is hidden without `document.create`, and the lines it creates carry no cost. A09.4 the toast host lifts above the bar (C14). A09.5 no state-changing bulk action exists (grep of the action ids in the PR).
- Motion (programme M, N16 to N19): floating bulk bar rise and fade (+40). Respect `data-motion`; tokens only.
- Tests: selection model spec (page/sort/filter behaviour), bar spec, action visibility per permission, clipboard spec; stub e2e `table-selection.spec.ts`.
- Re-check: power (primary), edge.

#### A10 Export CSV and Excel of the current view

- Goal: "download what I am looking at", respecting permissions.
- Files: table toolbar "Export" menu, `libs/shared/utils/src/lib/export/csv.ts` (pure builder), `table-export/*` (progress and Cancel), pages pass `exportFn`; the four existing server exports (stock summary, movements, ledger, low-stock) are called with the current filters and sort ("Excel, all matching rows"); every other table gets "CSV, current view".
- Behaviour: CSV is built in the browser from the same typed rows as the screen: only the effective columns in view order (hidden columns and `columnWhen` columns are not exported), header text in the user's language, rows fetched in pages of 200 with a progress bar and Cancel (`AbortController`), hard cap 5,000 rows ("Export is limited to 5,000 rows. Narrow the filters or use Excel."), selected rows variant (<= 500). Format: UTF-8 with BOM, CRLF, quoting, machine-readable numbers (money 2 decimals, `.` decimal, no thousands separator; quantity up to 4 decimals trimmed; ISO dates), spreadsheet-formula guard (a text cell starting with `=`, `+`, `-`, `@`, tab or CR is prefixed with `'`), file name `{screen}-{yyyyMMdd-HHmm}.csv`.
- API: B16 (server CSV, same filters and permission-aware columns, auditable) is optional. Without it: CSV is client-built (not auditable, so the security-engineer decides whether it is acceptable; recommended rule: Excel server export for anything above one page). Confirm that the four exports accept the same filters as their screens (`be-framework` to verify; no change expected).
- Size: 600. Depends on: A09.
- Acceptance: A10.1 a Staff CSV has no cost or value header or cell (unit test on the builder with the Staff column set, plus e2e). A10.2 `null` becomes an empty cell, never `0.00`. A10.3 a cell `=HYPERLINK(...)` is exported as text. A10.4 Cancel stops requests and writes no file. A10.5 chinese and malay text opens correctly in Excel (BOM).
- Tests: CSV builder spec (quoting, BOM, formula guard, numbers, nulls, permission column set), export flow spec with a mocked client; stub e2e `table-export.spec.ts` reads the download.
- Re-check: power (reconcile with the screen), edge; security-engineer reviews A10 once.

#### A11 Continuous (virtual) scroll mode, 10,000+ rows

- Goal: opt-in scrolling through very long lists without paging, while paging stays the default (N4).
- Files: `table/*` (`mode: 'paged' | 'continuous'`; toolbar toggle "Scroll mode" only where the page declares a seek source), `table-virtual/*` (lazy `import()`; `cdk-virtual-scroll-viewport` with fixed item size read from `--ims-row-height` at start and on density change), `SeekSource` in `libs/shared/utils` (windows of 200 rows through `afterId`/`nextCursor` (treated as an opaque cursor, N13: the GUID ADR renames it `cursor` and makes it a string); first call `includeTotal=true`, later calls `false`; prefetch when within 40 rows of the end; sort or filter change resets; latest wins), adoption on `receipts`, `issues`, `ledger`, `audit`.
- Behaviour and limits: `aria-rowcount` is the total and each rendered row has `aria-rowindex`, so assistive technology knows the position; rows are one line (ellipsis, identifiers `nowrap`) and row expansion (A08) is off; Ctrl+End is unavailable with a message ("Jump to the end isn't available in scroll mode; sort descending instead") because a seek cursor cannot jump; at most 10,000 rows are kept in memory, then the oldest window is dropped and the message "Showing the latest 10,000 rows" appears; not offered on phone cards (they page).
- API: B7 (seek paging for stock, products, movements) for the other screens; documents, ledger and audit already support `afterId` and `includeTotal`.
- Size: 1,000. Depends on: A04, A05.
- Acceptance: A11.1 with 10,000 rows the DOM holds at most 60 rows (count in e2e) and scrolling never shows a blank band longer than one frame at 4x CPU throttle (Q12). A11.2 the CDK scrolling chunk is not loaded in paged mode and is <= 45 kB raw. A11.3 a screen reader reads "row 1,200 of 10,432". A11.4 density change re-measures without losing the scroll position. A11.5 failure of a window shows a retry row at the end, not a toast.
- Tests: seek source spec (cursor, prefetch, reset, latest wins), virtual table spec with a fake source, row-count e2e on the gallery table (Q10) with 10,000 generated rows.
- Re-check: power, edge (screen reader positions).

#### A12 Combobox: async search, recents, scan mode

- Goal: one accessible type-ahead control for products, partners and anything else with a search endpoint.
- Files: new `web/libs/shared/ui/src/lib/components/combobox/*` (`app-combobox`: ARIA 1.2 combobox with a listbox popup built on `app-popover` and the CDK listbox; `ControlValueAccessor` and the `FORM_FIELD` token so it works inside `app-form-field` with label, error and the required marker of R02; inputs `search` (term to observable of items), `options`, `displayWith`, `valueOf`, `groupBy`, `minChars` (`MIN_SEARCH_LENGTH`), `recentKey`, `allowClear`, `scanMode`, `lookup`; an option template slot; states idle, loading (spinner after 300 ms), results, empty ("No product matches "{term}""), error row with Retry and no toast; polite "{n} results" announcement debounced 500 ms through `UiAnnouncerService`), icons appended if needed.
- Behaviour: Down opens and moves, Up, Enter selects, Esc closes then clears, Home/End inside the list, Tab leaves without selecting; `aria-activedescendant` keeps DOM focus in the input; typing is not searched while an IME composition is open (`compositionstart/end`, needed for Chinese input); recents (last 8 chosen, per user, per `recentKey`, cleared at sign-out) show when the field is focused and empty. Scan mode: a burst of characters (inter-key gap under 40 ms) ending with Enter is treated as a scanner: the `lookup` runs, an exact hit emits the selection, clears the field and keeps focus with a static check mark and the text "Added: {name}" for 1.5 s (no animation); a miss shows an inline sentence, never a toast (keeps `07-scan` and D8). Camera scanning is out of scope.
- API: none (`GET /api/products?search=`, `GET /api/products/lookup` and `GET /api/partners?search=` exist; B5 confirms what `search` matches).
- Size: 840. Depends on: A01, R08.
- Acceptance: A12.1 full keyboard map and ARIA (role, expanded, controls, activedescendant) pass the contract spec; axe clean. A12.2 latest request wins; a slow first answer never replaces a newer one. A12.3 IME composition never triggers a search. A12.4 recents never contain a cost or value and are cleared at sign-out. A12.5 inside a dialog the list renders above it and Esc closes the list first (A01).
- Motion (programme M, N16 to N19): combobox listbox pop (+40). Respect `data-motion`; tokens only.
- Tests: Spectator keyboard/ARIA matrix, race spec, IME spec, scan burst spec (fake timers), recents spec.
- Re-check: novice (scan on phone), edge.

#### A13 Combobox adoption: product picker and partner

- Goal: the editor's product search and partner field use `app-combobox`.
- Files: `web/libs/containers/catalog-shared/src/lib/product-picker/*` (keeps its public API: `warehouseId`, `notice`, `picked`, `termChanged`; the internals become a combobox in scan mode with the on-hand figure in the option template), `containers/documents/.../document-editor` header: partner field as an async combobox (`GET /api/partners?search=`), warehouse stays a select, `features/warehousing` store method for partner search if missing.
- API: none.
- Size: 500. Depends on: A12.
- Acceptance: A13.1 `07-scan` (barcode hit, repeat, miss, deactivated; focus on open) stays green unchanged. A13.2 the "DEMO-001: now 2." notice behaviour is preserved. A13.3 a Clerk or Staff option shows no cost. A13.4 on a 360px phone the list is a sheet-height popover with 48px options.
- Tests: picker spec (existing, adapted), partner field spec; run `07-scan`, `01-receive`, `02-issue-shortfall` locally.
- Re-check: novice (primary), power.

#### A14 Inline-editable lines grid: core

- Goal: receipt and issue lines edited like a spreadsheet on desktop; the phone keeps the card editor.
- Files: new `web/libs/shared/ui/src/lib/components/edit-grid/*` (generic `app-edit-grid`: `role=grid`, cell editors text/number/select, edit state per cell, validation hooks), `web/libs/containers/documents/src/lib/lines-grid/*` (wiring to the existing typed `LinesArray` form, so the form array stays the single source of truth and `DocumentEditorViewService` is untouched), `document-editor.component.html` (switch between grid and the existing `app-document-line-editor` by container width, `roomy` and wider get the grid), `undo-stack.ts`, the catalogs.
- Behaviour: Tab and Shift+Tab move to the next/previous editable cell and wrap across rows; Enter commits and moves down (on the last row it moves to the add-item combobox of A13); Esc cancels the edit and restores the value; typing on a focused cell starts editing and replaces its content, F2 edits in place; arrows navigate when not editing (A04 model); Ctrl+Z undoes the last change (cell edit, add, remove; 20 steps) and a visible "Undo: {what}" button does the same, disabled when empty. Columns: Product (read only), Quantity (4 decimals, minimum 0.0001), Unit (select when the product has several), Available (issues), Unit cost (receipts, `cost.view` only, 2 decimals), Remove. Per-cell validation on commit: `aria-invalid`, message linked with `aria-describedby` and shown under the cell and in a row status line; shortfall messages E1/E2 and "Set to N" stay; the changed row highlights as in R14.
- API: none.
- Size: 1,100 (edit-grid about 650, wiring about 450). Depends on: A02, A04, A12, R04, R14.
- Acceptance: A14.1 the full key map above works; entering ten lines of quantities needs no mouse. A14.2 invalid cells block Save/Submit exactly as today and never lose typed text. A14.3 a Staff grid has no cost column, no cost in `aria-label` or `title`. A14.4 undo restores value, focus and the highlight; undo of a removal restores the line at its index. A14.5 below the `roomy` band nothing changes (card editor).
- Tests: edit-grid key-map spec (Tab, Enter, Esc, F2, typing), validation per cell, undo stack, wiring spec against the real form array, existing `document-editor` specs unchanged and green; run `01-receive`, `02-issue-shortfall`, `13-cost-visibility`.
- Re-check: power (primary), novice (card editor unchanged), edge (screen-reader cell reading, keyboard-only).

#### A15 Lines grid: paste and editor integration

- Goal: paste many lines from a spreadsheet or message; finish the grid integration.
- Files: `lines-grid/paste.ts` (parse tab, comma or semicolon separated text from the `paste` event; optional header row detection; columns: product code (SKU or barcode), quantity, unit cost (only when the user has `cost.view` and the document is a receipt)), resolution of codes (B11 bulk lookup, or single `lookup` calls with concurrency 4), error summary through the `app-error-summary` of R14 ("3 lines were not added: line 4 'ABC' unknown product, line 7 quantity 'x' is not a number") with "Copy the failed lines" (CDK clipboard), one undo step for the whole paste, a confirm for more than 100 lines, `document-editor` final integration (desktop uses the grid), the catalogs, `07-scan` interplay (scanner input into a grid cell is never parsed as a paste).
- API: B11 recommended (bulk lookup). Without it the paste is capped at 50 lines and uses single lookups (about 4 in parallel).
- Size: 900. Depends on: A13, A14.
- Acceptance: A15.1 pasting 20 rows from a spreadsheet adds 20 lines (existing products add to the existing line like a scan: "DEMO-001: now 2."). A15.2 a bad row never blocks the good rows and is listed with its line number. A15.3 for Staff a pasted cost column is ignored with the neutral note "Only product and quantity are used." (no mention of cost values). A15.4 the whole paste undoes in one step. A15.5 a paste of 5,000 characters of non-tabular text adds nothing and says so.
- Tests: parser spec (delimiters, header, quotes, decimals, whitespace, Windows line ends), resolver spec with fakes, summary rendering, undo; e2e real-API `27-lines-grid` (paste, save, submit, post).
- Re-check: power (primary), edge, novice.

#### A16 Side drawer: document peek

- Goal: open a document beside the list, act on it, stay in context.
- Files: new `web/libs/shared/ui/src/lib/components/drawer/*` (`app-drawer` on a native modal `<dialog>`: right side 28 to 32rem from `roomy` up, full-screen sheet on phone; header with title and close; Esc, backdrop click, focus trap and focus return as `app-dialog`; reduce-motion: no slide), `containers/documents/src/lib/document-peek/*` (summary: number, status chip, type, warehouse, partner, dates; lines as a read-only A02 table; totals with `cost.view`; the existing `app-document-actions`, so buttons come from `allowedActions` only and every confirm dialog is unchanged; "Open full page"), router `?peek=<id>` so Back closes it and a link opens it, wiring on `document-list` and `inbox`: from 1024px a click on a non-Draft row opens the peek; a Draft opens the editor directly; Ctrl/Meta-click and the document-number link open the full page; on phone rows open the full page as today.
- API: none (`GET /api/documents/{id}`).
- Size: 920. Depends on: A01, R13, A02.
- Acceptance: A16.1 approve from the drawer works with the same dialogs, then the drawer and the list row update (no stale status). A16.2 Staff peek has no cost; Clerk has cost and no valuation. A16.3 focus: opening moves focus into the drawer, closing returns it to the row; Back closes it. A16.4 a document the user cannot see (other warehouse) shows the same "Page not found" as the full page (404, EA-25). A16.5 happy path steps do not increase for any role (UX FR-04): approvers need one click less, editors of drafts the same.
- Motion (programme M, N16 to N19): drawer slide and content reveal, swipe-to-close on phones (+120). Respect `data-motion`; tokens only.
- Tests: drawer a11y spec, peek spec per permission, URL sync, action refresh; stub e2e `drawer.spec.ts`, real-API `28-drawer-approve`.
- Re-check: power (primary), edge, novice (phone sheet).

#### A17 Document timeline

- Goal: who did what and when, on one screen.
- Files: new `web/libs/shared/ui/src/lib/components/timeline/*` (`<ol>` of events; icon + label, never colour only; who; absolute time in Kuala Lumpur `dd MMM yyyy HH:mm` plus a relative hint in text, not in a tooltip; optional reason block), `features/documents/src/lib/timeline/document-events.ts` (maps `DocumentDto` to events: Created, Submitted, Approved, Returned (with the rejection reason), Posted, Cancelled (reason), Reversed (reason), using `createdAt/By`, `submittedAt/By`, `approvedAt/By`, `postedAt/By`, `returnedAt/By` that exist today), use in the drawer (A16) and a "History" tab of the document editor (decision 7 reserved the tab list; small change in `document-editor`).
- API: B10. Today the document has the actor and time for create, submit, approve, return and post, but only a reason (no actor, no time) for cancel and reverse. Without B10 those two events show "Cancelled" or "Reversed" with the reason and no time or name, and the mapper never invents one.
- Size: 640. Depends on: A16.
- Acceptance: A17.1 events are in time order, each with icon, label, name and time where the API has them. A17.2 Staff sees no amounts in any event text. A17.3 the list is a real list for screen readers ("list, 5 items"). A17.4 with B10 the cancel and reverse events show actor and time (the mapper reads the new field when present).
- Motion (programme M, N16 to N19): timeline entrance (+40). Respect `data-motion`; tokens only.
- Tests: mapper spec for every status path (draft, submitted, approved, posted, returned, cancelled, reversed, with and without B10), timeline spec.
- Re-check: power, edge.

#### A18 Notifications panel

- Goal: approvals and returned documents reach people without hunting for them.
- Files: shell top bar bell button (`aria-label` "Notifications, 3 new"; badge number; a plain button in the shell, the panel is `@defer (on interaction)` so the overlay code is lazy), `core/navigation/.../notifications/*` (`NotificationsStore`: polls `GET /api/documents/counts` every 60 s while the tab is visible and on focus; loads previews only when the panel opens; "seen" set per user in `UserPreferenceStore` keyed by id + `concurrencyToken`, so a resubmitted document is new again), the panel (popover from 600px, full-screen sheet on phone): sections "Needs your approval" (`canAct`) and "Returned to you", up to 10 rows each (number, type, from whom, age, flags), "Mark all as seen", empty state "You're all caught up."; a polite announcement only when the count rises and at most once a minute; no pulsing, no animation; rows open the peek (A16) or the document. Reuses the Home feed store of R17 (`libs/features/documents/.../home-feed.store.ts`).
- API: none; optional B15 (ETag on counts, or a real notification feed with read state).
- Size: 760. Depends on: A01, R10, R17.
- Acceptance: A18.1 Staff and Clerk see only "Returned to you"; Supervisor and Admin both; Owner approvals of Large documents only (`canAct`). A18.2 no amount, value or cost in any row, name or live message (D5, FR-07). A18.3 polling stops when the tab is hidden and when signed out; offline shows the last count with no error. A18.4 the badge count equals the sidebar badge and the list totals (RA-08). A18.5 Esc closes the panel and focus returns to the bell.
- Motion (programme M, N16 to N19): bell panel pop, badge bump (+60). Respect `data-motion`; tokens only.
- Tests: store spec (polling with fake timers, seen set, visibility), panel spec per role, count equality; stub e2e `notifications.spec.ts`.
- Re-check: power (primary), novice (phone sheet), edge.

#### A19 SVG chart primitives

- Goal: small, honest, accessible charts drawn by us. No chart library.
- Files: new `web/libs/shared/ui/src/lib/components/chart/*`: `app-chart-frame` (title `h3`, period, one-sentence summary shown and used as description, legend, a "Show as table" toggle with `aria-pressed` that reveals a real data table, skeleton/empty/error states from the widget frame), `app-line-chart`, `app-bar-chart` (vertical, horizontal, stacked), `chart-scale.ts` (nice ticks, linear time scale; unit-tested), colour tokens `chart-1..chart-4` appended to the palette with their contrast pairs in `check-contrast.mjs` in the same PR, patterns for forced colours (see L06).
- Behaviour: `<svg role="img" aria-labelledby aria-describedby>` sized by its container (container query plus `ResizeObserver` for tick count); the plot area is focusable and Left/Right step through data points, announcing "12 Oct, 42 receipts" and showing a persistent value readout (nothing is hover-only); series are told apart by shape, dash or label as well as colour (FR-08); axis labels use the app pipes: money with 2 decimals and thousands separators, never "12.4k" (N10); no animation, no gradients, no shadows; prints in black and white with patterns.
- API: none (data is passed in).
- Size: 1,000. Depends on: R05, L01, L06.
- Acceptance: A19.1 every chart has a text alternative (title + summary) and a data table with the same numbers. A19.2 keyboard stepping works and is announced once per step. A19.3 axis and tooltip money has exactly 2 decimals. A19.4 colour is never the only carrier of meaning (grayscale screenshot, forced-colors screenshot). A19.5 the chart chunk is <= 40 kB raw and lazy.
- Tests: scale spec (ticks, edge cases: all zero, one point, negative), chart a11y spec, table-toggle spec, keyboard stepping spec, money formatting spec; stub visual screenshots of the gallery page (Q10).
- Re-check: edge (primary), power.

#### A20 Home dashboard charts

- Goal: three glanceable charts on Home for the roles that can read them.
- Files: `web/libs/containers/home/src/lib/widgets/*` (W14 Stock value, last 30 days: line, `valuation.view`; W15 Movements by day, last 14 days: stacked bars of posted receipts and issues as document counts, `reports.read`; W16 Low stock by category: horizontal bars of the top 8 categories plus "Other", `stock.read`), `libs/features/stock` and `libs/features/reports` stores, placement in the Reviewer, Overseer and Admin layouts after W10 (Doers keep W7), widgets load with `@defer (on viewport)` and each fails on its own with Retry.
- Permission rules (D5): W14 is not rendered at all (no frame, no skeleton, no text) without `valuation.view`, so Staff and Clerk never see it; W15 and W16 carry counts only, never money or quantity sums across units.
- API: B13 (stock value history) is required for W14: until it exists W14 is not rendered (the widget feature-detects a 404). B12 (movements by day) is recommended: interim, W15 counts posted documents per day from `GET /api/documents?status=Posted&from&to&pageSize=200&includeTotal=false`, at most 5 pages, and says "Based on the latest 1,000 documents" when it hits the cap. B14 (low stock by category) is optional: interim, W16 groups the first 200 rows of `GET /api/stock/low-stock` by `categoryName` and says so when `lowCount` is larger.
- Size: 900. Depends on: A19, R19.
- Acceptance: A20.1 Supervisor and Owner/Admin Home show W15, W16 and (with B13) W14; Staff and Clerk Home show none of the three (DOM scan, `aria-label` and `title` scan for "value"). A20.2 the first-load call count stays within RA-09 (charts add at most 3 deferred calls). A20.3 W14 numbers reconcile with the Stock value tile (last point equals the tile). A20.4 each chart has the table toggle and the summary sentence in en, zh and ms.
- Motion (programme M, N16 to N19): chart draw-in, series hover, tooltip (+200). Respect `data-motion`; tokens only.
- Tests: widget specs per permission, deferred loading spec, feature-detect spec; stub e2e `charts.spec.ts` (roles, table toggle, keyboard).
- Re-check: power (primary), edge, (novice only to confirm nothing changed on the Doer Home).

#### A21 Empty-state illustrations

- Goal: friendly but quiet empty states.
- Files: new `web/libs/shared/ui/src/lib/components/illustration/*` (`app-illustration name="first-use|no-results|no-access|error|offline|all-clear"`: inline SVG, `currentColor` plus two tokens, 96px box, `aria-hidden`, at most 1.2 kB each, no animation), `empty-state/*` (`illustration` input, shown from the `roomy` band; below it the R13 icon disc stays), adoption where the variant is clear (stock empty, filtered none, role-limited, error, offline banner area, Approvals "all clear").
- API: none. Size: 450. Depends on: R13.
- Acceptance: A21.1 decorative only (no name in the accessibility tree), legible in dark and forced colours, grayscale-safe. A21.2 total added markup <= 8 kB raw.
- Motion (programme M, N16 to N19): empty-state entrance with illustration parts (+100). Respect `data-motion`; tokens only.
- Tests: component spec (every name renders, aria-hidden), gallery entry. Re-check: edge.

#### A22 Breadcrumbs and consistent page header

- Goal: every screen has the same title block, actions and trail.
- Files: routes (`data: { breadcrumb: '<message key>' }` in each `*.routes.ts`; dynamic labels such as the document number through `PageTitleService.detail`), new `web/libs/shared/ui/src/lib/components/breadcrumbs/*` (`nav` with `aria-label`, `ol`, current item `aria-current="page"`, middle items collapse into a "..." popover when the container is narrow, hidden on phone where the back link stays), `page-header/*` (one API: `heading`, `subtitle`, a chips slot, an actions slot where the primary action is last and secondary actions collapse into a "More" popover at the `narrow` band; breadcrumbs appear automatically), ER-16 (titles come from the loaded record, an in-route not-found sets "Page not found", the login `h1` is "Sign in"), mechanical adoption on the screens that pass their own back link or arrange their own actions (about 12 templates).
- API: none. Size: 800. Depends on: R10, L01.
- Acceptance: A22.1 every routed screen shows a trail on >= 600px with the correct current item; titles match headings (ER-16). A22.2 actions never overflow at 320: the secondary ones are in "More" and reachable by keyboard. A22.3 existing e2e page objects that find the heading by role keep working.
- Tests: breadcrumb spec (collapse, current item), page-header spec (narrow container), route-data completeness spec (every route has a breadcrumb or an explicit opt-out).
- Re-check: edge (primary), novice.

### 1.5 Programme Q: Stable (Q01..Q12)

Goal: a regression is caught by a machine on the pull request that causes it, in under 15 minutes, with a picture and a rule that says who decides. Today the PR gate is lint, format, unit tests, contrast and a build; browser tests run only by hand or weekly against a real database (`e2e.yml`), so a layout, accessibility or bundle regression is found a week later by a QA persona.

#### 1.5.1 The gate model

| Layer | What it checks | Tool | Runs | Blocks the PR |
|---|---|---|---|---|
| Static | ESLint (module boundaries, template accessibility), Stylelint (token-only colours, no raw spacing in new files), design-rules script (no inline styles, no raw native controls in pages, no UI library, CDK only in `shared-ui`), Prettier, strict TypeScript, contrast gate | `npm run lint`, `format:check`, `contrast` | every PR | yes |
| Unit and contract | one Spectator spec per shared component with keyboard and ARIA behaviour and a `jest-axe` check; store and service specs | Jest | every PR | yes |
| Build and size | production build, initial and per-chunk budgets, no `/__gallery` in the production output | `build:prod`, `bundle:check`, `check-no-gallery` | every PR | yes |
| Stub e2e, Chromium | axe on every key screen in light and dark at 360 and 1280; visual screenshots at 360, 768, 1024, 1440 in light and dark; responsive subset (320, 390, 768, 1280); CLS on lists; smoke | Playwright against the production build with a stubbed API (no database) | every PR that changes `web/` or `e2e/` | yes (visual: report-only until gate G3) |
| Stub e2e, Firefox and WebKit | smoke subset: sign in, Home, list, editor, dialog, language and theme switch, palette | Playwright | every PR that changes `web/` or `e2e/` | yes |
| Real-API smoke on 3 engines | `@smoke` scenarios (01, 02, 03, 07, 08, 09) | Playwright, SQL Server container | nightly | no, but a red night blocks the next release |
| Full real-API suite | all numbered scenarios, Chromium | Playwright | weekly (today's `e2e.yml`) and on demand | no |
| Performance and environment | 5,000-row table interaction budget, long tasks, Lighthouse CI on login and the gallery table, full responsive matrix at 8 widths plus 200% and 400% zoom, forced colours and high contrast, offline and service-worker project | Playwright (CDP), `@lhci/cli` | nightly | no, but regressions open a ticket (1.5.3) |
| Dependencies | `npm audit --omit=dev --audit-level=high` | CI | every PR | yes (exists) |

PR wall-clock target: 15 minutes at most, by running the jobs in parallel: `web-static-unit-build` (about 8 minutes), `stub-chromium` (about 8 minutes, four Playwright workers), `stub-firefox` and `stub-webkit` (about 3 minutes each). Jobs are skipped when neither `web/**` nor `e2e/**` changed.

#### 1.5.2 Visual baselines: where the PNGs live (decision)

Options: (a) PNGs committed in the repository: about 128 images of 40 to 80 kB, 5 to 10 MB per full refresh, forever in the history of `main`; (b) Git LFS: needs LFS enabled and a quota on the host and a local install for every developer; (c) CI artifacts only: expire after 90 days, so there is no durable reference; (d) an orphan branch `visual-baselines` used as a data store.

**Decision: (d).** Baselines live on the orphan branch `visual-baselines` (never merged, never cloned by default). The PR job checks out that branch with `fetch-depth: 1` into `e2e/__baselines__/`, runs the comparison, and uploads expected, actual and diff images as an artifact and in the job summary. Baselines are produced only in CI inside the pinned Playwright container image (same version as `@playwright/test`), because fonts and anti-aliasing differ per OS; they are never generated on a developer machine. A baseline changes only through the manual workflow `visual-update.yml`, started by the fe-designer or the fe-architect on the PR branch after looking at the diff: it commits to `visual-baselines/pr-<n>/...`; a post-merge job promotes `pr-<n>` to `main/` and deletes it; a closed PR's folder is deleted. A monthly job squashes the branch history (it is a data branch, so a force-push is acceptable there and nowhere else). Cost: the main repository stays untouched, and the extra clone size is zero for people who do not fetch that branch.

#### 1.5.3 Failure triage (who decides what)

| Failure | First look | Decision owner | Rule |
|---|---|---|---|
| Lint, unit, build, budget | PR author | PR author; fe-architect for a budget increase | A budget increase needs a written reason in the PR and a line in `web/bundle-budget.json`; otherwise fix the code |
| Visual diff | PR author reads the diff | fe-designer decides intended or regression | Intended: run `visual-update.yml`, say why in the PR. Regression: fix |
| axe violation (serious or critical) | PR author | fe-framework fixes; qa-edge confirms | A violation on a screen the PR did not touch is a finding in `docs/qa`, added to `KNOWN` with the finding id and a 14-day expiry; no silent exceptions |
| Responsive or CLS failure | PR author | fe-framework | `KNOWN_RESPONSIVE` follows the same finding-id and expiry rule |
| Nightly failure | orchestrator reads the run | project-manager opens a HANDOFFS entry naming the owner | Fix within the next wave; a red nightly blocks release, not merges |
| Flaky test (fails, then passes on the CI retry) | anyone | fe-architect | Labelled `flaky`, tagged `@quarantine` (still runs, does not gate), at most 5 quarantined at once, each with an owner and a 14-day expiry; an expired quarantine blocks the PR gate again |

#### Q01 Stub-API harness and screen registry

- Goal: run the real production build in a browser with no API or database, so every later gate is fast and deterministic.
- Files (about 700 e2e + 200 tooling): `e2e/src/stub/stub-api.ts` (`page.route('**/api/**')` handler: method + path to typed fixture, honours `page`, `pageSize`, `search`, `afterId`, `status`; `POST /api/auth/refresh` returns the session of the chosen role), `e2e/src/stub/fixtures/*.ts` (typed with `import type` from `web/libs/shared/api/src/lib/api-client.ts` through a `paths` entry in `e2e/tsconfig.json`, so an API change breaks fixture compilation), `e2e/src/stub/sessions.ts` (permission sets per role, asserted equal to the `PERMISSION` constants by a test), a data generator (`makeStockRows(n)`, `makeDocuments(n)` for 200 and 10,000 rows), `e2e/src/screens.matrix.ts` (the registry: id, path, role, fixtures, ready locator, masks; about 16 screens: login, Home per role, Receipts list, Receipt editor with 3 lines, Issue editor with shortfall, Stock, Low stock, Reports movements, Approvals, Settings, Product edit, Users, Audit log, plus entries added by later slices: drawer, palette, dialog, gallery pages), `e2e/playwright.config.ts` (projects `stub-chromium`, `stub-firefox`, `stub-webkit` with `webServer` running `npm run serve:prod`; the existing `chromium` real-API project is unchanged), fixed clock (`page.clock.setFixedTime`), `serviceWorkers: 'block'`, `e2e/README.md`.
- API: none (it replaces it in tests).
- Size: 900. Depends on: none.
- Acceptance: Q01.1 `npm run test:stub` runs on a clean machine with Node only (no .NET, no SQL Server) in under 3 minutes. Q01.2 changing a field name in `api-client.ts` makes `e2e` fail to type-check. Q01.3 the same registry is the only list of key screens used by Q02, Q03, Q12 and L08.
- Tests: the harness has a self-test (each registry screen loads for its role and reaches its ready locator).
- Re-check: none (tooling); behavior-test-engineer reviews the page-object boundary.

#### Q02 Visual regression and baseline storage

- Goal: a screenshot of every key screen at four widths in two themes is compared on every PR.
- Files (about 450 e2e + 250 workflows): `e2e/tests/stub/visual.spec.ts` (registry x widths 360, 768, 1024, 1440 x `light` and `dark` via `colorScheme`; `toHaveScreenshot` with `animations: 'disabled'`, `caret: 'hide'`, `maxDiffPixelRatio: 0.002`, masks for time-dependent text; reduced motion on; locale and time zone fixed), `.github/workflows/visual-update.yml`, `.github/workflows/visual-promote.yml` (post-merge), `.github/workflows/visual-squash.yml` (monthly), a helper `npm run test:visual:docker` that runs the same container locally for viewing only, `docs/architecture/quality-gate.md` (section 1.5.2 text).
- API: none.
- Size: 700. Depends on: Q01, R06.
- Acceptance: Q02.1 about 128 comparisons run in under 5 minutes on the PR job. Q02.2 a one-pixel colour change of `primary` fails the job and shows expected, actual and diff. Q02.3 until gate G3 the job is report-only (comment and summary, green status). Q02.4 baselines exist only on `visual-baselines`; `git ls-files` of the repository contains no PNG under `e2e/`.
- Tests: the suite itself; a deliberate change in a throw-away branch proves red then green after `visual-update`.
- Re-check: fe-designer approves the first baseline set at gate G2.

#### Q03 axe on the matrix and three-engine smoke

- Goal: accessibility and cross-engine breakage found per PR instead of weekly.
- Files (about 450 e2e + 150 workflow): `e2e/tests/stub/a11y-matrix.spec.ts` (axe with `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa` on every registry screen x light/dark x widths 360 and 1280; serious and critical fail; moderate is attached as an annotation; the `KNOWN` mechanism of `11-accessibility` is reused: entry = finding id + expiry date), `e2e/tests/stub/smoke.spec.ts` tagged `@smoke` (sign in, Home, a list, open an editor, open a dialog, switch language, switch theme), projects `stub-firefox` and `stub-webkit` run only `@smoke`; the real-API scenarios 01, 02, 03, 07, 08, 09 get the `@smoke` tag and nightly projects for Firefox and WebKit; `docs/qa/release-device-checklist.md` (manual iOS Safari and Android Chrome checklist: install the PWA, keyboard with the sticky bar, safe area, print preview, camera not used). WebKit on Linux is close to, not identical to, iOS Safari: the checklist covers the difference.
- API: none.
- Size: 600. Depends on: Q01.
- Acceptance: Q03.1 the matrix run finds the known defects of today as `KNOWN` entries or fixes (none silent). Q03.2 smoke is green on the three engines. Q03.3 the existing `11-accessibility` scenarios stay (they cover the real API).
- Tests: itself. Re-check: edge reviews the first axe report.

#### Q04 Bundle and lazy-chunk budgets

- Goal: size cannot creep.
- Files (about 250 scripts + 100 config): `web/apps/main/project.json` (budgets as set in R01: initial warning 600 kB, error 650 kB; add `anyScript` warning 120 kB raw), `web/scripts/check-bundle.mjs` (builds with `--stats-json`, walks the esbuild metafile: initial raw and gzip, every lazy chunk against `web/bundle-budget.json` (default warning 90 kB, error 120 kB raw; named allowances: shared table chunk 90, combobox/overlay 40, virtual 45, drag 45, charts 40, palette 40), global styles <= 9 kB, total JS), `npm run bundle:check`, `web/bundle-baseline.json` (committed numbers from main; the job prints the delta), CI step, a note in the PR template (owner: orchestrator). Today: initial 538.51 kB raw / 123.48 kB transfer; the largest lazy chunk is 72 kB. If the metafile does not expose lazy chunks, the fallback is to name them from `ngsw.json` and check size per file.
- API: none. Size: 350. Depends on: R01.
- Acceptance: Q04.1 adding a 200 kB dependency to a lazy route fails the job; adding 7 kB to the initial total fails the per-slice rule (6 kB) unless `bundle-budget.json` carries a stated allowance. Q04.2 the output is one readable table in the job summary.
- Tests: the script has a fixture-based test (a fake metafile). Re-check: none.

#### Q05 Global error handling, route errors, GET retry

- Goal: nothing fails silently or in a loop; retries are safe. (The session-ended-while-editing case, ER-01, is R26.)
- Files: `web/libs/core/http/src/lib/errors/global-error.handler.ts` (Angular `ErrorHandler`: logs with the correlation id to the console, one deduplicated toast per 10 s "Something went wrong. Reload the page if it keeps happening.", never for `HttpErrorResponse` (already mapped), re-entrancy guard), `retry-get.interceptor.ts` (registered before the error interceptor so the toast fires only after the last attempt; `GET`/`HEAD` only; status 0, 502, 503, 504 and the timeout; at most 2 retries after 500 ms and 1,500 ms with +-30% jitter; honours `Retry-After` up to 5 s; never for 4xx and 429, never when `If-Match` is set or the `NO_RETRY` context flag is set (exports, downloads); cancelled together with the request), the router navigation error handler (a lazy chunk that fails to load, `Failed to fetch dynamically imported module`, shows `app-route-error` "A new version is available. Reload." and asks `SwUpdate.checkForUpdate()`), `app-route-error` page in `containers/system` (retry, go Home, sets the page title; also used for in-route not-found, ER-16), the pattern `@defer { } @error { retry }` for lazy widgets documented in `web/docs/conventions/component-design.md`, the catalogs.
- API: none. Optional B17: `POST /api/client-errors` to collect frontend errors.
- Size: 750. Depends on: R11.
- Acceptance: Q05.1 a table of statuses x methods proves GET is retried only for 0/502/503/504/timeout and POST, PUT, PATCH, DELETE never, in unit tests and in a stub e2e that counts requests. Q05.2 a failed chunk load shows the route error, not a blank page. Q05.3 one announcement per failure (R11 rule) holds after retries. Q05.4 an uncaught error in a component shows at most one toast per 10 s. Q05.5 an in-route "not found" has the title "Page not found - IMS".
- Tests: handler, retry (status x method table), router error specs; stub e2e `errors.spec.ts` (abort a chunk, fail a GET twice then succeed).
- Re-check: edge, power, novice.

#### Q06 Offline banner and safe read cache

- Goal: a warehouse with bad Wi-Fi still shows the last data, and never loses or fakes a write (N9).
- Files: `web/libs/core/pwa/src/lib/online.service.ts` (`$online` from `navigator.onLine`, the `online`/`offline` events and two consecutive status-0 GET failures within 10 s; recovers on the event or one successful GET), `app-offline-banner` in the shell (`role=status`: "You're offline. Lists show the last saved data. Saving and posting are unavailable until you're back online."; not dismissable while offline), command buttons (`app-document-actions`, Save, Submit, Post and the form saves) become `aria-disabled` with the reason "You're offline" through the reason-disabled pattern of R08 (no queue, no retry later, no `localStorage` of commands), `web/apps/main/ngsw-config.json` (new data group `read-cache`: `strategy: freshness`, `timeout: 3s`, `maxAge: 1h`, `maxSize: 40`, only `/api/stock`, `/api/stock/low-stock`, `/api/stock/summary`, `/api/documents` (list), `/api/documents/counts`; not document detail, reports, audit, users, auth, exports), `PwaService` (clear data caches at sign-out and when the signed-in user id differs from the last user stored on the device (`ims.lastUser`)), "as of {time}" taken from the response `Date` header while offline.
- API: none. Depends on the security-engineer's decision about ER-14 (authenticated responses currently carry no `Cache-Control`; B-item S1 asks for `private, no-store`): the builder tests whether `ngsw` still caches a `no-store` response and records the answer in the PR; if it does not, the read cache is limited to what the headers allow and the slice reports it.
- Size: 800. Depends on: Q05, R06.
- Acceptance: Q06.1 stub e2e project with `serviceWorkers: 'allow'` against `serve:prod`: sign in, visit Stock and Receipts, go offline, reload: the lists show the saved rows with the banner; Save, Submit, Post and Approve are disabled with the reason; going online re-enables them with no request fired by the app in between (network log). Q06.2 after sign-out, or after a different user signs in, no cached list is served. Q06.3 no code path persists a command (grep + a unit test that every command goes through `ApiCommand` with no storage). Q06.4 Staff cached lists contain no cost or value (the API sends none).
- Tests: online service spec (fake timers), banner and button reason specs, e2e offline project.
- Re-check: edge (primary), power, novice (phone, bad network). Security-engineer approves the cached endpoint list.

#### Q07 Lint and strictness: tokens, inline styles, CDK and library imports

- Goal: the design system cannot be bypassed by accident.
- Files: `web/.stylelintrc.json` (raw colours: no hex, `rgb()`, `hsl()`, named colours, `oklch()` outside the palette files; `font-size`, `z-index`, `border-radius`, `box-shadow` and spacing properties may not take raw numeric literals except `0`, `1px` borders and `calc()` with tokens; `animation-name` allow-list of the three loops; `transition` allow-list (R08); `@media` only through the layout mixins (N5), with `files` overrides for the legacy list), `web/.stylelint-legacy.txt` (files exempt from the new rules today; the PR guard script fails if this list grows; R24 deletes it), `web/scripts/check-design-rules.mjs` (scans `*.html` and inline templates: no `style="`, no `[style.x]` except custom properties `[style.--ims-...]` for dynamic values such as column widths, no `[ngStyle]`; no raw `<button>`, `<input>`, `<select>`, `<textarea>`, `<dialog>`, `<table>` in `containers/**` and `features/**` templates (allow-list with a reason per entry); `<img>` has `alt`; `target="_blank"` has `rel`; in TS no `.style.` writes and no `innerHTML` outside `shared/ui`; no `localStorage`/`sessionStorage` outside `core-preferences` and the documented exceptions), `web/eslint.config.mjs` (`@angular-eslint/template` accessibility preset: alt-text, elements-content, label-has-associated-control, interactive-supports-focus, click-events-have-key-events, mouse-events-have-key-events, role-has-required-aria, table-scope, valid-aria; `no-restricted-imports` for UI libraries (N1) and for `@angular/cdk/*` outside `shared-ui` (A01 adds the CDK part)), per-library `tsconfig` for `shared-ui`, `core-preferences` and every new library: `noUncheckedIndexedAccess` (the application already has `strict`, `noImplicitOverride`, `noPropertyAccessFromIndexSignature`, `noImplicitReturns`, `noFallthroughCasesInSwitch`, `strictTemplates`; `exactOptionalPropertyTypes` is deferred because the generated client uses `T | undefined` fields and would churn), `npm run lint` runs the design-rules script.
- API: none. Size: 700 (about 350 config + script, 350 fixes and specs; the legacy list absorbs the rest).
- Depends on: R05.
- Acceptance: Q07.1 adding `color: #fff`, `style="..."`, a raw `<button>` in a page, `import ... from '@angular/material'`, or `@angular/cdk/overlay` in a container each fails `npm run lint` with a message naming the rule. Q07.2 the legacy list is committed and its size is printed; Q07.3 the template accessibility preset passes on all templates or the exceptions are listed with a reason.
- Tests: the script has a fixture suite (one violating and one clean snippet per rule). Re-check: none.

#### Q08 Contract tests: form controls (jest-axe)

- Goal: one Spectator spec per shared component with keyboard and ARIA behaviour; first the controls.
- Files: dev dependency `jest-axe` (+ types), `web/libs/shared/ui/src/test/a11y.ts` (`expectNoA11yViolations(fixture)`; colour contrast is disabled there because jsdom has no layout, the contrast gate and Q03 cover it), specs for `input`, `select`, `textarea`, `checkbox`, `file-input`, extensions of the existing `search-input`, `radio-group`, `form-field`, `button` specs, `combobox` (A12 ships its own; Q08 only checks it follows the template).
- A contract = for each component: renders with each input state (default, disabled, readonly, invalid, required, busy where relevant), keyboard map (Tab, Space, Enter, arrows where the pattern needs them), ARIA (role, name, `aria-invalid`, `aria-describedby`, `aria-required`, `aria-busy`), outputs, no axe violation, forced content projection.
- API: none. Size: 700. Depends on: R08.
- Acceptance: Q08.1 each listed component has a spec file with the sections above. Q08.2 breaking `aria-describedby` on `app-form-field` fails a test. Q08.3 total suite time grows by less than 15 s.
- Re-check: none.

#### Q09 Contract tests: remaining components

- Files: specs for `banner`, `card`, `empty-state`, `icon`, `page-header`, `pager`, `skeleton`, `spinner`, `stat-card`, `tabs` (or the segmented control), `toast`, and, for components that arrived after wave 1, the specs they must ship themselves are only reviewed here (`table`, `popover`, `drawer`, `timeline`, `chart`, `illustration`, `breadcrumbs`, `filter-chips`, `edit-grid`).
- API: none. Size: 800. Depends on: R09, R13.
- Acceptance: Q09.1 every component exported from `libs/shared/ui/src/index.ts` has a `*.spec.ts` (a test walks the barrel and fails for a component without a spec). Q09.2 reduce-motion branches of spinner, skeleton and progress are asserted. Q09.3 the toast spec asserts `role` (alert for errors, status for success) and the live-region behaviour.
- Re-check: edge.

#### Q10 Development gallery `/__gallery`

- Goal: reviewers and QA agents see every component state in one place, in all four combinations, and it cannot reach production.
- Files (about 600 app + 100 tooling): `web/apps/main/src/app/gallery/*` (a registry `GALLERY_ENTRIES: { id, title, component, states[] }`, a shell with an index, a toolbar for theme (light/dark), density (comfortable/compact) and a width frame (the demo area is a resizable box, so container queries respond; no iframe), per-component pages for the existing 24 components with states: default, disabled, busy, error, empty, long text, loading, narrow container), a dev-only stylesheet `gallery/_scope.scss` that re-emits the theme and density tokens on `.gallery-scope[data-theme][data-density]` so one page can show light and dark side by side without touching `_themes.scss`, query parameters (`?theme=dark&density=compact&w=360`) so Playwright and QA can open one state, `web/apps/main/project.json` (configuration `gallery` = production optimisation with the gallery routes included, used by CI for stub e2e, visual and perf; configuration `production` replaces `gallery.routes.ts` with an empty routes file through `fileReplacements`), a guard that also checks `environment.gallery`, `web/scripts/check-no-gallery.mjs` (greps `dist/main/browser` for `__gallery` and the gallery chunk names; wired into CI after `build:prod`), a rule in section 4.1: every new component adds its gallery entry.
- Not shown in the gallery: `:hover` and `:active` (CSS pseudo-states cannot be forced without editing every component); the visual suite hovers and presses with Playwright instead.
- API: none (fixtures). Size: 760. Depends on: R09, R13.
- Acceptance: Q10.1 `npm run dev` serves `/__gallery`; `build:prod` output contains no gallery code (script green, bundle size unchanged +-0.1 kB). Q10.2 the gallery table page can generate 5,000 and 10,000 rows (used by A11 and Q12). Q10.3 each page renders without console errors in the four combinations.
- Motion (programme M, N16 to N19): gallery motion-mode switch and "replay" per component (+60). Respect `data-motion`; tokens only.
- Tests: registry completeness test (every exported component has an entry or an explicit opt-out). Re-check: fe-designer reviews the pages.

#### Q11 CI wiring, triage rules, flip the visual gate

- Goal: the model of 1.5.1 is real and documented.
- Files: `.github/workflows/ci.yml` (web job split: static/unit/build/budgets/no-gallery; new jobs `stub-chromium`, `stub-firefox`, `stub-webkit` with path filters; cache for Playwright browsers and the Nx build), `.github/workflows/nightly.yml` (real-API smoke on 3 engines, perf and Lighthouse, full responsive matrix with zoom, forced colours, offline project), `.github/workflows/e2e.yml` (unchanged weekly; documents the nightly split), `.github/rulesets/main.json` (required checks list; the file is outside the frontend lane, so the orchestrator applies this one), `docs/architecture/quality-gate.md` (1.5.1 to 1.5.3 final text, how to read a failure, how to update baselines, quarantine procedure), `.github/pull_request_template.md` (checklist lines: gallery entry, contract spec, baselines reviewed, bundle numbers), flip the visual and responsive jobs from report-only to blocking at gate G3.
- API: none. Size: 400. Depends on: Q01, Q02, Q03.
- Acceptance: Q11.1 a PR touching only `src/` (API) skips the web jobs. Q11.2 a PR touching `web/` runs them in parallel and finishes in <= 15 minutes on a cold cache. Q11.3 an intentionally broken PR (contrast, budget, axe, visual) goes red in the right job with the right artifact. Q11.4 the triage table is in the repository docs.
- Tests: dry runs on a throw-away branch. Re-check: none; orchestrator confirms branch protection.

#### Q12 Interaction, CLS and Lighthouse budgets

- Goal: the advanced table stays fast, and no screen jumps.
- Files (about 550 e2e + 150 config): `e2e/tests/stub/perf-table.spec.ts` (Chromium, gallery table with 5,000 and 10,000 rows, CPU throttle 4x through CDP: sort click to next paint <= 200 ms; scroll 2,000 px in 20 steps with zero long tasks over 100 ms and at most three over 50 ms; column resize drag keeps the 95th percentile frame <= 33 ms; in continuous mode (A11) DOM rows <= 60 and heap growth < 60 MB), `perf-cls.spec.ts` (registry list screens, Home and editors with 200 ms stub latency: cumulative `layout-shift` <= 0.02, 0 for skeleton-to-content swaps (FB-02)), `perf-nav.spec.ts` (route navigation total blocking time <= 200 ms for Home and lists), `e2e/lighthouserc.json` and the nightly job (`@lhci/cli`, no external server; pages: `/login` and the gallery table; mobile profile: performance >= 0.9, LCP <= 2.5 s, TBT <= 200 ms, CLS <= 0.05, accessibility >= 0.95), numbers recorded in `docs/architecture/quality-gate.md`.
- API: none. Size: 760. Depends on: Q01, A11, Q10.
- Acceptance: Q12.1 budgets pass on main, fail when a deliberate 300 ms synchronous loop is added to row rendering. Q12.2 CLS check runs on every registry list screen. Q12.3 results are artifacts, not commit noise.
- Motion (programme M, N16 to N19): MO-04 and the 5,000-row frame scenario (+60). Respect `data-motion`; tokens only.
- Tests: itself. Re-check: none; power confirms the table "feels" fast at 5,000 rows.

### 1.6 Programme L: Responsive (L00..L09)

L00 comes first and stands apart from the layout system: it is the content rule (nothing the user types may widen the page) and it ships in wave 2, before any table work. Goal of the rest: replace 47 ad-hoc `@media` blocks (two breakpoints, 600 and 1024, repeated per component) by a defined layout system: size classes for the shell and page frame, container queries for components, fluid tokens, explicit touch and pointer rules, and an automated matrix that proves it at eight widths. Today `app-table` switches between table and cards by the viewport (below 1024px), so at a 1024px window with the 15rem sidebar its container is only about 730px wide and the Reports table scrolls sideways; a component in a drawer or a two-column layout has no way to react to its own width.

#### 1.6.1 Size classes (viewport) and container bands (component)

| Size class | Viewport | Shell | Navigation | Page grid |
|---|---|---|---|---|
| compact | < 600 | top bar + bottom tab bar (five cells) | tab bar, More sheet | 4 columns |
| medium | 600 to 1023 | top bar + collapsible rail | rail 72px; expand = overlay with labels | 8 columns |
| expanded | 1024 to 1439 | persistent sidebar 15rem, user can collapse it to the rail | sidebar | 12 columns, content max 76rem |
| large | 1440 to 1919 | sidebar; the document peek docks as a right column (L09) | sidebar | 12 columns |
| xlarge | >= 1920 | same, content frame centred (max 90rem); Home grid up to 3 columns | sidebar | 12 columns |

Landscape phone: `(orientation: landscape) and (max-height: 500px)` uses the rail instead of the tab bar and lets bars flow with the page (the existing `media-short` rule, WCAG 1.4.10). Foldables: unfolded inner screens are `medium` or `expanded` by width; the cover screen (about 280px wide) must not overflow but is not a design target; dual-screen spanning (`horizontal-viewport-segments: 2`) is best effort in Chromium only (L09).

Container bands (width of the component's own container; named containers `ims-page`, `ims-table`, `ims-widget`, `ims-card`, `ims-form`, `ims-grid`, `ims-chart`): `narrow` < 480px, `snug` 480 to 639, `roomy` 640 to 899, `wide` >= 900. Viewport media queries are allowed only in the shell, the page frame (`app-page` style) and for user-environment features (N5).

#### 1.6.2 Grid, spacing and fluid type

| Token | Value | Notes |
|---|---|---|
| `--ims-gutter` | `clamp(1rem, 0.75rem + 1vw, 1.5rem)` | between columns and cards |
| `--ims-page-margin` | `clamp(1rem, 0.5rem + 2vw, 2rem)` | left and right of the page frame; plus the safe-area inset |
| `--ims-section-gap` | `clamp(1rem, 0.75rem + 1.2vw, 2rem)` | vertical rhythm between sections |
| page title | `clamp(1.375rem, 1.2rem + 0.6vw, 1.5rem)` | `font-size(5)` |
| stat value | `clamp(1.75rem, 1.4rem + 1vw, 2rem)` | `font-size(6)` |
| body, inputs, buttons | `1rem` fixed | inputs never below 16px |
| safe area | `--ims-safe-top/right/bottom/left` from `env(safe-area-inset-*)` | `viewport-fit=cover` set in L03 |

Rule: every `clamp()` has a `rem` minimum and maximum and a `rem + vw` preferred value, never `vw` alone, so browser zoom and text enlargement still scale the text (WCAG 1.4.4). Everything is in `rem`; a fixed `height` on a text container is a lint finding (Q07), use `min-height`.

#### 1.6.3 Touch and pointer rules

| Condition | Rule |
|---|---|
| `any-pointer: coarse` (phones, tablets, touch laptops) | targets >= 44px (48px for primary controls), 8px between targets, no action reachable only by hover, `touch-action: manipulation` on controls (no double-tap delay) |
| `pointer: fine` and `hover: hover` | 24px minimum targets (compact density from 1024px), hover tints, row hover; tooltips carry no essential text |
| `hover: none` | nothing depends on hover |
| hybrid (touch laptop) | targets follow `any-pointer` (the safe side); density follows `pointer: fine` (decision C4) |
| keyboard, any device | the focus ring is always visible and never obscured by a sticky bar (WCAG 2.4.11) |
| inputs | `inputmode`, `enterkeyhint` and `autocomplete` set per field; numeric fields use `inputmode="decimal"` |

#### 1.6.4 Zoom and reflow

200% text size: the root font size doubles and nothing clips, overlaps or needs a horizontal scroll at 768 and 1280. 400% zoom (1280px at 400% is 320 CSS px by 256): the layout is the compact class with cards, no horizontal page scroll (a labelled data region may scroll), the focused control is never hidden by a sticky bar (the `media-short` rule makes bars flow). Both are tested in L08.

#### 1.6.5 Automated matrix (summary; L08 builds it)

| Dimension | Values |
|---|---|
| Widths | 320, 360, 390, 768, 1024, 1280, 1440, 1920 |
| Extra viewports | 667x375 and 844x390 (landscape phones), 884x1104 and 280x653 (foldable inner and cover), 320x256 (400% of 1280x1024) |
| Zoom | 200% text (root font size), 400% reflow |
| Media | light, dark, reduced motion, forced colours, contrast more, print |
| Checks | no horizontal overflow outside declared scroll regions, nothing positioned outside the viewport, expected `data-shell` and table `data-mode` for the width, touch target sizes, focus not obscured, text not clipped, CLS |
| Expected modes | 320, 360, 390: tab bar, cards, card line editor. 768: rail, table at the `roomy` band (priority 1 and 2 columns). 1024: sidebar, `roomy` table. 1280 to 1920: sidebar, `wide` table (all columns); at 1440 and above the peek docks |
| Run | pull request: widths 320, 390, 768, 1280 on 6 screens; nightly: everything |

#### L00 Content containment: long values never cause horizontal scroll (v2: new, early; owner-reported defect)

- Defect: the Partners list scrolls sideways because very long unbroken values (a QA test created names of hundreds of repeated characters; the API accepts up to 300 for names and 500 for remarks) stretch the table and then the page. The same cause is NR-10 of the novice retest.
- Rule (N15): content must never cause horizontal page scroll at any width. Only components that are meant to scroll may scroll horizontally: the table's own scroll region, and `pre` / code blocks. They must be focusable (`tabindex="0"`) and labelled (`role="region"` with an accessible name), and they carry `data-scroll-region` so tests can find them.
- Files (about 350 source, 250 tests):
  - `web/libs/shared/styles/src/mixins/_containment.mixin.scss` (new): `wrap-anywhere` (`overflow-wrap: anywhere`, no hyphenation), `clamp-lines($n)` (line clamp with `overflow: hidden`), `truncate-line` (single-line ellipsis with `min-width: 0`; the existing `nowrap-text` mixin becomes an alias), `flex-child` (`min-width: 0`); `web/libs/shared/styles/src/globals/_containment.scss` (new partial, one line in `globals/index.scss`): `html { overflow-wrap: anywhere }` is inherited by all text, replaced elements get `max-width: 100%`, `pre`/`code` blocks scroll inside themselves. No `overflow-x: hidden` or `clip` on `html` or `body` anywhere: hiding the symptom is forbidden, the test must see it.
  - `min-width: 0` on every flex and grid child that holds text: page header title block, card and stat-card bodies, list-row text wrappers, shell top bar user name, dialog and sheet bodies, banner and toast text, form-field messages, product-picker result rows, line-editor product block, tab labels, pager range text.
  - Tables (today's `app-table`; A02 inherits the rules): `table-layout: fixed` with a column rule by `TableColumn.type` (new optional field, default derived from `primary` and `align`): `name` takes the flexible share (`minmax(10rem, 2fr)`), one line, ellipsis; `code` (SKU, document number, barcode, warehouse code) `fit-content` up to 20ch, never wraps, ellipsis beyond; `number` (quantity, money) `fit-content`, right aligned, never truncated; `status` (chip, flags) `fit-content`, never shrinks; `date` `fit-content`, never wraps; `text` (remark, reason) two-line clamp. On phone cards: title two lines, facts one line each, `wrap-anywhere`. A truncated cell carries `title` with the full value as a convenience only (UX 10.1 item 3: no essential information only in a tooltip), and the full value is always reachable without a pointer: on the detail page, in the "Details" disclosure and row expansion (L04, A08), in the peek (A16), and in the "Show more" control below.
  - New `app-clamped-text` (`libs/shared/ui/.../clamped-text/*`): clamps to N lines (default 2) and, when the text overflows, shows a real "Show more" / "Show less" button with `aria-expanded`; used for card titles, page header title and subtitle, remark and reason texts, dialog messages that echo a user value. Chips: single line, max 24ch, ellipsis, `title`. Toast: three-line clamp with the full text kept in the live region. Breadcrumb items (A22) 24ch, current item 40ch; command palette rows (R16) one line each. Dialogs and banners wrap, never clamp an instruction.
  - `UiScrollRegionDirective` (`appScrollRegion`, label input): sets `tabindex`, `role="region"`, `aria-label`, `data-scroll-region`; applied to the existing scrolling region of the Reports table and to any `pre`.
  - Maximum visible length conventions (visible, not stored): names and titles in tables 1 line; headings 2 lines; chips 24 characters; codes 20 characters; toasts 3 lines; remarks 2 lines then "Show more"; anything longer is reachable in full as above.
  - `web/.stylelintrc.json`: a literal `white-space: nowrap` is disallowed (use `truncate-line`, which includes the ellipsis); the 7 existing uses are converted; a PR review rule says the same (4.1 row 16).
  - Tests: `e2e/src/stub/long-values.ts` (generator: 500 characters in six variants: unbroken Latin, Latin with spaces, unbroken Chinese, emoji (including joined sequences), a long e-mail-like token, mixed), `e2e/tests/stub/containment.spec.ts` (Playwright on the Q01 stub harness: Partners, Users, Products, Receipts and Issues, Stock, Low stock, Warehouses, Categories and units, Audit log, Approvals, the Reports table, product edit, and the receipt editor with long product names, remark and partner; plus a confirm dialog and a toast with a long server message; at 320, 360, 768 and 1440; asserts `document.documentElement.scrollWidth <= document.documentElement.clientWidth`, that no visible element outside a `[data-scroll-region]` extends past the right edge, and that every scroll region is focusable and has an accessible name), Spectator specs for `app-clamped-text`, the table (column type classes, `title` on truncated cells, no `nowrap` without ellipsis), status chip, banner, toast, dialog, page header, card and picker rows. jsdom does not lay out, so the Spectator specs prove the markup contract (clamp component present, `title`, `aria-expanded`, type classes) and the Playwright test proves the real widths.
- API: none. Backend item B20 asks for tighter text limits; the UI must still render today's 300-character names and 500-character remarks, and whatever imports and scripts create.
- Size: 600. Depends on: none (uses the Q01 harness from wave 1).
- Acceptance: L00.1 Partners, Users, Products and Receipts lists with 500-character values in all six variants have no horizontal page scroll at 320, 360, 768 and 1440 (the owner's defect). L00.2 the same for the editor, dialogs and toasts. L00.3 every truncated value is reachable in full by keyboard. L00.4 scroll regions are focusable and labelled (axe `scrollable-region-focusable` passes). L00.5 a literal `white-space: nowrap` fails `npm run lint:styles`. L00.6 initial bundle growth <= 2 kB. L00.7 later slices that add a list, card, chip, dialog, toast, breadcrumb, header or palette row add a long-value state to their gallery entry (4.1 row 16) and their screen to the containment test (registry flag `longValues`).
- Re-check: novice (Partners at 360 with the QA names, primary), edge (keyboard reach of "Show more", scroll regions, 400% zoom), power.

#### L01 Layout system foundation

- Goal: define the vocabulary once so the rest of the programme (and A02) can use it.
- Files: `web/libs/shared/styles/src/variables/_breakpoint.var.scss` (adds `$breakpoint-large: 1440px`, `$breakpoint-xlarge: 1920px`, container band widths), `mixins/_media.mixin.scss` (adds `media-large`, `media-xlarge`, `media-landscape-short`; the existing `media-phone|tablet|desktop|short|below-desktop` stay as aliases), new `mixins/_container.mixin.scss` (`container-up(band)`, `container-down(band)`, `container-name(name)`), new `variables/_fluid.var.scss` (the tokens of 1.6.2 and `fluid-size()`), new `globals/_safe-area.scss` (not `_base.scss`, so R07 and L01 in the same wave never edit one file), `web/libs/shared/utils/src/lib/layout/layout.service.ts` (`LayoutService`: signals `$sizeClass`, `$isCoarsePointer`, `$isFinePointer` from `matchMedia`), `web/libs/shared/ui/src/lib/directives/container-width.directive.ts` (`appContainerWidth`: `ResizeObserver` writes `data-width-band` and exposes a signal; used by the table, the lines grid and the tests), `web/docs/conventions/responsive.md` (sections 1.6.1 to 1.6.4 as the rulebook), `web/docs/conventions/scss-design.md` (links it).
- API: none. Size: 800. Depends on: R05.
- Acceptance: L01.1 existing screens look identical at 360 and 1280 (screenshots), no horizontal scroll at 320. L01.2 the new mixins compile and are used by one converted component as the proof (the page header). L01.3 the rulebook states which kind of query is allowed where.
- Tests: Sass compile test for the mixins (the styles library already has a Jest setup), `LayoutService` spec with a `matchMedia` stub, directive spec with a `ResizeObserver` stub.
- Re-check: novice and edge only for "nothing moved".

#### L02 Layout system applied to the screens

- Goal: every screen sits in the same page frame and adapts to its container instead of the window.
- Files: `web/libs/containers/*/src/lib/*/*.scss` (about 20 files; the 47 ad-hoc `@media` uses become container queries or the page-frame rules, the shell excepted), `libs/shared/ui/.../page-header/*.scss`, `form-field` layout helpers, a shared `.ims-page` frame (`container-type: inline-size; container-name: ims-page`, margins and gutters from the tokens, content max 76rem), the filter bars: at the `narrow` and `snug` bands the filters collapse behind one "Filters (n)" button that opens an `app-popover` (sheet height on phone) as `mvp-ux.md` 10.2 asks, search and the Mine/All scope stay visible; forms become two columns from the `roomy` band; the filter-bar gap uses `--ims-stack-gap` (moved here from R12).
- API: none. Size: 1,000. Depends on: L01, R10, A01.
- Acceptance: L02.1 at 320, 360, 390, 768, 1024, 1280, 1440, 1920 no horizontal page overflow on the 16 registry screens (the L08 matrix is the proof once it exists; until then manual). L02.2 at 1920 the content is not stretched past the frame. L02.3 filter bars never wrap into more than two rows at 1024 and collapse at `snug`. L02.4 no `@media (min-width` or `(max-width` is left in `containers/**` (grep).
- Tests: no logic; page-frame component specs where a class is toggled; screenshots reviewed by fe-designer at gate G2.
- Re-check: novice (filters on phone), power, edge.

#### L03 Shell behaviours: tab bar, collapsible rail, sidebar, keyboard

- Goal: the three shells of 1.6.1 behave well, including the on-screen keyboard.
- Files: `web/libs/core/navigation/src/lib/components/shell.component.{html,ts,scss,spec.ts}` (host attributes `data-shell="tabbar|rail|sidebar"` and `data-nav="collapsed|expanded"`; compact: tab bar with `env(safe-area-inset-bottom)`; medium: rail with an expand button that opens the labelled navigation as an overlay over the content (it does not push the page), closes on selection, Esc or outside click and returns focus to the button; expanded and larger: persistent sidebar with a "Collapse" toggle remembered per user in `UserPreferenceStore` (`nav.collapsed`); landscape short phone: rail), `web/apps/main/src/index.html` (`viewport-fit=cover`, `interactive-widget=resizes-content`), `libs/core/navigation/src/lib/keyboard-inset.service.ts` (sets `--ims-keyboard-inset` from `visualViewport` only when the layout viewport did not already resize, so Android Chrome with `resizes-content` and iOS Safari both keep the sticky action bar above the keyboard; the tab bar hides while the keyboard is open, R10), the sticky document action bar uses `bottom: max(var(--ims-safe-bottom), var(--ims-keyboard-inset, 0px))`.
- API: none. Size: 1,000. Depends on: L01, R10, M03.
- Acceptance: L03.1 at 320 to 599 a tab bar, at 600 to 1023 a rail, at 1024 and up a sidebar (read from `data-shell`), and the landscape phone rule. L03.2 the rail overlay is keyboard operable (`aria-expanded`, Esc, focus return, `aria-current`), causes no layout shift, and under reduce motion does not slide. L03.3 the collapse state survives reload for the same user only. L03.4 manual device check (release checklist of Q03): with the keyboard open on iOS Safari and Android Chrome the focused field and the action bar are visible. L03.5 `aria-label`s and counts (badge equals list total) unchanged.
- Tests: shell spec per size class (`matchMedia` stub), nav-collapse persistence, keyboard-inset spec with a `visualViewport` stub; stub e2e `shell-modes.spec.ts` at 360, 768, 1280.
- Re-check: novice (primary), power, edge.

#### L04 Table-to-card by container width, column priorities

- Goal: a clear, testable rule for which columns survive at each width, driven by the table's own container.
- Rule (the contract): each column has a `priority`. Priority 1 = identifier and status (never hidden; in a card they are the title line and the chip). Priority 2 = key facts (shown from the `roomy` band; in a card up to three of them form the fact line). Priority 3 = details (shown only at the `wide` band; below that they are reachable through a "Details" disclosure on the row, which lists the hidden columns as label/value pairs; never shown on a card line). Bands of the table's container: `wide` (>= 900px) shows all columns; `roomy` (640 to 899) shows priority 1 and 2; below 640 the same data renders as cards (the two-line card of NV-08, 48px target). Data-dense report tables opt out with `narrow: 'scroll'`: they keep one labelled, focusable horizontal-scroll region with nowrap identifiers (Reports movements, ledger, audit), as required by R03. Columns removed by `columnWhen` are absent in every band, never blank.
- Files: `table/*` (band logic: the table computes `--cols-wide` and `--cols-roomy` grid templates and CSS selects by container query; `data-mode` from `appContainerWidth`; the generic hidden-columns disclosure), the column definitions of the table-hosting pages with their priorities (initial assignment, fe-designer confirms; the builder reads the real columns): Receipts/Issues P1 No, Status; P2 Partner, Warehouse, Qty, Date; P3 Amount (`cost.view`), Created by. Stock P1 Product, On hand, Status; P2 Warehouse; P3 Category, Safety, Value (`valuation.view`). Low stock P1 Product, On hand, Receive action; P2 Shortage, Warehouse. Approvals P1 No, Flags; P2 Type, By, Waiting; P3 Amount (`cost.view`). Products P1 SKU, Name, Status; P2 Category, Unit; P3 Barcode, Safety. Partners and Users P1 Name, Status; P2 Type or Role; P3 contact and warehouse columns. Reports movements, ledger, audit: scroll mode.
- API: none. Size: 900 (about 400 table, 250 column definitions across 14 pages, 250 specs). Depends on: L01, A02.
- Acceptance: L04.1 at container widths 1000, 800, 639 the visible columns are exactly the ones in the rule (read through `getComputedStyle` and the accessibility tree: hidden columns are removed from it). L04.2 identifier and status are visible at every width; P3 values stay reachable by the Details disclosure by keyboard. L04.3 at a 1024 viewport (container about 730px) Receipts shows priority 1 and 2 without horizontal scroll; Reports movements scrolls inside its region, SKU and date on one line (R03 stays true). L04.4 phone cards are unchanged (NV-08, TB-04).
- Tests: model spec (grid templates per band, priority defaults), disclosure spec, per-page column spec (priorities present); stub e2e in the L08 matrix (modes per width).
- Re-check: power (primary: does 1024 feel better?), novice (cards), edge.

#### L05 Touch and pointer rules, text zoom and reflow fixes

- Goal: make 1.6.3 and 1.6.4 true everywhere, using the failures the L08 matrix reports.
- Files: global rules (`globals/_base.scss`: `touch-action: manipulation` on controls, `overscroll-behavior: contain` in popover lists, drawers and the lines grid, tap highlight), component SCSS fixes found by L08 (an estimated 300 lines across 20 files: target sizes, fixed heights turned into `min-height`, `line-clamp` on essential text removed, clipped chips and tab labels at 200% text), `inputmode`/`enterkeyhint`/`autocomplete` audit of every form, removal of any hover-only affordance found by the audit (grep for `:hover` that reveals content).
- API: none. Size: 600. Depends on: L01, R08, L08.
- Acceptance: L05.1 L08 reports zero overflow and zero clipped text at 200% text size on the 16 screens at 768 and 1280, and zero horizontal page scroll at the 400% case. L05.2 every interactive element on coarse-pointer projects is >= 44px or has a >= 44px hit area, and primary buttons are 48px. L05.3 no `KNOWN_RESPONSIVE` entry referencing L05 remains. L05.4 numeric fields open the numeric keyboard on a phone (`inputmode`).
- Tests: the matrix; a lint check that no template combines `(mouseenter)`/`(mouseover)` with revealing content.
- Re-check: novice (primary), edge (zoom).

#### L06 Forced colours and high contrast

- Goal: the app is usable in Windows high contrast and with `prefers-contrast: more`.
- Files: new `globals/_forced-colors.scss` and `globals/_contrast-more.scss` (partials, `_themes.scss` is not edited), at most six component edits: buttons, chips, checkbox/radio/switch indicators, selected row/tab indicators, skeleton and spinner (`ButtonBorder`/`CanvasText` fallbacks), focus ring (`outline` with a system colour, never `box-shadow` alone), charts get `forced-color-adjust: none` with system colours and patterns (A19 consumes this), icons use `currentColor`; `prefers-contrast: more` raises borders (`border` to `border-strong`) and muted text to full text through custom properties.
- API: none. Size: 600. Depends on: R05, R13.
- Acceptance: L06.1 under Playwright `forcedColors: 'active'` every registry screen keeps visible borders on buttons, inputs, chips, cards and dialogs, a visible focus ring, and status chips keep icon + text. L06.2 under `contrast: 'more'` borders and muted text pass 7:1 where the token allows (script check on computed colours). L06.3 no content is lost (screenshot comparison on two screens in both modes).
- Tests: stub e2e `forced-colors.spec.ts` (computed-style assertions, screenshots at 360 and 1280), CSS lint rule: no `border: 0` on a control. Re-check: edge (primary).

#### L07 Print stylesheets for receipt and issue

- Goal: a document prints on A4 cleanly, with 2-decimal money and totals, and with the same permission rules.
- Files: `globals/_print.scss` (`@media print`: hide the shell, bars, tab bar, toasts, banners, action bar, breadcrumbs, filters; black on white by forcing the light tokens; `@page { size: A4; margin: 14mm }`; `print-color-adjust: exact` only for status borders; link URLs not appended), `containers/documents/.../document-editor` print block `document-print/*` (screen: hidden; print: title and number, status, warehouse, partner, remark, created/submitted/approved/posted with names and times from the timeline mapper if A17 exists, otherwise from the DTO; a lines table with No., SKU, Product, Quantity with unit, and, with `cost.view`, Unit cost (RM) and Amount (RM) with exactly 2 decimals and thousands separators; totals row: total quantity and, with `cost.view`, total amount (RM); "Estimated" mark for an unposted issue (`amountIsEstimate`); Draft shows "DRAFT"; signature boxes "Prepared by" and "Approved by" with printed names; footer "Printed by {display name} on {dd MMM yyyy HH:mm}"), a Print button in the document actions (icon appended: `printer`) calling `window.print()`, header row repeats on each page (`thead { display: table-header-group }`), rows do not split (`break-inside: avoid`). The print block reads `$values()` and the DTO once (no second source of truth).
- API: none. (A company name or address on the printout is open question P5; none is shown by default.)
- Size: 700. Depends on: R14, R04.
- Acceptance: L07.1 Playwright `emulateMedia({ media: 'print' })` at 794px wide shows only the document block with no shell, no buttons. L07.2 Staff prints without any cost or amount, Clerk with cost, nobody gets valuation. L07.3 money cells match the screen exactly (`12,400.00`), quantity keeps its precision. L07.4 60 lines flow over pages with the header repeated (visual check in the print-emulated screenshot and a manual PDF check; automated page counting is not attempted). L07.5 dark theme prints light.
- Tests: print block spec per permission, number formatting spec; stub e2e `print.spec.ts` (emulated print screenshot of a 5-line receipt, PDF text contains the totals).
- Re-check: power (primary), novice (print from phone: the menu path works), edge.

#### L08 Responsive test matrix automation

- Goal: section 1.6.5 as code, so L02 to L05 are provable and regressions are caught.
- Files (about 550 e2e + 250 helpers): `e2e/tests/stub/responsive.spec.ts` (registry x widths; assertions: no horizontal overflow outside `[data-scroll-region]`, no fixed or sticky element outside the viewport, `data-shell` and table `data-mode` equal the expected mode of the width (single table in `e2e/src/layout-expectations.ts` mirroring 1.6.1), touch-target sizes on `hasTouch` projects, focus not obscured while tabbing 15 times, no clipped text (elements whose `scrollWidth` exceeds `clientWidth` without `text-overflow`)), zoom cases (root font size 200%; viewport 320x256 for 400%), landscape and foldable viewports, `KNOWN_RESPONSIVE` (screen|width|rule -> finding id + expiry), pull-request subset and nightly full set, a runner script that prints a matrix table in the job summary.
- API: none. Size: 800. Depends on: Q01, L01.
- Acceptance: L08.1 on the first run the failures are either fixed or listed in `KNOWN_RESPONSIVE` with a finding id and an owner slice (L02 to L05 remove them). L08.2 a deliberately widened element (`width: 400px`) fails at 320 and 360. L08.3 the PR subset runs in under 3 minutes.
- Tests: itself. Re-check: edge reviews the first matrix report.

#### L09 Large screens, foldables, docked drawer

- Goal: use the space at 1440 and 1920 and survive foldables.
- Files: `app-drawer` (A16) gains `docked` mode: at the `large` size class the peek is a non-modal right column inside the page frame (the list narrows, so its container query moves it from `wide` to `roomy` by itself), focus order list then peek, Esc closes it; `.ims-page` max width 90rem at `xlarge`; Home grid up to 3 columns when its container is >= 1400px; `@media (horizontal-viewport-segments: 2)` and `env(viewport-segment-*)` keep the page frame inside one segment (Chromium dual-screen only, graceful no-op elsewhere); landscape-phone and cover-screen notes in `responsive.md`.
- API: none. Size: 500. Depends on: L03, A16.
- Acceptance: L09.1 at 1440 and 1920 opening a peek keeps the list visible and usable; the table switches band without a reload. L09.2 at 1920 no screen has text lines longer than 90 characters. L09.3 the dual-segment rule is behind the media query and has no effect in Firefox and WebKit. L09.4 spanning is documented as untested in CI (manual only).
- Tests: drawer docked spec, stub e2e `large-screen.spec.ts` at 1440 and 1920. Re-check: power.

#### 1.6.6 What goes into `docs/architecture/frontend.md` (done in R24, drafts earlier)

New sections: the CDK policy (N2) and the popover/overlay rule; the table model (columns, priorities, views, URL state, paging modes); the layout system (1.6.1 to 1.6.4 summary and link to `responsive.md`); the quality gate (link to `docs/architecture/quality-gate.md`); error handling and offline (Q05, Q06 decisions: retry rules, never queue commands); preferences (`core-preferences`, keys); the gallery and the rule "every component has a gallery entry and a contract spec". The decisions list in section 5 of that file gets the new rulings N1..N12 as items 14 to 25.

### 1.7 Programme M: Motion (M01..M08, plus growth of 20 slices)

Source: `docs/design/2026-refresh-motion.md` (fe-designer; the owner accepted every default of its open questions M1..M8: in-app "Animations: follow device / reduce" setting, press scale 0.97 and 2 px hover lift, count-up of Home integers, shared-element transition behind a flag, pull to refresh, dialog overshoot as proposed, sign-in entrance on every load, automatic Lite mode). Its section 7 is applied here. It supersedes the motion rulings C1, C2, C3, C7 (durations), C13 and the motion lines of D8, the visual file section 4, UX 7.6 and UX 11 item 3, and login visual section 11, by rulings N16 to N19 (0.2). Colour, contrast, targets, density and cost visibility are unchanged.

What changes for implementers, in one list: skeletons shimmer (a finite sweep, 6 sweeps) instead of pulsing; buttons scale to 0.97 on press and fade a hover overlay in 120 ms (table rows and focus rings stay instant); dialogs spring, sheets and drawers slide; routes and the theme switch use view transitions; one disclosure may animate height through `grid-template-rows`; counts on Home count up once; the top progress bar trickles and completes instead of looping; everything is controlled by `data-motion` = full, lite, reduced or off, and reduced can only be requested, never bypassed. `@angular/animations` (deprecated in Angular 22) is removed; no animation library is added.

Limits that every motion slice obeys (motion file H1..H6): honour `prefers-reduced-motion`; animate `transform` and `opacity` only (named exceptions: `clip-path` reveals and one `grid-template-rows` disclosure); functional transitions 300 ms at most and entrances 400 ms at most; no layout shift; no looping decoration; nothing per row animates except the first 8 rows on first reveal. Budgets: motion code at most 8 kB raw added to the initial total (the target after R24 moves from 575 to 583 kB raw; warning 600 and error 650 do not change); global styles cap moves from 9 to 11 kB; lazy parts at most 1.5 kB each (section 4.3).

#### 1.7.1 Slices that grow

| Slice | Growth | New size | What it adds (section 7.1 of the motion file) |
|---|---|---|---|
| R05 | +60 | 660 | motion tokens (CSS properties and SCSS functions, Reduced and Off blocks, `shine`) |
| R07 | +100 | 450 | shimmer replaces the pulse (6 sweeps then static), `ims-loop-*` names, spinner only while busy, reduce-motion block uses the token mapping |
| R08 | +150 | 900 | button hover overlay, press 0.97, three-layer label stack, busy, success morph and tick, shake token; checkbox, radio, switch micro-interactions; focus underline; the stylelint transition rule becomes the allow-list of the motion file 4.1 |
| R09 | +200 | 500 | sliding indicator for tabs and the segmented control (uses M02, new dependency), badge bump, chip swap pop, stat-card lift |
| R10 | +120 | 520 | tab bar pill and icon pop, sidebar indicator, markup for the top-bar progress trickle (component in M03) |
| R11 | +80 | 930 | toast `animate.enter` / `animate.leave`, FLIP of the survivors, `inert` while leaving |
| R13 | +250 | 850 | dialog, sheet, drawer-ready CSS (`@starting-style`), scrim, sheet swipe handle, banner disclosure, empty-state entrance (new dependency M01) |
| R14 | +80 | 780 | row highlight variants (user edit, data update), error-summary disclosure |
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
| Q10 | +60 | 760 | gallery motion-mode switch and "replay" per component |
| Q12 | +60 | 760 | MO-04 and the 5,000-row frame scenario |

Unchanged on purpose: A02 and A03 (at the cap; their motion is M06), A04, A05, A19 (draw-in is in A20), R17 and R19 (Home motion is M04), R25 (entrance is M05), R06 (theme crossfade is M03), Q01 to Q09, the L slices (L06 re-checks overlays under forced colours as part of its scope). Each grown slice has a "Motion" bullet in its block saying so; the sizes in the master list include the growth.

#### M01 Motion foundation

- Files: `libs/shared/styles/src/globals/_motion.scss` (keyframes `ims-rise|fade|pop|leave|shake|flash|wipe|loop-shimmer|loop-spin`, utility classes, the `disclosure` mixin, view-transition rules, mode blocks), `mixins/_motion.mixin.scss` (`hover-overlay`, `press`, `elevation-overlay`, `disclosure`), `MotionService` in `libs/shared/utils/src/lib/motion` (signal `mode`: full, lite, reduced, off; reads `matchMedia`, the stored Lite flag and `ims.motion`; writes `data-motion` on `<html>`), `theme-init.js` reads `ims.motion` before first paint (about 90 bytes), the Lite guard (automatic on weak devices, stored locally, no setting), `MAX_ANIMATION_TIMEOUT` provider (500 ms), removal of the unused `@angular/animations` dependency, `web/scripts/check-motion.mjs` and the stylelint allow-list for animated properties (MO-02), `web/docs/conventions/motion.md`.
- API: none. Size: 650. Depends on: R05, R07. Lane: Y (styles) and S for `theme-init.js`.
- Acceptance: MO-02 (no stylesheet animates a property outside the allowed list; no `will-change`; no `infinite` except `ims-loop-spin`); `data-motion` is set before first paint; Reduced cannot be overridden towards more motion; initial growth <= 3 kB; `@angular/animations` is gone from `package.json` and the lockfile (the lockfile change is this slice's only one besides A01).
- Tests: `MotionService` spec with a `matchMedia` stub (reduced, Lite flag, `off` wins), script fixture tests. Re-check: edge.

#### M02 Motion primitives

- Files: new `libs/shared/ui/src/lib/motion/*`: `app-disclosure` (grid-rows wrapper with `inert` when closed), `imsReveal` (one class bound to a signal, 600 ms ceiling, first 8 items), `imsIndicator` (tab, segment, nav, tab bar), `flip()` and `imsFlip`, `countUp` (with a sizer span so nothing shifts), `imsEnter` / `imsLeave` helpers, `provideMotion` test provider, specs for each.
- API: none. Size: 900. Depends on: M01. Lane: none (new folder).
- Acceptance: in Reduced/Off every directive sets the final state synchronously and never calls `Element.animate`; the count-up tile's accessible name contains the final number from the first frame (MO-07); no primitive is used inside a virtualised body.
- Tests: Spectator with `provideMotion('reduced' | 'off' | 'full')` per primitive. Re-check: edge.

#### M03 Route and theme transitions, top bar

- Files: `withViewTransitions` in `app.config.ts` with `onViewTransitionCreated` rules (skip same-route, Lite, hidden tab), chrome `view-transition-name`s (top bar, nav and tab bar stay put), fallback CSS entrance, `ThemeService` view-transition wrapper (crossfade 200 ms, no flash), one trickle progress component replacing `shell-progress` and the table progress bar (appears after 300 ms, creeps to 80 percent, completes in 200 ms, no loop), shared-element transition behind a flag (Could, kept only if clean on the low-end phone), the route `h1` receives focus before the transition ends.
- API: none. Size: 650. Depends on: M01, R06, R10. Lane: S (before L03).
- Acceptance: MO-06 (the route `h1` has focus before the transition ends; a click during the transition works); the theme switch has no flash and DK-03 stays true; no loop in the top bar (MO-08); a stub e2e checks focus and input during a transition.
- Tests: service and component specs; stub e2e `route-transition.spec.ts`. Re-check: edge, novice.

#### M04 Home motion

- Files: widget frame reveal, stat tile count-up (integers only, once per load, 400 ms, skippable, never money) and hover lift (`(hover: hover) and (pointer: fine)`), later profiles (R19) inherit it from the frame; no change to data code.
- API: none. Size: 400. Depends on: M02, R17. Lane: H.
- Acceptance: money never counts up (FR-05); Reduced shows the final numbers at once; the tile's accessible name is final from the first frame. Re-check: novice, power.

#### M05 Sign-in motion

- Files: entrance table of the motion file 3.13 (every load, 360 ms, never blocks typing), failed sign-in disclosure and one shake, panel fade; LG-47 (no running animation after settle) stays true.
- API: none. Size: 300. Depends on: M02, R25. Lane: U.
- Acceptance: the user can type during the entrance; one shake per failed sign-in; Reduced has none. Re-check: novice, edge.

#### M06 Table, list and editor motion

- Files: sort chevron, row flash on a refetch change (at most 10 rows, never on first load), FLIP for editor lines and filter chips, column drag feedback on the CDK drag-drop, keyboard reorder announcement, first-reveal class on list pages (first 8 rows only).
- API: none. Size: 700. Depends on: A03, A04, R14, M02, R30, A15 (lanes T and E, serial after both). Lane: T.
- Acceptance: with 5,000 rows nothing per row animates beyond the first 8 (Q12 scenario); MO-05 frame gate at 4x throttle; Reduced applies the static tint only. Re-check: power, edge.

#### M07 Motion test harness

- Files: `e2e/src/support/motion.ts` (`settle`, CPU throttle, frame-pacing and INP probes), Playwright projects `motion-full` and `motion-reduced`, the kill-switch fixture (`ims.motion=off` for visual and functional tests), six gate scenarios, specs MO-01 to MO-09, report artefacts.
- API: none. Size: 600 (about 450 e2e, 150 tooling). Depends on: Q01, M01. Lane: Q.
- Acceptance: MO-01 (reduced: no running transform animation, loops stopped), MO-03 (zero animations after a registry screen settles), MO-05 (frame, long-task and INP gates; report-only until gate G3, then blocking), MO-09 (the check script prints the keyframe names for the motion gate review). Re-check: none; fe-designer checks on a low-end Android phone and an iPhone at G1.

#### M08 Pull to refresh on phone lists (Could)

- Files: gesture directive on the three phone lists, scroller `overscroll-behavior`, text fallback ("Pull to refresh" has a button equivalent), spec.
- API: none. Size: 350. Depends on: R15, R30, M02. Lane: T. First to cut.
- Acceptance: a refetch keeps the old rows (R15 rule); the gesture never fires inside a horizontally scrolling region; the button path works without touch. Re-check: novice.

Motion acceptance and tests: MO-01 to MO-09 are defined in the motion file 6.4; the definition of done (4.1) gets a motion row. Motion gates: G1 adds a fe-designer check on a real low-end Android phone and an iPhone; MO-05 turns blocking at G3.

### 1.8 Programme X: Access, roles and permissions (RP00..RP11)

Sources: `docs/design/roles-permissions-ux.md` (ui-ux-designer: screens S53 Roles list, S54 role editor, S57 person's access page, safety UX, acceptance, Playwright scenarios 40 to 45) and `docs/architecture/adr-roles-permissions.md` (be-architect: model, API, backend slices RP-0 to RP-9). Frontend slice ids are written `RP01`; backend slice ids of the ADR are written with a hyphen, `RP-3`. About 9,900 lines in 12 slices: the UX file's RP01 to RP10 are 9,200; RP11 (400) is the Settings approval-tier form that the ADR lists as web slice P04; RP00 (300) is the small permission-derived profile helper that the roadmap calls "P01-lite" (ADR web slice P01) and that must exist before R10 so that no new role-name constant is ever written. A new lane **X** owns `libs/containers/administration/src/lib/roles/**`, the person's page under `.../users/**` and `libs/features/administration/**`; it shares the shell only for one menu item (RP01) and consumes `app-table`.

#### 1.8.1 Reconciliation: where the UX file and the ADR differ, the ADR wins

| # | UX file said | ADR decides | What the web does |
|---|---|---|---|
| 1 | "Every warehouse" is a setting of the role (D3, Q8) | Per person: a flag `AllWarehouses` on the user (migrated to true for today's Admin and Owner users); roles carry no warehouse setting | No warehouse option in the role editor and no column in the Roles list. The person's Warehouses card has a switch "Can work in every warehouse" (High impact, reason required, audited, read-only on your own page) and the list of assigned warehouses. The Access preview says "Works in: the warehouses assigned to each person". UX Q8 is closed |
| 2 | An "approval level" control appears when Approve is ticked (3.6) | Two approval tiers are two permissions: `document.approve` (standard documents) and `document.approve.large` (large ones). The Owner holds only `.large` by default | No level control. The checklist shows two rows with plain sentences. The Settings approval-rule form picks a tier (RP11) and lists the roles that can approve it. Home profile, inbox and menus accept either permission (`approveAny`), which R10, R17 and R19 already need because the Owner's list changes with RP-4a |
| 3 | Cost and valuation visibility is a fixed rule (D10, `mvp-ux.md` 1.4) | Configurable: `cost.view` and `valuation.view` are catalog permissions, flagged dangerous; granting one needs an administrator, a reason, and writes an audit entry. System-role defaults are unchanged (Staff none, Clerk cost, Supervisor, Owner and Admin cost and valuation) | The UI keeps deciding by permission, never by role name (D5). Tests and e2e `13-cost-visibility` run on the system roles with their defaults. The Roles screens show permission names and sentences, never amounts. The Access preview shows "Can see unit costs: Yes/No; stock value: Yes/No" |
| 4 | 18 permissions, proposed names (`roles.read`, `roles.manage`, `users.overrides`) | 21 permissions in 5 groups; the catalog comes from `GET /api/permissions` (code, group, dangerous, requires); there is no `users.overrides`: exceptions are managed with `users.manage`, by administrators | The web renders the catalog it receives; words live in the web catalogs as `perm.<code>`, with the API description as fallback. One flag "High impact" (the ADR's dangerous flag) replaces the UX file's two flags "High impact" and "Sensitive"; the Cost and value group header carries the sensitive note |
| 5 | Exceptions: reason always, expiry optional | A grant always ends (at most 180 days); a take-away may be permanent; not on your own page; never on the administrability core; dangerous permissions only by an administrator | Expiry is required for "give" (7, 30, 90, 180 days or a date up to 180) and optional for "take away" |
| 6 | Live change by a header or a 30-second check | `X-Permissions-Version` on every response, `permissionsVersion` in `/api/auth/me`; deactivation answers 401; a demotion keeps the session; old rights stop within seconds (closes ER-15) | Header first; while the tab is visible and no request has run for 30 s the web reads `/api/auth/me`. Banner "Your access was changed" with [Refresh my access] |
| 7 | A dry-run for every access-changing write (4 in section 10) | Impact preview is the optional backend slice RP-9; role writes return the diff and 409 codes (`last_administrator`, `own_access`, `escalation`, `admin_role_locked`, `role_in_use`) | RP-9 (impact preview and Reset to standard) is promoted from optional to required for RP04. Fallback if it is late: the count comes from the role's people list and the person-level messages come from the 409 codes after the attempt |
| 8 | Admin role fully locked | Admin editable except the administrability core (`users.read`, `users.manage`, `roles.read`, `roles.manage`), which it always holds | The editor shows the four core ticks locked with the reason "Needed to administer the system"; the rest of Admin is editable by an administrator |
| 9 | Role name 2 to 40 characters; role cap 50; exception cap 20 | Name 2 to 60, description 200; caps not fixed | The form follows the API's messages; no client-side cap beyond the length |
| 10 | Audit filters for Role and User entities (BE-E) | Row-level audit exists for Role, RolePermission and overrides; explicit events (`role.permissions-changed`, `user.role-changed`, `user.override-set`, ...); no filter by entity type is planned | Backend item B23 (below). RP09 is built when it exists; the sentences use the ADR event names |

Roles and permission words in the web must never compare a role name: `PHONE_TABS_BY_ROLE`, `ALL_WAREHOUSE_ROLES`, `ROLE_LABELS` and `approval-visibility.ts` go away in RP01 (system roles keep translated display names through their stable `code`). Until RP01 lands, R10 and R17 must use one exported permission-derived helper and add no new role constant (ADR 7.4).

#### 1.8.2 Backend dependencies (ADR slices RP-0 to RP-9) and when they are expected

The ADR's own estimate is 10 backend slices, about 137 agent-hours, about 13 working days on the critical path with two backend agents. RP-5 to RP-7 are public endpoints and are built after the GUID flip PR-7 (the GUID ADR is Accepted: hybrid, a random public GUID on 9 tables), so their ids are strings from the first line. The roadmap (`docs/pm/ROADMAP.md` 1 and 3.4, conflict C10) goes further: RP-3 onward rewrites the same handlers, `UserInfo` and policies as the id switch, so RP-3 to RP-9 reach `main` only after PR-9; only RP-0 to RP-2 (no wire change) may merge earlier. The roles ADR is still "Proposed"; the plan builds against its recommended defaults (roadmap O2) and the owner's answers change copy, not structure.

| Backend slice | Gives the web | Expected on main by wave | Needed by |
|---|---|---|---|
| RP-3 (access snapshot, `X-Permissions-Version`, `roleName`, `seesAllWarehouses`, `permissionsVersion` additive fields, 401 on deactivation; after PR-9) | derived profile inputs, live rights change | 12 | RP01, RP07 |
| RP-4a (permissions from the database) | the Owner's `document.approve.large` in the permission list | 15 | RP01 (tolerates both until then), R17, R19 |
| RP-4b (two approval tiers, `AllWarehouses`) | the approval-rule tier, the per-person flag | 18 | RP11, RP05 |
| RP-5 (permissions list, roles list and detail, create/copy, update, deactivate, delete, users of a role; after GUID PR-7) | catalog and roles | 15 | RP02 |
| RP-6 (replace role permissions with diff, closure, containment, last-admin lock) | role editor writes, 409 codes | 18 | RP03, RP07 |
| RP-9, promoted to required (impact preview, Reset to standard; the roadmap notes about 600 lines and 10 agent-hours that are not in the ADR's 137, conflict C8) | dry-run counts, reset | 19 | RP04 |
| RP-7 (overrides, `effective-permissions`, expiry, exceptions count) | person's page, exceptions | 20 | RP05, RP06, RP08 |
| B23 (audit filter by entity type Role and User, reason field) | history links | 21 | RP09 |

Expected waves follow the roadmap's slots (BE-1 the id and schema lane, BE-2 the feature lane: GUID PR-1 to PR-9, document corrections BE1/BE2, then roles RP-3 onward after PR-9). The master list shows the first wave each frontend slice may start. With one or two agents these dates slip; the scheduler is greedy, so a slice whose backend is late gives its slot to the next one in the order of 2.1.

#### RP00 Permission-derived profile helper ("P01-lite", v2: new, from the roles ADR 7.4 and the roadmap)

- Goal: before the tab bar (R10) and the Home profile are rebuilt, remove the role-name maps from the web so that no new role constant is written. Works with today's API: `GET /api/auth/me` already returns `permissions[]`.
- Files: `libs/core/identity` (a pure `profileOf(permissions)` helper: Home profile, phone tabs, mine-first lists, `approveAny` = `document.approve` or `document.approve.large`, `seesAllWarehouses` read from the user when the API sends it and otherwise derived exactly as today), `libs/core/navigation` (`PHONE_TABS_BY_ROLE` and `ALL_WAREHOUSE_ROLES` replaced by the helper; the role switch in `approval-visibility.ts` replaced), specs.
- API: none. Size: 300. Depends on: none. Lane: S (before R10).
- Acceptance: the five system roles get exactly today's tabs, Home profile and list defaults (table test); a user with a custom permission set gets tabs from permissions; the Owner (who will hold only `document.approve.large` after RP-4a) still sees Approvals; no role-name literal is added.
- Tests: table test for the five roles and for a custom set. Re-check: novice (phone tabs), edge.

#### RP01 Access foundations

- Files: `libs/features/administration` (permission catalog store from `GET /api/permissions` with a bundled fallback of the 21 codes taken from the OpenAPI enum `PermissionCode`; pure rules: dependencies (`requires`), bulk rule, diff, `describeAccess`; i18n keys `perm.<code>` for the catalog), `libs/core/identity` (`PermissionService` fields `roleName`, `seesAllWarehouses`, `permissionsVersion`; removal of the remaining `ROLE_LABELS` role map after RP00 removed the other maps; display names of system roles keyed by `code`), menu item and group relabel (Administration: Users, Roles, Settings, Audit log, gated by `roles.read`), route guards `requirePermission('roles.read')` and `roles.manage`, e2e `Role` type in `e2e/src/config.ts` and `06-roles` re-keyed to permission sets.
- API: RP-3 (additive fields); catalog endpoint from RP-5 when it exists (fallback bundled); client batch CS5, first part. Size: 800. Depends on: R06, R10, R04, RP00.
- Acceptance: no role-name literal remains in `web/libs` except display names of system roles keyed by `code` (grep test); the Owner (only `.large`) still sees Approvals, Home and the inbox; a custom role gets its phone tabs from permissions; a catalog contract test fails when the API adds a code with no translation.
- Tests: rules table test for every code, profile function spec per permission set, guard specs; update `06-roles`. Re-check: edge, novice (phone tabs), power. Review: security-engineer (permissions drive chrome).

#### RP02 Roles list

- Files: Roles list (S53) with `app-table` (name, people, permissions, changed-from-standard marker, last changed), filters, row menu, Add role dialog (blank, from a standard set, copy of an existing role), retire, delete, empty and error states; row actions carry the role name (ER-10); a role editor route stub. Words per UX section 7.
- API: RP-5. Size: 900. Depends on: RP01, R08, R13, A02, A04, A01.
- Acceptance: a role with people offers Retire, not Delete, with the reason in text (`aria-describedby`); system roles cannot be deleted or retired; every disabled control shows its reason; Roles is absent for a user without `roles.read` (menu, palette, deep link gives Forbidden); ER-04 focus and server-error mapping on the dialog.
- Re-check: power, edge.

#### RP03 Role editor core

- Files: details card, `app-permission-groups` (one component for edit mode and read mode, shared with the person's effective list: fieldsets, tri-state group box, flags, dependencies, bulk rule, locks including the Admin core), dirty state, unsaved-changes guard (reuses R26 for a session ending), save, stale banner (`If-Match`), special roles (Admin core locked, standard roles with "Changed from standard", retired, view only for `roles.read` without `roles.manage`).
- API: RP-5, RP-6. Size: 1,100. Depends on: RP02, R11, R14, R26.
- Acceptance: ticking a permission ticks what it requires and says so; unticking a required one is blocked with the reason; the four core ticks of Admin are locked; the form never loses typed work on a session end (R26); ER-03 and ER-04 behaviour.
- Re-check: edge (primary), power.

#### RP04 Role editor panels, Review and save

- Files: Access preview (sentences such as "Can see unit costs: Yes, stock value: No"), People with this role, Changes (diff, with Undo of an unsaved edit), template apply and Reset to standard, Review and save dialog with the impact preview and the reason rules for dangerous permissions.
- API: RP-6, RP-9 (required). Size: 1,100. Depends on: RP03.
- Acceptance: the dialog lists every change and the number of people affected before the call; a dangerous permission needs a reason; Reset to standard restores the catalog defaults and is audited; nothing offers Undo after saving.
- Re-check: power, edge, novice (phone sheet).

#### RP05 Person's access page

- Files: tabs, Role card and Warehouses card with the live "difference" line, the "Can work in every warehouse" switch (High impact, reason, not on your own page), Profile tab moved out of the dialog, create-user dialog update, Users list columns (role, exceptions count) and filters, Effective permissions list with the source of each permission (role or exception) using `app-permission-groups` in read mode, the drawer pattern of A16 for "what does this role allow".
- API: RP-4b, RP-7. Size: 1,150. Depends on: RP03, A16.
- Acceptance: an administrator can add a role, set its permissions, assign it to a person and see the effective list; your own page is read-only for role, scope and exceptions with the reason; a person's role list comes from the API (custom role names are user text and are not translated).
- Re-check: power (primary), edge, novice.

#### RP06 Exceptions

- Files: exceptions table, Add exception dialog (give or take away, permission, reason 5 to 200 characters, expiry: required for give, up to 180 days; dependency and redundancy rules; High impact second step), banners and chips, Expired section.
- API: RP-7. Size: 850. Depends on: RP05, R08.
- Acceptance: a give without an end date is blocked with the reason; nothing can be given that the actor does not hold; the dangerous second step needs an administrator; expired exceptions are shown in their own section.
- Re-check: edge, power.

#### RP07 Safety rules and live rights change

- Files: SF-01 to SF-13 messages and the mapping of 409 codes, own-page read-only, permission-version header handling and the 30-second fallback, the "Your access was changed" banner with [Refresh my access] (no modal, no sign-out, polite), `permissions_changed` and 403 handling, the Forbidden sentence, the account-disabled path (a deactivated user is told on the next call), palette and menus re-read permissions on a version change.
- API: RP-3, RP-6. Size: 850. Depends on: RP05, R11, R26, R10. Must ship before the first release of the Access programme.
- Acceptance: a demotion removes menu entries and buttons within seconds without losing the open form; deactivation shows the sign-in page with the reason; the last administrator cannot be removed by any path; escalation attempts show the reason and are audited by the server.
- Re-check: edge (primary: lockout paths, escalation), power.

#### RP08 Compare (Could)

- Files: `app-access-compare` (ARIA grid), role compare page, person versus role and person versus person, differences only; phone shows two items at most.
- API: RP-5, RP-7. Size: 900. Depends on: A02, A04, RP05. Cut early.

#### RP09 History links, Activity tab, audit sentences

- Files: History links, Activity tab, audit sentences per the ADR event names, palette entries, zh and ms sweep, docs update.
- API: B23. Size: 650. Depends on: RP05.

#### RP10 Roles and access e2e

- Files: real-API scenarios 40 to 45 of the UX file (written slice by slice, finished here), stub cases, page objects, axe and phone matrix registry entries (including long role names and long people names for L00).
- API: none. Size: 900. Depends on: RP07. Built by behavior-test-engineer.

#### RP11 Approval tier in the Settings rule form

- Files: `containers/administration/.../settings` approval-rule form: the rule stores a tier (standard or large) instead of a role name, shows which roles can approve each tier (from the roles list), keeps `roleRequired` read-only for one release while the API deprecates it.
- API: RP-4b. Size: 400. Depends on: R08, RP01.
- Acceptance: a rule shows "Standard approvers" or "Large approvers" with the current role names; an old rule that named a role shows its tier; the Settings screen keeps ER-04 behaviour.

### 1.9 Public ids: the web side of the GUID decision (G01, G02)

The GUID ADR (`docs/architecture/adr-guid-keys.md`) is **Accepted** (2026-10-12): option 1, the hybrid. The database keeps its integer keys; the API, URLs and logs use a random public GUID (`PublicId`) on 9 tables (User, Warehouse, Partner, Category, Uom, Product, ApprovalRule, StockDocument, Attachment), cursors become opaque strings, and `id` leaves the ledger and audit responses. Owner decisions: no offline creation (client-minted ids are optional, PR-7b), and ids must not be guessable. The backend PRs 4 to 7 are stacked on an integration branch `feature/public-ids`, which PR-9 merges to main in one step once the web is ready, so main never has an API and a web that disagree. Backend PR-1 to PR-3 go straight to main and change nothing the web depends on.

Consequences for this plan: (1) the web flip is two slices, G01 (ADR PR-8) and G02 (ADR PR-9, e2e); (2) slices that carry ids (the Ids column of the master list, section 1.10) are written to rule N13 from the first line; those marked **H** wait for G02 (the owner's decision: slices that build, store or compare ids wait for the merge of the integration branch), those marked **T** go ahead and are rebased; (3) G01 occupies a wave together with disjoint tooling slices only (no table, editor, Home, shell, sign-in, access or drawer work in that wave), because it touches every store, route and spec; (4) the generated client is regenerated once per batch (batch CS3 of the roadmap, 2.4); (5) the lane freeze of the ADR (about 1.5 working days in the roadmap) is that wave. The roadmap's rules for the integration branch (`docs/pm/ROADMAP.md` 3.4) apply: no migration on the branch (the schema half, PR-3, is already on `main`), the product owner adds the `large-change` label to the one oversized merge (PR-9), and the real-API e2e workflow is run by hand on the branch before the merge.

#### G01 Public-id web flip (ADR PR-8)

- Files: regenerate the client from the integration branch (ids `string` with `format: uuid`, `cursor` and `nextCursor` as strings, `DocumentLineDto.id` removed so lines are identified by position, the optional `id` on create ignored), ids to `string` in models, stores and specs, routes `/receipts/:id`, `/issues/:id`, `/stock/:id`, `/catalog/:id` (the `Number(id)` conversions go; `NEW_DOCUMENT_ID = 'new'` cannot collide with a UUID; an old bookmark `/receipts/31` shows "Page not found"), the 409 `productId` extension parsed with `Number` in `document-editor.component.ts` becomes a string compare, recents (R18), saved views (A06), selections (A09), the notification "seen" set (A18) and preferences keep ids as opaque strings and validate their shape on read, the `master-data` group `version` in `ngsw-config.json` is bumped so old integer payloads are dropped, an ESLint `no-restricted-syntax` rule bans `Number(` and `parseInt(` on id-named values, the typed stub fixtures of Q01 are updated (they fail to compile at the flip, which is the point).
- API: ADR PR-7 merged on the integration branch (expected on it by wave 8). Size: 900. Depends on: none in the web; earliest wave 9.
- Acceptance: no `number`-typed entity id remains in `@ims/*` (type test); every spec compiles and passes; the production build passes the budget; a deep link with an unknown UUID gives the polite not-found page; an out-of-scope id answers 404 and the UI shows the same page (EA-25); no id is parsed, compared numerically or used as an index (grep).
- Tests: the existing suite re-typed; stub fixtures regenerated; real-API scenarios run in G02. Re-check: edge (IDOR retest with the security-engineer, ADR PR-10), power, novice.

#### G02 Public-id e2e fixes and merge (ADR PR-9)

- Files: `e2e/src/api.ts`, `e2e/src/pages/documents.ts` (`savedId`) and `screens.ts` page objects, scenario fixes, smoke, the merge of `feature/public-ids` to main after G01 is green.
- API: none. Size: 300. Depends on: G01. Built by behavior-test-engineer (page objects are edited here, in this slice's reserved slot).

### 1.10 Slices that touch entity ids (the GUID decision is accepted)

The decision in `docs/architecture/adr-guid-keys.md` is accepted: every id in the API becomes a string (a random public GUID), `afterId` becomes an opaque `cursor`, routes such as `/receipts/:id` and `/stock/:id` carry UUIDs, and the generated client changes types. The master list (1.2) has an "Ids" column: **H** = the slice builds routes, query parameters, cursors, stored ids or compares ids; by the owner's decision it **waits for G02** (the merge of the integration branch) and carries that dependency in the list; **T** = type-only: it compiles against the generated types, goes ahead in its wave and is rebased at G01; **-** = does not touch ids. Rule N13 applies to every slice from the first line.

| Mark | Slices | What they touch |
|---|---|---|
| H | R17, R19 | Home rows link to `/receipts/:id` and `/stock/:id`; home feed store; also wait for client batch CS4 (B1 to B3) |
| H | R18, R20 | recents of documents and products stored on the device; palette results open documents and `/stock/:id` |
| H | A07 | URL state builds `warehouseId`, `productId` and similar query parameters |
| H | A09 | selection holds ids; "create a receipt from these" passes `?products=` ids |
| H | A11 | seek cursor (opaque `cursor` after the switch) |
| H | A16 | `?peek=<id>` route parameter and fetch by id |
| H | A18 | notification "seen" set keyed by id + concurrency token |
| H | Q06 | cached endpoints; the master-data `version` bump is done in G01 and Q06 only checks it |
| H | RP02 to RP09 | role and person ids in routes, stores and URLs |
| H | G01, G02 | the flip itself |
| T | R03, R06, R11, R14, R15, R22, R27, R28, R31, R33, RP01, RP11 | reports store types; preference keys use the user id as a string; 409 `productId` extension; row highlight compares ids; `paramMap` of the stock page; `/api/documents/{id}/recall` and `send-back`; line-editor group references |
| T | A06, A08, A13, A14, A15, A20, R29, R30, Q01, M06 | saved-view filters, fetch by id, picker, lines (the ADR removes `DocumentLineDto.id`: lines are identified by position, which A14 already does), paste resolution, chart stores, links, typed stub fixtures (they fail to compile at the flip, which is the point) |

Rules: (1) new code treats ids as opaque strings (N13); (2) the web flip is one wave (G01) followed by the merge (G02), about plan wave 10 to 11 (end of the roadmap's P2); (3) the H slices that the schedule would otherwise place earlier (Home Doers R17 was in the fast path) move behind G02, and R27 takes R17's place in the fast path (roadmap conflict C3); (4) the roles backend RP-3 onward also reaches `main` only after PR-9 (roadmap C10), which the readiness waves of 3.3 already reflect.

## 2. Waves, serial lanes and file collisions

### 2.1 Waves (at most three frontend builds in flight)

A wave is a set of at most three slices built at the same time by separate agents; the wave is finished when all its slices are merged, the independent review is done and CI is green. Slices in one wave touch disjoint files (collision map 2.4). The letter is the lane (2.2) and the number the size in lines. The order inside the table comes from one rule set: dependencies, one slice per lane per wave, the backend readiness waves of section 3.3, and a priority order that puts the fast path first, then the defects and the foundations, then the quality net, the access programme and the table features, and the Could slices last. The G01 wave contains only tooling slices (it touches every store and route). The pull request limit of `docs/pm/ROADMAP.md` (4 agents at once, at most 2 heavy) means that while both backend slots are busy the orchestrator runs two web builds, not three; the order below does not change, only the calendar (section 2.8).

| Wave | Parallel builds (lane, lines) | Lines | Gate before the next wave |
|---|---|---|---|
| 1 | R01 (Y, 310) / R02 (F, 600) / Q01 (Q, 900) | 1,810 | merged, CI green |
| 2 | R04 (F, 450) / R33 (T, 200) / R05 (Y, 660) | 1,310 | merged, CI green |
| 3 | L00 (O, 600) / RP00 (S, 300) / R07 (Y, 450) | 1,350 | merged, CI green |
| 4 | R08 (F, 900) / L01 (Y, 800) / R10 (S, 520) | 2,220 | merged, CI green |
| 5 | R27 (E, 450) / A02 (T, 1,000) / A01 (V, 650) | 2,100 | G1: fast path (waves 1-5) plus L00, RP00, R33, A01; fe-designer light/dark review at 320 and 1280 and motion check on a low-end Android phone and an iPhone; qa-novice (NR-01 on a phone, Partners with 300-character names), qa-power, qa-edge smoke |
| 6 | R06 (S, 700) / M01 (Y, 650) / R11 (V, 930) | 2,280 | merged, CI green |
| 7 | R28 (E, 750) / R03 (O, 350) / A03 (T, 1,000) | 2,100 | merged, CI green |
| 8 | M02 (N, 900) / R13 (V, 850) / R25 (U, 1,000) | 2,750 | merged, CI green |
| 9 | A04 (T, 800) / M07 (Q, 600) / R31 (E, 650) | 2,050 | merged, CI green |
| 10 | G01 (G, 900) / Q02 (Q, 700) / Q03 (Q, 600) | 2,200 | merged, CI green |
| 11 | G02 (Q, 300) / R12 (S, 500) / R09 (O, 500) | 1,300 | merged, CI green |
| 12 | M03 (S, 650) / L04 (T, 900) / M05 (U, 300) | 1,850 | merged, CI green |
| 13 | L02 (O, 1,000) / A05 (T, 600) / L03 (S, 1,000) | 2,600 | G2: sign-in page, document corrections (R28, R31), id flip merged (G01, G02), tables, overlays, density, layout system, motion foundation; QA round 2; qa-novice, qa-power, qa-edge, fe-designer approves the first baselines |
| 14 | R14 (E, 780) / R17 (H, 900) / RP01 (X, 800) | 2,480 | merged, CI green |
| 15 | A06 (T, 900) / A12 (F, 840) / L08 (Q, 800) | 2,540 | merged, CI green |
| 16 | R15 (T, 800) / R16 (V, 500) / Q11 (Q, 400) | 1,700 | merged, CI green |
| 17 | R19 (H, 900) / A07 (T, 900) / R26 (E, 450) | 2,250 | merged, CI green |
| 18 | M04 (H, 400) / RP02 (X, 900) / Q04 (Q, 350) | 1,650 | G3: quality net on (Q02, Q03, L08, Q11, Q04); baselines approved; visual, matrix and MO-05 gates flipped to blocking; Home (R17, R19), first access screens, QA round 3 |
| 19 | R18 (S, 750) / R23 (T, 600) / A16 (D, 920) | 2,270 | merged, CI green |
| 20 | RP03 (X, 1,100) / A08 (T, 620) / R20 (S, 700) | 2,420 | merged, CI green |
| 21 | RP04 (X, 1,100) / A09 (T, 840) / A13 (E, 500) | 2,440 | merged, CI green |
| 22 | RP05 (X, 1,150) / Q07 (Q, 700) / A10 (T, 600) | 2,450 | merged, CI green |
| 23 | RP07 (X, 850) / A14 (E, 1,100) / Q10 (Q, 760) | 2,710 | G4: roles and person's page (RP01 to RP05, RP07), drawer, combobox, export; QA round 4 with qa-edge leading (lockout, escalation, live rights change) |
| 24 | RP11 (X, 400) / A11 (T, 1,000) / R29 (E, 700) | 2,100 | merged, CI green |
| 25 | RP06 (X, 850) / R30 (T, 600) / A15 (E, 900) | 2,350 | merged, CI green |
| 26 | Q05 (Q, 750) / R32 (Q, 500) / RP09 (X, 650) | 1,900 | merged, CI green |
| 27 | Q06 (Q, 800) / Q12 (Q, 760) / A17 (D, 640) | 2,200 | G5: lines grid and paste, virtual scroll, phone screens, errors, offline, exceptions, history, approval tier |
| 28 | A18 (S, 760) / M06 (T, 700) / Q08 (Q, 700) | 2,160 | merged, CI green |
| 29 | RP10 (Q, 900) / Q09 (Q, 800) / L06 (Y, 600) | 2,300 | merged, CI green |
| 30 | A21 (V, 450) / A19 (H, 1,000) / L05 (O, 600) | 2,050 | merged, CI green |
| 31 | R22 (S, 500) / RP08 (X, 900) / A20 (H, 900) | 2,300 | merged, CI green |
| 32 | A22 (S, 800) / L07 (E, 700) / L09 (O, 500) | 2,000 | G6: charts, print, forced colours, breadcrumbs, motion final, compare, shortcuts; qa-edge, qa-power, fe-designer final screenshots |
| 33 | M08 (T, 350) | 350 | merged, CI green |
| 34 | R24 (Y, 300) | 300 | G7: final pass of all three personas, full real-API suite, release device checklist, one release of soak before R24 |

Dependency spine: R01 -> R05 -> {R07, R08, L01} -> A02 -> A03 -> A04 -> A05 -> A06 -> A07 (the table lane); R05 + R07 -> M01 -> M02 -> {R09, M03, M04, M05}; every slice marked H in the Ids column waits for G02 (merge of the id integration branch): G02 -> {R17 -> R19 -> A20, R18, R20, A07, A09, A11, A16, A18, Q06}; RP00 -> R10; A01 -> {A03, A12, A16, A18, L02, R25}; R11 + R25 + R14 -> R26 -> RP03; backend PR-7 -> G01 -> G02; RP-3/RP-4b -> RP01 -> RP02 -> RP03 -> RP04 and RP05 -> RP06/RP07; B22 -> R28 -> R31 -> R32; Q01 -> {Q02, Q03, L08, M07} -> Q11; A12 -> A13 -> A15 (with A14); A16 -> A17; R11 -> Q05 -> Q06. Only R01, R02, R03, L00, RP00, R33 (after the money PRs merge) and Q01 can start early; R03 waits for a free slot (wave 7) because the slots of waves 1 to 6 go to the fast path, L00, RP00, R33, R06, M01 and A01.

### 2.2 Serial lanes

A lane is a set of slices that edit the same files, so they run one at a time in the order shown. Different lanes run in parallel, which is how three builds stay in flight. The lane letters in the wave table refer to this table.

| Lane | Order (wave in brackets) |
|---|---|
| T Table and list pages (critical path) | R33 (2), A02 (5), A03 (7), A04 (9), L04 (12), A05 (13), A06 (15), R15 (16), A07 (17), R23 (19), A08 (20), A09 (21), A10 (22), A11 (24), R30 (25), M06 (28), M08 (33) |
| S Shell and navigation | RP00 (3), R10 (4), R06 (6), R12 (11), M03 (12), L03 (13), R18 (19), R20 (20), A18 (28), R22 (31), A22 (32) |
| E Document editor | R27 (5), R28 (7), R31 (9), R14 (14), R26 (17), A13 (21), A14 (23), R29 (24), A15 (25), L07 (32) |
| U Sign-in | R25 (8), M05 (12) |
| D Drawer and document detail | A16 (19), A17 (27) |
| H Home | R17 (14), R19 (17), M04 (18), A19 (30), A20 (31) |
| X Access (roles and permissions) | RP01 (14), RP02 (18), RP03 (20), RP04 (21), RP05 (22), RP07 (23), RP11 (24), RP06 (25), RP09 (26), RP08 (31) |
| F Form controls | R02 (1), R04 (2), R08 (4), A12 (15) |
| V Overlays and feedback | A01 (5), R11 (6), R13 (8), R16 (16), A21 (30) |
| Y Styles library | R01 (1), R05 (2), R07 (3), L01 (4), M01 (6), L06 (29), R24 (34) |
| Q E2E and CI tooling | Q01 (1), M07 (9), Q02 (10), Q03 (10), G02 (11), L08 (15), Q11 (16), Q04 (18), Q07 (22), Q10 (23), Q05 (26), R32 (26), Q06 (27), Q12 (27), Q08 (28), Q09 (29), RP10 (29) |
| G Public-id flip (exclusive wave) | G01 (10) |
| N New folders (no collision) | M02 (8) |
| none (SCSS or page-level) | L00 (3), R03 (7), R09 (11), L02 (13), L05 (30), L09 (32) |

The critical path is lane T: 17 slices from R33 (wave 2) and A02 (wave 5) to the Could slice M08 (wave 33). The plan has 34 waves: 98 slices at three per wave is 33, and R24 must follow every other slice alone. The last two waves are small by design.

### 2.3 Known collision risks inside a wave (and the rule that handles them)

| Wave | Slices | Risk | Rule |
|---|---|---|---|
| 2 | R04, R33, R05 | R04 line-editor and pipes, R33 stock and ledger screens, R05 tokens | disjoint files; R33 regenerates the client (batch CS1, 2.4) and nobody else does |
| 3 | L00, RP00, R07 | L00 and R07 both edit SCSS | L00 does not touch `skeleton/*` or `spinner/*`; R07 does not touch component layout rules |
| 4 | R08, L01, R10 | R08 controls, L01 new styles partials, R10 shell | L01 puts safe-area variables in a new partial |
| 5 | R27, A02, A01 | R27 `input/*` and the editor, A02 table, A01 new folders | A02 renders its own skeleton rows and leaves `skeleton/*` to R15 |
| 6 | R06, M01, R11 | R06 and M01 both edit `theme-init.js` | R06 merges first, M01 adds one line after rebasing; R11 edits `core-http` and notification |
| 7 | R28, R03, A03 | R28 `document-actions` and the Post dialog; R03 the reports page; A03 table | disjoint; R28 regenerates the client (batch CS2) |
| 8 | M02, R13, R25 | R13 restyles `app-banner`, R25 uses it and appends to Y-lane files (`_palette.var.scss`, `$widths`, `check-contrast.mjs`) and adds `menu` to `shared/ui` | R13 keeps the banner API; nobody else edits those files in the wave |
| 9 | A04, M07, R31 | A04 touches five row-action screens; R31 the document dialogs | disjoint |
| 10 | G01 with Q02, Q03 | the id flip touches every store, route and spec | only e2e and CI slices share the wave; every other lane is frozen for the wave (the ADR's one-day freeze, about 1.5 working days in the roadmap) |
| 11 | G02, R12, R09 | R12 shell account dialog; R09 chips and tabs (uses M02); G02 page objects and the merge | disjoint |
| 12 | M03, L04, M05 | M03 shell, `app.config.ts`, `ThemeService`; L04 list-page columns; M05 login | disjoint; M03 merges before L03 (wave 13) |
| 13 | L02, A05, L03 | L02 edits `*.scss` of pages, A05 their `*.ts` and `*.html` | L02 makes no TypeScript change |
| 14 | R14, R17, RP01 | RP01 touches the menu constant `MENU_ITEMS`, `core-identity` and `features/administration`; R17 Home | RP01 does not edit `shell.component.*` |
| 15 | A06, A12, L08 | A06 adds `tableId` to pages only | disjoint |
| 16 | R15, R16, Q11 | R15 touches the Settings screen and the editor | R15 merges before RP11 (wave 24) |
| 17 | R19, A07, R26 | R26 edits the editor guard and login pieces | disjoint |
| 18 | M04, RP02, Q04 | RP02 is a new folder | disjoint |
| 19 | R18, R23, A16 | R23 (date filter bars) and A16 (row click and peek) both edit `document-list` | R23 merges first; A16 rebases |
| 20 | RP03, A08, R20 | A08 edits `document-list` after A16 | serial by wave |
| 21 | RP04, A09, A13 | A09 adds a query-parameter read to `document-editor`; A13 edits its header | A13 merges first |
| 22 | RP05, Q07, A10 | RP05 uses the drawer of A16 and edits the users pages | none expected |
| 23 | RP07, A14, Q10 | RP07 adds the access banner to the shell (lane S is otherwise idle) | none |
| 24 | RP11, A11, R29 | RP11 Settings; A11 list pages; R29 editor and `line-editor` styles | disjoint |
| 25 | RP06, R30, A15 | R30 `product-picker` and stock pages; A15 `lines-grid` | disjoint |
| 27 | Q06, Q12, A17 | A17 adds the History tab to `document-editor` | none |
| 28 | A18, M06, Q08 | M06 adds FLIP to editor lines (after A17 in 27) | serial by wave |
| 30 | A21, A19, L05 | all SCSS-heavy | L05 makes no TypeScript change; A21 edits only `empty-state/*` |
| 31 | R22, RP08, A20 | R22 shell and editor handlers | disjoint |
| 32 | A22, L07, L09 | A22 touches every page header | A22 keeps the old header API working, so pages change only where they pass their own back link |

### 2.4 Collision map (one owner per lane at a time)

| Hotspot | Slices that touch it, in merge order | Rule |
|---|---|---|
| Table and list pages | lane T | Serial. R03 avoids the table (cell templates only). A01, L02 and the M slices do not edit table files except M06. |
| Shared form field (`form-field`, `input`, `select`, `radio-group`) | R02, R08, R27 (`input` selectOnFocus), A12 (uses `FORM_FIELD`), R23 | Serial. R02 changes markup and the token, R08 styles/states and motion, A12 and R23 add sibling components that use the field. |
| Shell (`libs/core/navigation/.../shell.component.*`) and the role-name maps | RP00 (removes `PHONE_TABS_BY_ROLE`, `ALL_WAREHOUSE_ROLES`, the role switch of `approval-visibility.ts`), R10, R06 (account dialog), R12 (density radio), M03 (transitions, progress), L03 (modes, keyboard inset), R18 (trigger), R20 (phone button), RP07 (access banner), A18 (bell), R22, A22 | Serial in that order. Keep every existing class name (`shell-nav`, `shell-tabbar`, `shell-bar`); `data-shell` attributes are additive. The menu constant `MENU_ITEMS` (RP01) is a separate file. |
| Sign-in (`containers/authentication`, `core/identity` login state) | R02 (legend, D3), R25 (page), M05 (entrance), R26 (re-sign-in dialog) | Serial. |
| Access (`containers/administration/.../roles`, `.../users`, `features/administration`) | RP01, RP11, RP02 to RP09 | Lane X, serial. RP11 and R15 both edit the Settings screen (see 2.3). |
| Styles library (`libs/shared/styles/**`) | R01 (palette, `color()`), R05 (all non-colour tokens incl. motion tokens, density blocks), R07 (`_base.scss`), L01 (new partials), M01 (`_motion.scss`, mixin), A01 (`globals/_cdk.scss`), R25 (brand aliases, two width entries), L06 (new partials), R24 | R05 owns `_layout.var.scss`, `_shape.var.scss`, `_font*.var.scss`, `_space.var.scss`, `_themes.scss`; later slices add new partials, with R25's two `$widths` entries as the one allowed append. `globals/index.scss` has one line per partial: on conflict keep both lines. |
| Overlay components (dialog, confirm-dialog, toast, banner, card, empty-state, `core-notification`, popover, menu, drawer) | A01 (new), R11 (behaviour), R13 (look, depends on M01), R25 (new `menu`), A16 (new), A21 (`empty-state` input) | R11 before R13; R13 must not change the toast or confirm service API; A21 after R13. |
| Motion code (`shared/ui/.../motion`, `shared/utils/.../motion`, `_motion.scss`) | M01, M02, then consumers | Only M01 and M02 create motion primitives; slices consume them. A slice that needs a new motion pattern asks fe-designer to add it to the motion file first. |
| Document editor (`document-editor`, `document-actions`, `line-editor`, `lines-grid`) | R02, R04, R10, R27, R11, R28, R31, R14, R15, R26, A13, A09 (query-parameter read), A14, R29, A15, A17 (History tab), M06, R22, L07 | Serial in wave order. |
| i18n catalogs (`libs/core/i18n/src/lib/messages/{en,zh,ms}.messages.ts`) | every slice | Add keys inside the existing area groups (`form.`, `theme.`, `login.`, `table.`, `perm.`, `role.`, `chart.`, `palette.`, ...), never appended at the end of the file. Every key goes into all three files in the same PR (the compile-time check fails otherwise); zh and ms drafts carry `TODO-review`. A second PR to merge rebases and keeps both blocks. The access programme adds the most keys (about 300): RP slices use the `perm.` and `role.` groups only. |
| `UI_TEXT` token and provider | R02, R07, R08, R16, A01, A03, A09, A10 | Append alphabetically; same rebase rule. |
| `icon.paths.ts` | R05, then A03, A09, L07, RP01 (`shield`, `eye`, `plus`, `minus`) and any slice that needs an icon | Append-only and alphabetical (N11); each slice lists its icon names. |
| e2e page objects, registry and config | Q01 owns the structure; R02 and R25 (`LoginPage`), R18 (`Shell.account`), R17 and R19 (Home), G02 (`documents.ts` `savedId`, `screens.ts`), RP01 (`Role` type), then registry entries added by later slices | Page objects are edited only by the slice that breaks them, in that PR; a registry entry is one line. Stub tests live in `e2e/tests/stub/` and are unnumbered. |
| `web/apps/main/project.json`, `ngsw-config.json`, `index.html`, manifest | R01, R06, L03, R25, Q04, Q10, Q06, G01 (`version` bump of the master-data group) | Serial in wave order. |
| `web/scripts/check-contrast.mjs` | R01, R06, R25, A19 | A new colour token enters only with its pairs in the same PR. |
| `package.json` and the lockfile | A01 (`@angular/cdk`), M01 (removes `@angular/animations`), Q12 (`@lhci/cli`, dev, in `e2e/`), Q08 (`jest-axe`, dev) | fe-architect approves each change; never two in one wave (waves 5, 6, 26, 28). No other dependency change is planned. |
| **Generated API client** (`libs/shared/api/src/lib/api-client.ts`, `web/openapi/**`) | regenerated only in the five batches of `docs/pm/ROADMAP.md` 3.3, by one agent | CS1 (after the money PRs #44 to #46: ledger balance, one low-stock rule, `reversedBy`, search) in R33; CS2 (DC-BE2 and the opaque cursors: recall, send back, cancel body, new `allowedActions`) as the first commit of R28, merged alone; CS3 (the whole id switch) in G01 only; CS4 (after PR-9: `returned` filter, ascending order, `updatedAt`) before R17 and R19; CS5 (roles API, `PermissionCode`, `UserInfo` additive fields, `X-Permissions-Version`, approval tier) before RP01 and RP02 (RP01 takes the first part, RP-3 and RP-4b fields, RP02 the roles endpoints, RP05 the overrides; one agent each time, never two regenerations open at once, never during the id freeze except CS3). Other PRs rebase and never run `api:generate`. Regeneration lines are excluded from slice sizes as before. |
| `web/eslint.config.mjs`, `web/.stylelintrc.json`, `check-motion.mjs` | R01, R08, A01, M01, Q07, R24 | Serial. |
| Core HTTP interceptors | R11, R25 (uses the flag), Q05 | R11 first. |
| Home widgets | lane H | Serial. |
| `.github/workflows/*` | Q02, Q03, Q11, M07 | Separate files per slice so no two PRs edit one workflow; Q11 merges them into the final model. The orchestrator assigns the owner (suggestion: behavior-test-engineer writes, security-engineer reviews). |

Reserved real-API scenario files: `14-required-marker` (R02), `15-reports-range` (R03), `16-appearance` (R06), `17-reduced-motion` (R07, R15), `18-feedback` (R11, R14, R15), `19-density` (R12), `20-home-doer` (R17), `21-home-reviewer` (R19), `22-palette` (R18, R20), `23-shortcuts` (R22), `24-paging-dates` (R23; A05 extends it), `25-login` (R25), `26-resign-in` (R26), `27-lines-grid` (A15), `28-drawer-approve` (A16), `30-document-corrections` (R32), `40` to `45` (RP10: roles list, role editor, person's access, exceptions, safety, live change). `01-receive` is extended by R27, `10-phone` by R29 and R30. Existing scenarios updated: `06-roles` (RP01), `08-session`, `11-accessibility`, `13-cost-visibility`, `01-receive`, `03-approval-override`. The real-API suite stays manual, nightly (`@smoke`) and weekly; the PR gate is the stub suite (N6), so a slice author runs the affected stub tests locally (`npm run test:stub`) and the real-API scenarios they list.

### 2.5 Who builds what

fe-framework builds every slice in lane `web/`. behavior-test-engineer builds the e2e parts (Q01, Q02, Q03, Q12, L08, M07, G02, R32, RP10 and the scenario files above). The orchestrator decides who edits `.github/` (Q02, Q03, Q11, ruleset). fe-designer reviews screenshots and approves baselines at gates G1, G2, G3 and G6, rules on the designer-owned findings before R29 and R30 are built, and checks motion on a real low-end phone at G1 and G3. ui-ux-designer owns the access UX file and the corrections file (rulings on questions). fe-architect reviews each PR against `docs/architecture/frontend.md` and this plan, and is the independent reviewer of 4.5 (never for a slice it wrote). security-engineer reviews A01 (CSP), R25, R26, Q05, Q06, A10, A18, G01 (IDOR retest, ADR PR-10), RP01, RP03, RP05 to RP07. be-framework and be-architect build section 3.

### 2.6 Fast path: the first 10 slices that make the UI visibly better

The slices with the most visible and daily effect that can ship first. R17 (Doer Home) was in this list in the previous revision; it carries ids, so it now waits for the id integration branch (roadmap conflict C3) and R27, the top finding of the novice retest (NR-01), takes its place. RP00 (300 lines) is a prerequisite of R10: it replaces the role-name maps by a permission-derived helper before the tab bar and the Home profile are rebuilt. Motion growth is included in the sizes (the first visible motion, shimmer skeletons and pressed buttons, arrives with R07 and R08).

| # | Slice | Lines | Wave | What the user sees |
|---|---|---|---|---|
| 1 | R01 Palette, aliases, contrast gate | 310 | 1 | Calm blue/neutral palette in light and dark across the whole app with no component edits; contrast gate on |
| 2 | R02 Required marker, legend, login | 600 | 1 | Red asterisk and "* means required"; login banner no longer fights field messages |
| 3 | R05 Token foundation, icons, motion tokens | 660 | 2 | Radius, line height, type scale, crisper icons; density and motion tokens ready |
| 4 | R04 Money format and input rules | 450 | 2 | `12,400.00` everywhere; no sub-cent input; correct quantity rule (ER-12) |
| 5 | R07 Motion contract, spinner, shimmer skeletons, delay helper | 450 | 3 | Shimmering skeletons that stop by themselves; reduce-motion respected |
| 6 | R08 Buttons and form controls | 900 | 4 | Clear states, pressed and hover feel, busy button that keeps focus, "Saved" state, segmented control |
| 7 | L01 Layout system foundation | 800 | 4 | Nothing visible alone; unlocks the table and every responsive slice |
| 8 | R10 Shell, tab bar, action bar | 520 | 4 | New top bar and five-cell tab bar with an active-tab cue, keyboard-aware tab bar, Account button name (ER-09) |
| 9 | A02 Table core | 1,000 | 5 | Sticky header, pinned first column, cleaner table, columns decided by the table's own width, density-ready rows |
| 10 | R27 Quantity entry and double-save safety | 450 | 5 | The quantity field replaces its value when typed into (NR-01); a double tap saves once |
| | prerequisite: RP00 Permission-derived profile helper | 300 | 3 | Nothing visible; no new role constants |
| | **Total** | **6,440** | 1 to 5 | |

The spare slots of those waves go to Q01 (the stub test harness), L00 (content containment, the owner-reported sideways scroll) and R33 (the follow-up of the money PRs: ledger balance, one low-stock rule); wave 5 adds A01, wave 6 R06 (theme per user, no flash), M01 and R11. Elapsed: 5 waves, about 9 working days including the independent review of each slice (4.1 row 15), the 25% rework allowance and a half-day gate (G1 runs after wave 5). R25 (the sign-in page) is the next most visible slice and lands in wave 8; R17 (Doer Home) follows the id merge, in wave 14. What the fast path leaves out on purpose: feedback and overlay behaviour (R11, R13), the density switch, the command palette, every Home screen, every advanced table feature, editors, drawer, charts, the access programme.

### 2.7 What can be cut for a shorter path

Cut in this order (the first item hurts least). Each cut lists the slices that fall with it.

| Order | Cut | Slices (lines) | Falls with it | Effect |
|---|---|---|---|---|
| 1 | Charts and polish | A19, A20, M08, A21, L09, R22, L07 (4,400) | none | Home keeps tiles and lists; no chart draw-in (the motion file's cut list: M08, shared-element transition, chart draw-in come first); no illustrations, no docked peek, no shortcuts dialog, no print stylesheet; removes backend B12 to B14 and migration MG6 |
| 2 | Virtual scroll, table motion, perf budgets | A11, Q12, M06 (2,460) | none | Paging only (the default anyway); no drag feedback or row flash on refetch; keep Q04 |
| 3 | Table power extras | A08, A09, A10 (2,060) | A10 needs A09 | No row expansion, bulk bar or CSV export; the four server Excel exports remain. A06 stays because A07 needs its `TableView` model |
| 4 | Timeline and notifications | A17, A18 (1,400) | none | The drawer (A16) stays because RP05 uses it; approvals still appear in Home and the sidebar badge |
| 5 | Spreadsheet-style lines | A14, A15 (2,000) | M06 already cut | Card line editor stays on desktop; the combobox (A12, A13) stays |
| 6 | Offline read cache and the second contract-test slice | Q06, Q09 (1,600) | none | No offline banner or cache; contract tests only for form controls |
| 7 | Access extras | RP06, RP08, RP09 (2,400) | none | No exceptions, no compare, no history links; roles and the person's page stay |
| | **All seven cuts** | **16,320** (22 slices) | | 76 slices and 52,520 lines remain, about 26 waves, about 11 calendar weeks |

Never cut (they carry the promises and the Major findings): R01 to R08, R10 to R15, R25 to R28, R31, R33, L00 to L04, L08, A01 to A05, A07, A12, A13, G01, G02, M01, M07, Q01 to Q05, Q07, Q10, Q11, RP00 to RP05, RP07, RP10, RP11. M02 to M05 are small and make the motion consistent; cutting them leaves the foundation unused, so cut M08 and M06 first (the motion file's order). R28 is the NR-02 fix. R24 cleanup is the last slice in every variant.

### 2.8 Size and elapsed time

| Programme | Slices | Lines |
|---|---|---|
| R Refresh (R01..R33; R21 removed; R25 sign-in, R26 re-sign-in, R27 to R30 QA round 3, R28 and R31 document corrections, R32 their e2e, R33 money-PR follow-up) | 32 | 19,750 |
| A Advanced UI | 22 | 17,720 |
| Q Stable | 12 | 8,220 |
| L Responsive (L00..L09) | 10 | 7,500 |
| M Motion (8 new slices; growth of 20 slices is inside the other rows) | 8 | 4,550 |
| X Access, roles and permissions (RP00..RP11) | 12 | 9,900 |
| G Public-id web flip (G01, G02) | 2 | 1,200 |
| **Total** | **98** | **68,840** |

Version 1 of the plan was 24 slices and 14,760 lines; the previous revision of version 2 was 73 slices and 49,260 lines (the motion file counts 81 slices and 55,940 lines on top of that, the roadmap uses the same figures). The increase to 68,840 is 6,680 lines of motion, 9,900 of access (RP00 to RP11), 1,200 of public ids, 1,800 of document corrections and their follow-ups (R28 grows from a 300-line placeholder to 750, R31 650, R32 500, R33 200).

Elapsed-time estimate for an agent team with CI, the independent review of every slice, QA checks and a **25% rework allowance** (the same allowance as the roadmap). There is no velocity data for this repository yet; the estimate is built from stated assumptions and must be re-estimated after wave 3 (the roadmap re-estimates at WD 10).

- One slice of 600 to 1,000 lines: build 3 to 5 hours (code, specs, local lint/test/build), CI 15 minutes, independent review 30 to 45 minutes (PR-REVIEW section 8). Three slices run in parallel, so a wave under 2,000 lines takes about 1 working day of build and a wave of 2,000 lines or more about 1.5 days.
- Waves: 24 waves of 1.5 days, 8 of 1 day, 2 closing waves of half a day = 45 build days. Rework allowance: 25% of the build = 11.3 days. Independent review overhead: 0.25 day per wave = 8.5 days. Gates G1 to G7: 3.5 days. Orchestrator routing, owner decisions and the id-merge window: 3 days. Total about 71 working days.
- **Honest range: 14 to 15 calendar weeks as the central figure, 12 weeks if reviews are quick and no gate finds a Major, 18 weeks if CI is slow, the table or the lines grid needs a redesign, the backend lanes (section 3.3) are late, or only one backend agent is available.** The fast path is about 9 working days (under 2 weeks). Waves 4 to 10, 13 to 15, 17, 19 to 25 and 27 to 32 are at or above 2,000 lines (the heaviest are 8, 13 and 23); A02, A14, A11, A19, R25 and the access editor RP03 are the slices most likely to run over and be split.
- Relation to `docs/pm/ROADMAP.md`: the roadmap is the calendar owner and covers backend, QA rounds, hosting and deployment (72 working days in total, typical). Both documents assume about 2.2 to 2.4 finished slices per working day on the web side (three builds per wave here; two web slots in the roadmap while both backend slots are busy). This plan's figure is the frontend critical path including the 25% allowance; the roadmap's P1 to P5 total is 65 working days for a smaller web list (81 refresh slices and RP01 to RP10). With RP00, R33 and the document-corrections slices added, the project-manager should re-baseline at WD 10; until then use the roadmap's dates and this plan's order.
- Backend work runs in parallel in the roadmap's slots (BE-1 id and schema lane, BE-2 feature lane) and is not on the frontend critical path as long as the readiness waves of 3.3 hold; with fewer backend agents RP01 to RP09 slide and the scheduler gives their slots to the next slices.

## 3. Work that is not frontend (backend)

Nothing here blocks a slice from starting: each slice has a stated fallback. The items only improve exactness, scale or safety. If B-items are built, the web client is regenerated with `npm run api:generate` and the change is a small follow-up (not counted in the 1,200-line cap).

### 3.1 Backend items

| ID | Need | Used by | Without it | Priority | Owner |
|---|---|---|---|---|---|
| B1 | `returned=true` filter on `GET /api/documents` (a Draft with `RejectionReason != null`, the same rule as `isReturned`) | R17 W3, A18 | The client reads the newest 25 of the user's documents; a returned draft older than that is counted in the heading but not listed | Recommended | be-framework |
| B2 | An ascending order option on `GET /api/documents` (for example `sortBy=submittedAt&desc=false`; today it is `OrderByDescending(d => d.Id)` only) | R19 W6 "oldest first" | The client sorts the newest 50 waiting documents; with more than 50 waiting, the true oldest could be missed | Recommended | be-framework |
| B3 | `updatedAt` (and `returnedAt`) on `DocumentListItem` | R17 W3/W4 "age" and "saved time" | Rows show `createdAt`; slightly wrong for an old draft edited today | Optional | be-framework |
| B4 | Server-side 2-decimal rule for `unitCost` (receipt lines) and `amountThreshold` (approval rules): reject, with the sentence the UI uses, or round half-up and say so in the OpenAPI description | R04, A15 | The UI blocks sub-cent input, but a script or old client can still post 12.345 | Recommended | be-architect decides, be-framework builds |
| B5 | Confirm that `GET /api/products?search=` matches SKU, name and barcode, and `GET /api/partners?search=` matches name | R20, A12, A13 | The palette and combobox find fewer items than their placeholder says | Verify only | be-framework |
| B6 | Already open, visible on the new Home and charts: PR-01 (reversing a receipt leaves the average cost and so Stock value wrong), PR-05, PR-08 | R19 W9, A20 W14 | The Stock value tile and the value-history chart show a wrong number after a receipt reversal | Existing finding | be-framework |
| B7 | Seek (keyset) paging, `afterId`/`nextCursor` and `includeTotal`, on stock, products and movements (documents, ledger and audit have it). Today offset paging stops at 10,000 rows (`Limits.MaxOffsetRows`) and a page is at most 200 rows | A11 on those screens, A05 message | Continuous mode is offered on documents, ledger and audit only; beyond 10,000 rows the pager says "narrow the filters" | Recommended when any list can exceed 10,000 rows (test data: 700 products, 335 receipts) | be-architect (cursor for sorted lists), be-framework |
| B8 | Multi-column sort (`sort=date:desc,sku:asc`, at most 3) on documents, stock, ledger, movements | A06 multi-sort | Single sort only; the multi-sort UI stays off (a client-side sort of one page would be wrong) | Recommended | be-framework |
| B9 | Per-user preference store `GET/PUT /api/me/preferences` (versioned JSON, 16 kB) | A06, R06, R12 roaming | Theme, density, views, page size stay per browser (N12) | Optional, needs owner answer P1 | be-architect, be-framework |
| B10 | Cancel and reverse actor and time: `cancelledAt/By`, `reversedAt/By` on `DocumentDto`, or `GET /api/documents/{id}/events` | A17, L07 | Timeline shows "Cancelled"/"Reversed" with the reason but no name and time; the mapper never invents them | Recommended | be-framework |
| B11 | Bulk product lookup `POST /api/products/lookup-many` (<= 200 codes, per code found / inactive / not found) | A15 paste | Paste is capped at 50 lines and uses single lookups (about 4 in parallel) | Recommended | be-framework |
| B12 | Movements per day: `GET /api/reports/movements/daily?from&to&warehouseId` (counts of posted receipts, issues, reversals; `reports.read`, caller's warehouses) | A20 W15 | W15 counts posted documents from the documents list, at most 1,000 documents, and says so | Recommended | be-framework |
| B13 | Stock value history: a nightly snapshot of total value and `GET /api/stock/value-history?days=30` (`valuation.view`; 403/404 otherwise). Correct only after B6/PR-01 is fixed. Roadmap migration MG6 `AddStockValueSnapshot`, built last and only if the charts stay in | A20 W14 | W14 is not rendered (feature-detected); there is no other data source | Required for W14 | be-architect, be-dba, be-framework |
| B14 | Low-stock count by category | A20 W16 | W16 groups the first 200 rows of `GET /api/stock/low-stock` and says so when `lowCount` is larger | Optional | be-framework |
| B15 | `ETag`/`If-None-Match` on `GET /api/documents/counts`, or a notification feed with read state | A18 | The panel polls the counts every 60 s while the tab is visible; "seen" is kept locally | Optional | be-framework |
| B16 | Server CSV export with the same filters, permission-aware columns and an audit event, for the lists that have no Excel export | A10 | Client-built CSV up to 5,000 rows, not auditable | Optional; needed if owner answer P3 is "exports must be auditable" | be-framework, security-engineer |
| B17 | `POST /api/client-errors` for frontend error reports | Q05 | Errors stay in the console | Optional | be-framework |
| B18 | Administrator unlock of a locked account (an action on the Users screen and an endpoint that clears `LockoutUntil` and `FailedLoginCount`, audited, permission `users.manage`). The login spec called this B7 | R25 second line of the lockout banner | The banner shows only when the user may try again; no second line about asking an administrator | Recommended | be-architect, be-framework, security-engineer |
| B20 | Text limits (owner-reported sideways scroll, L00): names are allowed up to 300 characters (`Limits.NameMaxLength`, partners and products), 200 for category and warehouse names, 500 for remarks, 100 for user names, 64 for SKU and barcode, 20 for codes. There is no rule against control characters or bidirectional overrides in names (EA-30 added it for user names only) and no limit on an unbroken run. Recommend 120 for partner and product names, 80 for category, warehouse and display names, the user-name character rules for every name, and a migration report (not silent truncation) of existing rows over the new limit. The product import checks the same limits | L00 (the UI renders today's limits and tests with 500 characters regardless) | A 300-character partner name is accepted and stored; lists rely on L00 containment | Recommended; owner question P12 | be-architect (decide), be-framework |
| B21 | Document corrections, domain (DC-BE1): `Recall`, `WithdrawApproval`, `Cancel` from Approved with a mandatory reason, `MayRecall` and `MayReviewApproved` policies, `allowedActions` strings `recall` and `sendBack`, audit names; about 550 lines, no migration | R28, R31 | The buttons cannot be built | Required (closes NR-02, PR-02) | be-framework |
| B22 | Document corrections, API (DC-BE2): `POST /api/documents/{id}/recall`, `POST /api/documents/{id}/send-back`, `cancel` with a required reason when Approved, idempotent repeats, `If-Match`; about 700 lines; contract batch CS2 | R28, R31 | as above | Required | be-framework |
| B23 | Audit filter by entity type (Role, User) and the `reason` field, entries scrubbed per the cost rule | RP09 | No history links on the access screens | Recommended | be-framework |

### 3.2 Backend-owned findings from the edge and novice retests, and for the security-engineer (not part of this plan)

Backend-owned, listed separately from the frontend triage in 0.3 and 0.4: ER-06 (`from=0001-01-01` returns 500 on five endpoints), ER-07 (`PUT /api/settings` with a partial body writes defaults), ER-08, ER-14, ER-15 (token rights lag up to 15 minutes after demotion or deactivation), ER-21, SEC-08 (lockout by username only) and the unlock action B18. ER-15 is closed by the roles backend slice RP-3 (see 3.3); the web side is RP07.


| ID | Finding | Why it matters here |
|---|---|---|
| S1 | ER-14: authenticated responses carry no `Cache-Control` (ask: `private, no-store`) | Q06 caches some GETs; the builder tests whether the service worker keeps a `no-store` response and the security-engineer approves the cacheable list |
| S2 | ER-15: role change or deactivation took effect only when the 15-minute token ended (SEC-06) | Closed by the roles backend RP-3 (access snapshot, 401 on deactivation, rights fresh within seconds, no refresh revocation on a role change). Until RP-3 is on `main` (wave 12) the web behaves as today; the note on the Users screen is no longer needed |
| S3 | ER-06: `from=0001-01-01` gives 500 on documents, movements, ledger, audit and the export | R23 and A07 reject dates before 2000-01-01 on the client as a courtesy until the API answers 400 |
| S4 | ER-07: `PUT /api/settings` with missing fields writes defaults | none |
| S5 | ER-08: attachments leak existence (403 vs 404) and accept uploads to other users' drafts | none (no attachments screen yet) |
| S6 | ER-21: 6 MB body resets the connection (ask: 413) | none |
| S7 | SEC-08 / EA-24: the sign-in limiter is per address and the lockout is by username only; 18 attempts with an unknown name from one address block every sign-in for 60 seconds | R25's copy explains the wait; the fix (per account and address) is backend and not in this refresh |

Other non-frontend items: `security-engineer` confirms the CSP note for `theme-init.js` (R06), that the CDK overlay positioning works under the strict CSP of `docs/security/web-headers.md` (A01), that local recents, saved views and the notification "seen" set contain no sensitive data and are handled at sign-out (R18, A06, A18), the CSV decision (A10), the re-sign-in dialog (Q05) and the cacheable endpoint list (Q06). `integration-test-engineer` has nothing to do unless B7, B8, B10, B12 or B13 are built, which then need API tests.

### 3.3 Backend readiness: the contracts the frontend waits for

The roadmap (`docs/pm/ROADMAP.md` 3.1 to 3.4) runs the backend in two slots next to the web: BE-1 (the id and schema lane: money PRs, GUID chain, RP-3 onward) and BE-2 (the feature lane: document corrections, hardening, RP-0 to RP-2, RP-4, RP-5). The table gives the first plan wave in which the frontend slice may start, derived from the roadmap's order. A wave is about 1.3 working days.

| Backend work | Contract it gives the web | On `main` / branch by wave | Frontend slice that waits |
|---|---|---|---|
| Money PRs #44 to #46 | ledger balance, one low-stock rule, `reversedBy`, search (CS1) | 2 | R33 |
| Document corrections DC-BE1, DC-BE2 (B21, B22) and opaque cursors (GUID PR-2) | recall, send back, cancel body, new `allowedActions` (CS2) | 5 | R28, R31 |
| GUID PR-3 (`AddPublicIds`, `AddPartnerCode`) on `main`; PR-4 to PR-7 on the integration branch | none for the web until the flip | PR-7 on the branch by 9 | G01 (earliest 9) |
| GUID PR-9 (merge of the integration branch) | ids as strings and opaque cursors on the main branch (the client batch CS3 is inside G01) | 11 | every H slice (G02) |
| Hardening B1 to B3 (Home filters, updated time) | `returned` filter, ascending order, `updatedAt` (CS4) | 12 | R17, R19 (earliest 11, fallbacks exist) |
| Roles RP-0 to RP-2 (no wire change) | none | any time before PR-9 | none |
| Roles RP-3 (after PR-9) | `UserInfo` additive fields, `X-Permissions-Version`, 401 on deactivation (part of CS5) | 12 | RP01 (13), RP07 |
| Roles RP-4a, RP-5 | permissions from the database; permissions and roles endpoints | 15 | RP02 (16) |
| Roles RP-4b, RP-6, RP-9 (promoted) | approval tiers and `AllWarehouses`; role writes with diff and 409 codes; impact preview and reset | 18 to 19 | RP03, RP07, RP11 (19), RP04 (20) |
| Roles RP-7 | overrides and `effective-permissions` | 20 | RP05 (21), RP06 (21), RP08 (22) |
| B23 audit filters | filter by Role and User, reason | 21 | RP09 (22) |
| B1 to B3, B7 to B17 (optional items) | see 3.1 | when built | the slice named in 3.1, each with a fallback |

If a backend lane is late, the slice's slot goes to the next slice in the order of 2.1 (the scheduler is greedy); nothing else waits. The roles ADR is "Proposed" and the plan builds against its recommended defaults (roadmap decision O2); the GUID ADR is "Accepted". Open owner decisions that affect this table are in section 6.

## 4. Definition of done

### 4.1 Per slice (all must be true to merge)

Run from `web/` (Node 22.6 on the dev machine needs `NODE_OPTIONS=--experimental-require-module`, and Prettier `--end-of-line auto` on the Windows checkout; CI uses Node 22.22.3+).

| # | Check | Command / evidence |
|---|---|---|
| 1 | Lint (ESLint, module boundaries, template accessibility, Stylelint, design rules) | `npm run lint` |
| 2 | Format | `npm run format:check` |
| 3 | Unit and contract tests | `npm test`; the slice's tests listed above exist and pass; the i18n completeness test passes (en, zh, ms); a new or changed shared component has a contract spec with keyboard and ARIA cases (from Q08 on; before that, a plain spec) |
| 4 | Contrast | `npm run contrast` (any new colour pair is in the script in the same PR) |
| 5 | Production build within budget | `npm run build:prod`; no budget warning or error (4.3); from Q04 on also `npm run bundle:check`; from Q10 on also `check-no-gallery` |
| 6 | Dependency audit | `npm audit --omit=dev --audit-level=high` (as in `.github/workflows/ci.yml`) |
| 7 | Stub e2e gates (from Q03, Q02, L08 on) | axe 0 serious/critical on every touched registry screen in light AND dark; smoke on Chromium, Firefox and WebKit; visual diffs reviewed (report-only until G3, then blocking); responsive subset green; CLS budget (from Q12) |
| 8 | Real-API e2e | the affected scenarios plus `06-roles`, `10-phone`, `11-accessibility`, `13-cost-visibility` run against a throw-away API and the production build (steps in `e2e/README.md`) |
| 9 | Gallery | a new or changed shared component has its `/__gallery` entry (from Q10 on) with its states (Q10) |
| 10 | Manual checks | 320x568 and 1280 at 400% zoom: no horizontal page scroll, no clipped or overlapped control (FR-03); a Staff and a Clerk walk-through of every new surface (FR-07/D5, table 4.4); a grayscale screenshot for status meaning (FR-08) when chips, banners or charts changed |
| 11 | Acceptance IDs | every ID named in the slice passes |
| 12 | Docs and hand-off | `docs/team/HANDOFFS.md` entry (PROTOCOL format) naming the next role; if a contract changed, `docs/architecture/frontend.md` updated (or listed for R24) |
| 13 | PR size | under about 1,200 changed lines (excluding lockfile, `web/openapi/**`, generated `api-client.ts`; the PR guard stops at 1,500); more than 1,000 during the build means stop and split |
| 14 | No scope creep | no change outside the slice's file list without a note in the PR; the collision rules of 2.3 and 2.4 followed |
| 15 | **Independent review before merge** (`docs/team/PR-REVIEW.md` section 8, decided by the product owner) | passing CI is necessary, not sufficient. Every slice in this plan is over about 200 changed lines, so every slice gets an independent reviewer: an agent that did not write it and has not seen the author's reasoning (it receives the diff and the repository). Slices that touch money, permissions or cost visibility, authentication or security headers, posting, or CI/CD also get the matching specialist (4.5). A Blocker or Major finding is fixed (or accepted in writing by the product owner) before merge; Minor findings become `TASKS.md` items; the orchestrator records the verdict in the PR |
| 16 | **Long-value check** (from L00 on) | a new or changed list, card, chip, dialog, toast, page header, breadcrumb or palette row has a long-value state in its gallery entry (500 characters: unbroken, with spaces, Chinese, emoji) and its screen is in the containment test; a reviewer rejects a literal `white-space: nowrap` and any `overflow-x: hidden` on `html` or `body` |
| 17 | **Motion** (from M01 on, for every slice that animates) | the pattern is in the motion file section 3; tokens only (no literal duration, curve or distance); `data-motion` honoured, Reduced and Off look right (looked at, not assumed); MO-01 and MO-02 are green; the gallery shows the pattern with a replay control (Q10); a reviewer rejects an animated property outside the allow-list, a `will-change` in a stylesheet and `@angular/animations` |
| 18 | **Generated client** | the slice does not run `api:generate` or commit the generated client unless it is the named owner of a batch (2.4: CS1 to CS5); a PR that touches `api-client.ts` or `web/openapi/**` outside a batch is sent back |

### 4.2 Wave gates (QA personas and designer, after a wave)

| Gate | After wave | Who |
|---|---|---|
| G1 | 5 | fast path (waves 1-5) plus L00, RP00, R33, A01; fe-designer light/dark review at 320 and 1280 and motion check on a low-end Android phone and an iPhone; qa-novice (NR-01 on a phone, Partners with 300-character names), qa-power, qa-edge smoke |
| G2 | 13 | sign-in page, document corrections (R28, R31), id flip merged (G01, G02), tables, overlays, density, layout system, motion foundation; QA round 2; qa-novice, qa-power, qa-edge, fe-designer approves the first baselines |
| G3 | 18 | quality net on (Q02, Q03, L08, Q11, Q04); baselines approved; visual, matrix and MO-05 gates flipped to blocking; Home (R17, R19), first access screens, QA round 3 |
| G4 | 23 | roles and person's page (RP01 to RP05, RP07), drawer, combobox, export; QA round 4 with qa-edge leading (lockout, escalation, live rights change) |
| G5 | 27 | lines grid and paste, virtual scroll, phone screens, errors, offline, exceptions, history, approval tier |
| G6 | 32 | charts, print, forced colours, breadcrumbs, motion final, compare, shortcuts; qa-edge, qa-power, fe-designer final screenshots |
| G7 | 34 | final pass of all three personas, full real-API suite, release device checklist, one release of soak before R24 |

Findings go to `docs/qa` and are triaged before the next wave (1.5.3). R24 merges only after G6 and one release of soak.

### 4.3 Bundle size budget

Measured today on main with `npm run build:prod` (Angular build output, raw size / estimated transfer):

| Initial chunk | Raw | Transfer |
|---|---|---|
| `chunk-DhIl4S_k.js` (framework and shared vendor code) | 496.61 kB | 112.50 kB |
| `main-3WXR6LQT.js` | 36.12 kB | 9.86 kB |
| `styles-OOVADWYC.css` | 5.79 kB | 1.12 kB |
| **Initial total** | **538.51 kB** | **123.48 kB** |

Lazy chunks today: 16 files from 0.9 kB to 72.0 kB. The current budget in `web/apps/main/project.json` is warning 700 kB / error 1.2 MB, which would hide a 60% regression. Budget (set in R01, enforced per chunk by Q04):

| Budget | Warning | Error | Note |
|---|---|---|---|
| `initial` | 600 kB | 650 kB | target after R24: at most 583 kB raw (575 plus up to 8 kB of motion code, N16 to N19) and 135 kB transfer; the whole of programme A may add at most 12 kB to the initial total (A01 <= 6 kB; the rest is lazy); the motion programme at most 8 kB (M01 <= 3 kB, each lazy part <= 1.5 kB, no animation library) |
| `anyComponentStyle` | 8 kB | 16 kB | unchanged |
| any lazy chunk | 90 kB raw | 120 kB raw | named allowances in `web/bundle-budget.json`: shared table chunk 90, popover/combobox chunk 40, virtual scroll 45, drag-drop 45, charts 40, palette 40, Home 25 growth |
| per slice | n/a | n/a | a slice adds at most 6 kB raw to the initial total unless it states and justifies more; the palette (R16, R18, R20), the Home widgets (R17, R19), the bell panel (A18) and the charts (A20) are lazy (`@defer`), so they do not count against it |
| global styles | n/a | n/a | at most 11 kB raw (5.79 kB today; 9 kB plus 2 kB for the motion tokens, keyframes and view-transition rules) |

Each PR states the initial total and its biggest lazy chunk before and after in its description (Q04 prints the table). `theme-init.js` is a separate 400-byte file and is not in the initial script budget.

### 4.4 Cost and value on every new surface (D5, FR-07)

| Surface | Staff | Clerk | Supervisor, Owner, Admin |
|---|---|---|---|
| Charts W14 Stock value | not rendered at all (no frame, no skeleton, no text) | not rendered | rendered with `valuation.view` |
| Charts W15, W16 | counts only | counts only | counts only |
| Notifications panel, filter chips, timeline, palette | no amounts | no amounts | no amounts |
| Drawer (peek) and row expansion | no cost, no amount | unit cost and amount (`cost.view`), no valuation | cost and amount; valuation only where the screen already shows it |
| Export (CSV, selected rows) | effective columns only: no cost or value header or cell | cost columns, no valuation | per `columnWhen` of the table |
| Saved views | a saved column the user cannot see is dropped on load | same | same |
| Lines grid and paste | no cost column; pasted cost ignored with a neutral note | cost column on receipts | cost column on receipts |
| Print | no cost, no amount | cost and amount | cost and amount; valuation never |
| Offline cache | the API sends `null`; nothing to hide | same | cached lists contain what the user could see |
| Skeletons, `aria-label`, `title`, tooltips | same rule as the visible column | same | same |
| Roles and access screens (RP) | permission names and sentences only, never amounts; the Access preview says "Can see unit costs: Yes/No; stock value: Yes/No" | same | same. `cost.view` and `valuation.view` are configurable by administrators with a reason and an audit entry; the defaults above do not change, and tests run on the system roles |

### 4.5 Who reviews which slice (independent review gate, row 15 of 4.1)

The reviewer is the fe-architect agent (checklist in PR-REVIEW section 6), started by the orchestrator after CI is green, and never the agent that built the slice. Specialists are added as follows; the reviewer reads the diff, not a summary.

| Slices | Why | Added specialist |
|---|---|---|
| R04, A10, A15, A20, L07 | money, rounding, totals, exports | security-engineer for A10; the money checklist for the others |
| R17, R19, A08, A09, A10, A14, A15, A16, A18, A20, L07, R29 | cost and value visibility (D5) | the cost sweep of qa-edge at gates, plus the DOM money scan in the slice's tests |
| R02, R06, R25, R26, Q05, Q06 | authentication, session, storage, cache, CSP | security-engineer |
| A01 | CSP behaviour of the overlay | security-engineer |
| Q02, Q03, Q11 | CI/CD and the PR guard | security-engineer |
| R27, R28, R31 | posting and submit flow (double submit, recall, send back, cancel on Approved) | be-framework reviewer for the state machine (B21, B22); the Staff and Clerk cost sweep |
| RP00, RP01, RP03, RP05, RP06, RP07 | permissions drive the chrome; role and exception writes; escalation and lockout rules | security-engineer (and the matching ADR reviewer at be-architect for contract questions) |
| G01, G02 | the id flip: every route, store and cursor | security-engineer (IDOR retest, ADR PR-10), be-architect |
| M01, M03, M07 | `theme-init.js` and view transitions; the motion harness | none beyond the frontend reviewer; fe-designer checks the low-end phone |
| every other slice | general | none |

Q slices that only add tests are still reviewed; the review checks that the test would fail if the behaviour were wrong.

## 5. Risks and rollback

### 5.1 Risks

| # | Risk | Slice | Mitigation |
|---|---|---|---|
| K1 | The palette change recolours the entire app at once; a contrast or meaning regression hides in a screen no test opens | R01 | The alias drop-in touches no component; the 162-check gate; axe in both schemes (Q03 makes it a per-PR matrix); fe-designer screenshots at G1 |
| K2 | Radius, line height and icon stroke shift layouts | R05 | Comfortable token values equal today's; 320 and 400% zoom checks; `10-phone` stays green; L08 matrix later |
| K3 | Removing duplicate toasts (R11) removes the only announcement of a failure | R11 | Each failure keeps exactly one announcement (inline alert or panel); EA-15/EA-26 re-test; an e2e that counts `role=alert` equals 1; Q05 keeps it true after retries |
| K4 | `theme-init.js` is blocked by CSP or stale in the service worker, or the flash returns | R06 | Same-origin classic script (allowed by `script-src 'self'`); listed in `ngsw-config.json`; DK-03 test; the script ignores unknown or malformed storage |
| K5 | The palette trigger breaks `Shell.account` in all e2e scenarios | R18 | The locator change ships in the same PR; run all scenarios |
| K6 | Cost or value leaks through a new surface (widget, chart, palette row, drawer, export, print, skeleton column, `aria-label`, `title`) | R15, R17..R20, A08..A20, L07 | Permission-driven rendering only (`*appCan`, `columnWhen`); table 4.4; DOM money scan in the Staff specs; `13-cost-visibility` extended per slice; qa-edge repeats the cost sweep at G3 and G6 |
| K7 | Home call budget (RA-09) or latency regress, or a returned draft is not listed (B1) | R17, R19, A20 | Call-count spec; headings use `counts`; charts deferred; B1 and B2 offered to the backend; widgets fail independently |
| K8 | Money display rounds a stored value with more decimals (average cost has 6) so qty x shown unit cost differs from the shown amount by a cent | R04 | Amounts shown are the server's totals; unit cost fields are inputs limited to 2 decimals; a derived average cost is never multiplied on screen; note to the owner |
| K9 | Single-key shortcuts or a paste handler fire from a barcode scanner (`/` is a valid barcode character; a scan burst can land in a grid cell) | R22, A12, A14, A15 | Shortcuts never fire in an editable control and are off below 1024px; the combobox scan mode detects bursts; a scan into a grid cell is never parsed as a paste; `07-scan` rerun |
| K10 | Parallel PRs conflict in catalogs, `UI_TEXT`, icons, `globals/index.scss`, list pages | all | Section 2.3 and 2.4 rules; append-only lists; R05 pre-lands tokens; L02 and L04 split by file type |
| K11 | Compact density at 1024px with a touch tablet | R12 | `pointer: fine` guard plus 1024px; control hidden otherwise; DN-02, DN-06 |
| K12 | Skeleton delay makes the app feel slower or flicker | R07, R15 | 300 ms delay only avoids flashes, 400 ms minimum only applies once shown; FB-01, FB-02 measured; Q12 keeps CLS at 0 |
| K13 | Bundle growth | all | Budgets in 4.3, lazy palette, charts and CDK parts, stated before and after in each PR, `bundle:check` per chunk |
| K14 | Edge-retest findings not yet assigned | all | Done in 0.3; ER-01 is in Q05; any later finding becomes a slice rather than growing one |
| K15 | Machine-quality zh and ms texts for new copy | all | `TODO-review` marker and the native-speaker review already planned |
| K16 | **The ARIA-grid table (N3) regresses screen-reader behaviour compared with a native `<table>`** | A02 | Roles and counts set explicitly; contract spec; axe on three tables; qa-edge reads the accessibility tree at G1 and G2; A02 is API-compatible, so reverting it restores the native table without touching pages |
| K17 | CDK overlay inside a native modal `<dialog>` (top layer), Esc order and focus differ per engine | A01, A12 | `DialogAwareOverlayContainer`; stub test on Chromium, Firefox and WebKit (Q03); Esc closes the popover first |
| K18 | Virtual scroll: fixed row heights, seek paging cannot jump to the end, screen-reader position semantics | A11 | Opt-in per table; `aria-rowcount`/`aria-rowindex`; documented limits; paging stays the default |
| K19 | Visual baselines churn while the UI is still being restyled and cost reviewer time | Q02 | Report-only until G3; baselines updated in batches by the fe-designer; masks for volatile text |
| K20 | Stubbed fixtures drift from the real API and give false green | Q01 | Fixtures typed from the generated client (a field change breaks compilation); nightly real-API smoke on three engines |
| K21 | The plan is large (98 slices) and some backend items never arrive | all | Every slice has a fallback; the cut list (2.7) removes whole slices without breaking others; each programme is usable alone |
| K22 | Offline cache serves another user's or stale data | Q06 | Few endpoints, one-hour maximum age, cleared at sign-out and when the user id changes, S1 header decision, security-engineer approval |
| K23 | CSV export leaks data or enables spreadsheet formula injection | A10 | Effective columns only, formula guard, 5,000-row cap, server Excel preferred, security-engineer review |
| K24 | Container queries change layout in ways viewport queries did not (size containment, first-paint) | L01, L02, L04 | Band selection is pure CSS (no JavaScript layout, so no first-paint jump); `data-width-band` only for tests and the few TypeScript needs; CLS budget (Q12) and the L08 matrix |
| K25 | Paste and IME: pasted HTML or formulas, composition events trigger searches | A12, A15 | Paste read as plain text only; composition guard in the combobox; specs for both |
| K26 | The quality gates slow the PR loop or flake and get ignored | Q01..Q12 | 15-minute target, stubbed API, one CI retry, quarantine rule with owner and expiry, visual report-only until G3 |
| K27 | New dependencies (`@angular/cdk`, `jest-axe`, `@lhci/cli`) add supply-chain risk | A01, Q08, Q12 | Three approved additions, only the CDK is a runtime dependency, versions pinned, `npm audit` in CI, lockfile reviewed by fe-architect |
| K28 | Estimates are guesses until the first waves report real numbers | all | Re-estimate after wave 3; the cut list and fast path keep value early |
| K29 | The id flip (G01, G02) touches every store, route and spec, and the H slices wait for it | slices marked H or T in 1.10 | The GUID decision is accepted; N13 for all new code; typed stub fixtures turn the flip into compile errors; the G01 wave is exclusive (only tooling slices); H slices wait for G02 by dependency; the one oversized merge (PR-9) needs the `large-change` label and a manual real-API e2e run on the branch first |
| K30 | The sign-in limiter is per address and shared with token refresh (SEC-08), so a few failed attempts block a whole warehouse for a minute | R25 | The copy states the wait and who can help; the limiter fix is backend (S7); not in this refresh |
| K31 | The independent review gate adds a step to every slice and can become the new bottleneck | all | Review starts when CI is green and runs while the next wave is built; reviewers get the diff only; Minor findings do not block; 0.25 day per wave in 2.8 is the budget |
| K32 | The document-corrections backend (B21, B22) or the opaque cursors (PR-2) are late | R28, R31 | R28 is the NR-02 fix and has the earliest start of all backend-dependent slices (wave 5); if late its slot goes to the next slice; the design says no migration (roadmap C2) |
| K34 | Motion makes the app slow or janky on a low-end phone, or leaves reduced-motion users with movement | M01 to M08 and the grown slices | Budgets and gates MO-01 to MO-09 (frame pacing, long tasks, INP at 4x CPU throttle), Lite mode, `data-motion`, fe-designer checks a real low-end Android phone and an iPhone at G1 and G3; MO-05 is report-only until G3; cut order M08, shared element, chart draw-in, M06 |
| K35 | The access programme locks administrators out or lets someone escalate | RP01 to RP11 | Server rules (ADR 2.9) are the protection, the UI shows the reason for every disabled control; RP07 ships before any release of the programme; qa-edge leads QA round 4; the last-administrator and escalation matrices are server tests |
| K36 | The backend lanes (GUID chain, document corrections, roles) are late or run with fewer agents than the roadmap's two slots | R28, R31, G01, RP01 to RP11 | Readiness waves in 3.3; the scheduler is greedy and gives the slot to the next slice; every backend-dependent slice states its fallback |
| K37 | The UX file for roles and the ADR drift apart again | RP01 to RP11 | 1.8.1 is the single answer; ui-ux-designer updates `roles-permissions-ux.md` (warehouse flag per person, two approval tiers, one "High impact" flag, Admin editable except the core, 21 permissions, name length 60); changes go through the architect |
| K38 | Two agents regenerate the generated client at once, or a PR commits it by accident | all | Five named batches, one agent, merged alone (2.4, DoD row 18) |
| K39 | The scope (98 slices, about 15 weeks) is more than the owner wants to carry | all | Fast path first; cut list (2.7) removes 22 slices without breaking others; re-baseline at WD 10 (roadmap) |
| K40 | The plan and the roadmap disagree on capacity (3 web builds vs 2 web slots while both backend slots are busy) | all | N23: the order is the same, only the calendar differs; the project-manager owns the calendar |
| K33 | A long unbroken value (accepted by the API, created by imports or scripts) widens a page again after a new component is added | L00 and every list, card, chip, dialog, toast, header, breadcrumb or palette slice | Containment mixins and the stylelint ban on a literal `nowrap`; the Playwright containment test runs the registry with `longValues` on every PR from Q01/L00 on; a long-value state in each gallery entry (4.1 row 16); B20 limits the input |

### 5.2 Rollback

- Every slice is one pull request that is green alone and independently revertable (`git revert` of the merge commit). No feature flags, with three per-page opt-ins that act as switches: continuous mode (A11), the lines grid (A14, chosen by container width and removable with one line in the editor template), and multi-sort (A06, off until B8).
- Palette: reverting R01 restores the old palette in one step as long as R24 has not merged. R24 is the only point of no return for token names; merge it only after G6 and one release of soak, and keep it a pure rename so it can itself be reverted.
- Table: A02 keeps the public API, so reverting it brings back the native table and breaks no page; A03 to A11 revert in reverse order.
- Motion: the kill switch `data-motion="off"` ships with M01 and turns every animation off without a deploy of code; each motion pattern lives in the slice that owns it and reverts with it; the removal of `@angular/animations` (M01) is reverted by re-adding the package, nothing else imports it.
- Access: the roles screens only talk to the new endpoints; reverting RP02 to RP11 hides the screens and leaves the backend rules in place; RP00 (the profile helper) is independent and stays.
- Id flip: the integration branch is merged only after G02 is green; the schema half is already on `main` and inert, so reverting PR-9 needs no database step (roadmap 3.4).
- Document corrections: R28 and R31 show buttons only from `allowedActions`; if the backend commands are reverted the buttons disappear by themselves.
- CDK: reverting A01 is possible only after the slices that import it (A03, A12, A16, A18, L02) are reverted; the package stays in the lockfile until then.
- Storage: new per-user keys (`ims.pref.<userId>.*`, including `views.*`, `table.*`, `nav.collapsed`) are new names; the device keys `ims.theme` and `ims.density` keep their meaning, so an older build reads them correctly. A revert leaves unused keys, which is harmless; a saved view with an unknown version is ignored.
- Visual baselines: the `visual-baselines` branch can be deleted at any time; the jobs then report "no baseline" and do not block until it is recreated.
- Service worker: a revert of R06, L03 or Q06 changes `index.html`, `theme-init.js` or `ngsw-config.json`; the update prompt delivers it on the next visit; cached lists are cleared at sign-out.
- R04 and R23 change what users type; the API still accepts what it accepted before, so a revert strands no data.
- If a slice fails its QA gate after merge, revert it, record the finding in `docs/qa`, and re-plan only that slice; later slices that depended on it wait (the dependency column in 1.2 shows which).

## 6. Questions for the product owner (defaults are used if no answer comes)

| # | Question | Default taken |
|---|---|---|
| P1 | Should theme, density, saved views and page size follow the user to another device (needs B9)? | No: per browser |
| P2 | Do you want bulk approve, reject or cancel of documents? (override reasons, self-approval and per-document rules make this a business decision) | No (N8) |
| P3 | Must every export be auditable (B16 server CSV), or is the browser-built CSV up to 5,000 rows acceptable? | Browser CSV plus the existing server Excel exports |
| P4 | Chart axes: keep FR-05 strictly (full numbers with 2 decimals) or allow shorter axis labels later? | Strict |
| P5 | Company name or address on printed receipts and issues? | None |
| P6 | Is it acceptable that on desktop a click on a non-draft document opens the side peek and the full page is one more click? | Yes (drafts still open the editor directly) |
| P7 | Is a manual iOS Safari and Android Chrome check before each release acceptable (checklist in Q03)? | Yes |
| P8 | NR-02 (closed): decided by `docs/design/document-corrections.md`; the three confirmations (only the approver undoes an approval; Owner and Admin may send back or cancel an Approved Large document; a reason of 5+ characters to cancel an Approved document) use their defaults | Defaults taken; a later change is a small edit |
| P9 | NV-07: restore unsaved lines after a reload? (the login spec forbids writing drafts to browser storage because cost data would stay on a shared device) | No: the browser warns before leaving; a session ending is handled by R26 |
| P10 | EA-30: the API accepts the same product on two lines. Keep or reject? | Keep; the editor and paste merge the quantity into the existing line |
| P11 | Sign-in page: is there a privacy notice, terms or company logo to show, and may the page promise "Made for barcode scanners and phones"? (login spec section 10) | No link; the claim is reworded to phones and tablets |
| P12 | Maximum lengths of names (B20): keep 300 or tighten to 120 for partner and product names and 80 for the other names? | Tighten to 120 and 80 for new input; existing data keeps working and the UI copes with 300 |
| P13 | Motion questions M1..M8 | **Accepted by the owner with every default** (in-app "Animations: follow device / reduce", press 0.97 and hover lift, Home count-up of integers, shared element behind a flag, pull to refresh last, dialog overshoot as proposed, sign-in entrance on every load, automatic Lite mode) |
| P14 | Roles questions (the ADR's Q1 to Q7 and the UX file's Q1 to Q8, merged by the roadmap as O2): one role per person plus exceptions; grants end within 180 days, restrictions may be permanent; only Admin holds `roles.manage` at install; cost visibility configurable with a reason and an audit entry; two approval levels; refuse to deactivate a role in use; "sees every warehouse" per person | Defaults taken (owner answer wanted by about WD 8) |
| P15 | Hosting direction and the `large-change` label for PR-9 | Roadmap section 8.2 (O3, O4); not a frontend question |
