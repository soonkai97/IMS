# IMS 2026 design refresh - UX specification

Author: ui-ux-designer. Date: 2026-10-11. Status: ready for fe-designer (tokens, component look), fe-architect (contracts, slices) and fe-framework (build).
Builds on `docs/design/mvp-ux.md` (flows, roles, wireframes, copy). Where this file and `mvp-ux.md` differ, this file wins for the items it names (section 0.3); everything else in `mvp-ux.md` stands.
Visual tokens and the dark palette belong to the visual designer's spec (reference it by token name only; this file never names a hex value). No code is written here.

## 0. How to read this

### 0.1 Scope: five items the product owner chose

| # | Item | Priority | Section |
|---|---|---|---|
| 1 | Role-aware screens | Must (Staff/Clerk Home), Should (the rest) | 2 |
| 2 | Calm, clear design tokens | Owned by the visual designer; this file only lists what the UX needs from them | 3 |
| 3 | Proper dark mode (behaviour) | Must | 4 |
| 4 | Density option and keyboard-first desktop with a command palette | Should (density, palette), Could (extra shortcuts) | 5, 6 |
| 5 | Restrained feedback motion | Must (loading, reduce-motion), Should (button success state) | 7 |

Also specified because they serve the five items: progressive disclosure (8), empty states (9), table and list patterns (10), do-not-do list (11).

Rejected on purpose (product owner): glassmorphism and heavy effects, an AI chatbot, layouts that rebuild themselves, decorative animation. They are not "later"; do not add them.

### 0.2 Fixed rules (whatever the trend) and how a tester verifies them

Every slice in section 12 must keep these true. They are the global acceptance criteria.

| ID | Rule | How to verify |
|---|---|---|
| FR-01 | WCAG 2.2 AA: text 4.5:1, large text and UI parts 3:1, in light AND dark | `npm run contrast` passes; axe scan (e2e `a11y` scenario) has 0 serious/critical on every screen touched, both themes |
| FR-02 | Phone touch targets 44px minimum, 48px for primary controls (existing `touch` / `touch-min` tokens) | Measure in the browser at 360px: every button, link, tab, chip with action, checkbox label, palette row and menu row is >= 44px high and wide (or has >= 44px hit area) |
| FR-03 | Nothing breaks at 320px width or 400% zoom: no horizontal page scroll, no clipped or overlapped controls, focused control never hidden by a sticky bar | Open every touched screen at 320x568 and at 1280 wide with browser zoom 400%; scroll width equals client width (a wide data table may scroll inside its own labelled region, existing exception) |
| FR-04 | Nothing is slower to scan than today | Section 0.4 budget table: tap/keystroke counts and "first useful content" are equal or better |
| FR-05 | Money shows exactly 2 decimals everywhere (Home tiles, widgets, tables, dialogs, exports), thousands separator, never abbreviated ("12.4k" is forbidden); a quantity shows up to 3 decimals, trailing zeros trimmed (D10) | Search the touched screens for any amount without 2 decimals; Home value tile reads `119,371.90` not `119k` |
| FR-06 | Required field: red asterisk visible, hidden from screen readers (`aria-hidden`); the control carries `required` / `aria-required`, so the state says "required" (the label has no sr-only word: ruling of the R02 review); plus a visible legend "* means required" on every form that has a required field | Section 3.3 acceptance list; screen reader reads "Email or username, required, edit text", never "star" |
| FR-07 | Warehouse staff never see cost or value; Clerk sees cost (`cost.view`) but never valuation (`valuation.view`); decided by permission, never by role name (`mvp-ux.md` 1.4) | Section 0.5 table: sign in as Staff and Clerk, walk every new surface (Home widgets, palette, skeletons, tooltips, aria-labels, exports) and find no cost or value |
| FR-08 | Colour is never the only carrier of meaning (status chips, errors, required, low stock keep icon + text) | Grayscale screenshot test of Home, a list and a document |
| FR-09 | Honour `prefers-reduced-motion`, `prefers-color-scheme`, `forced-colors` | Section 7.7 and 4 acceptance lists |

### 0.3 What this file changes in `mvp-ux.md` (supersedes)

| Where | Was | Now |
|---|---|---|
| 2 `app-form`: required marker | "(required)" in the label | Red asterisk (aria-hidden) + `required` state on the control + legend (3.3) |
| 1.2 last bullet: Home | One paragraph per role | Widget lists per role (2.3) |
| 2 `app-toast`, 6.3 "Business rule violation ... plus an error toast with the same text" | Inline message AND toast | Inline message only (an alert); no duplicate toast (7.4). QA EA-15 and EA-26 found double announcements |
| 6.1 skeleton | 5 grey rows after 300ms | Shapes per screen type, shown after 300ms, stay at least 400ms (7.1) |
| 2 `app-list` | no density | Comfortable or compact on desktop (5) |

### 0.4 Scan-speed budget (FR-04)

| Task | Today | After the refresh | Rule |
|---|---|---|---|
| Staff opens New receipt from Home (phone) | 1 tap | 1 tap, same position | Receive and Issue stay the first two controls after the greeting |
| Staff finds a returned draft | Home, Receive list, filter, open (about 4 taps) | Home, tap the row in "Returned to me" (2 taps) | W3 lists the items |
| Supervisor opens the oldest waiting approval | Home, Approvals, row (2 clicks) | Home, row in "Needs my approval" (1 click) | W6 |
| Supervisor finds document RCV-2610-0007 | Menu, Receipts, search, Enter, open (5 steps) | Ctrl+K, type 4 characters, Enter (3 steps) | Palette |
| Switch to dark | User menu, choose (2 taps) | same (or palette: Ctrl+K, "dark", Enter) | Menu path never removed |
| Any list | First rows visible without scrolling | Same or more rows (compact option adds rows) | Home widgets never push the primary actions below the fold on a 360x640 phone |

### 0.5 Cost and value on every new surface (FR-07)

| Surface | Staff | Clerk | Supervisor / Owner / Admin |
|---|---|---|---|
| Home W6 amount column | not rendered (no column, no skeleton for it) | amount shown (`cost.view`) | amount shown |
| Home W9 Stock value | not rendered | not rendered | shown (`valuation.view`) |
| Home W12 Rules in force | not rendered | not rendered | Admin only (`settings.manage`) |
| Palette results | no amounts anywhere | no amounts | no amounts (a palette row never carries money; keeps the rule trivially true) |
| Skeletons | only columns the user can see | same | same |
| Recents, `aria-label`, `title` | no amounts | no amounts | no amounts |
| `null` from the API | render nothing; no `0.00`, no dash | same | same |

## 1. Evidence this design answers (QA)

| Finding | What it asked | Where answered |
|---|---|---|
| NV-08, NV-10, NV-11 | Phone lists too tall; one-tap path to drafts | 2.3 (W3, W4 rows), 10.2 |
| NV-04 | Highlight the changed line | 7.5 row highlight |
| NV-21 | Spinner on the tapped button, toast out of the way | 7.2, 7.4 |
| EA-15, EA-26 | One announcement per failure | 7.4 |
| EA-21 | Target size 24px desktop, 44-48 phone | FR-02, 5.4 |
| PS-01 residual | Reverse dialog keeps the typed reason on failure | 7.3 dialog errors |
| PS-13 | Only Previous/Next | 10.3 (proposal, open question) |
| PS-10/PR-10 | Low stock order | 2.3 W7 note |
| PR-06, PS-22 | SKU and date wrap, wide report | 10.1 |
| PS-05, NV-03 | Search focus, scanner path | 6.3 (palette uses the same `data-autofocus` rule) |

## 2. Item 1 - Role-aware screens

### 2.1 Goal

Each role lands on a Home that shows only what that role can act on, in the order they act, using data the API already returns. Nothing is customised by the user and nothing rearranges itself. The layout for a role is fixed and the same on every visit.

Priority: Must for Staff and Clerk Home (they are the phone users and the first-day users), Should for Supervisor, Owner, Admin.

### 2.2 Exact behaviour

1. The Home layout is chosen from permissions, never from the role name (same rule as cost, `mvp-ux.md` 1.4):

   | Home profile | Rule (all must hold) | Roles today |
   |---|---|---|
   | Doer | `document.create` and not `document.approve` | Warehouse staff, Clerk |
   | Reviewer | `document.approve` and `document.post` | Supervisor, Admin |
   | Overseer | `document.approve` and not `document.post` and not `document.create` | Owner |
   | Admin extras | `settings.manage` adds W12 and W13 to the Reviewer profile | Admin |

   A user whose permissions match none (for example a future read-only role) gets W1 plus the stock widgets they can read (W7, W9 if permitted).
2. Order = importance of action: things to do first (W2 to W6), problems next (W7), plain numbers last (W8 to W10). Within a list, the oldest waiting item is first (approvals) or the worst shortage is first (low stock).
3. DOM order equals visual order equals reading order. No CSS `order` re-sequencing. Two-column desktop layout fills left to right, row by row.
4. Each widget loads and fails on its own: its own skeleton (7.1), its own error row with Retry, its own empty line. One failing call never blanks the page.
5. A widget with nothing to act on: Doer widgets W3 and W5 are hidden at zero (the page stays short); Reviewer widgets W6 and W7 stay and say "All clear" (a supervisor checks them every morning and needs to see that they were checked). W4 hides at zero.
6. A list widget shows at most 3 rows on a phone and 5 on desktop, then a "See all" link to the full screen.
7. Tapping a row opens the document (or product stock page) directly. Counts and headings are not tap targets except "See all".
8. Counts come from `GET /api/documents/counts` and `GET /api/stock/summary` (already loaded today); previews come from the list endpoints named in the table. No new endpoint is needed. Optional additions are flagged "API?" in 2.4.
9. Greeting: "Hi, {name}" and the warehouse scope line ("Main WH", or "{n} warehouses", or "All warehouses" for Admin and Owner). No time-of-day greeting, no dates, no motivational text.

### 2.3 Widget catalogue

Visible-when uses permission names from `PERMISSION`. "Money" says whether an amount can appear.

| ID | Widget | Data (existing endpoint) | Shows | Visible when | Money | Rows / link |
|---|---|---|---|---|---|---|
| W1 | Greeting | `PermissionService` display name, warehouse names (cached master data) | "Hi, Siti", "Main WH" | always | no | none |
| W2 | Quick actions | none | Two large buttons: Receive stock, Issue stock (Reviewer: same two as secondary buttons in the page header) | `document.create` | no | New receipt / New issue |
| W3 | Returned to me | `counts.returnedToMe` + `GET /api/documents?mine=true&status=Draft` filtered by `isReturned` | Heading with count chip; rows: document no, type icon, "Returned" chip, party, age | `document.create` and count > 0 | no | 3 phone / 5 desktop; row opens document; See all goes to the list (Receipts or Issues by the first row's type) |
| W4 | My drafts | `counts.myDrafts` + same list (drafts not returned) | Rows: no, type, party, qty, saved time | `document.create` and count > 0 | Clerk: amount (cost.view); Staff: none | same |
| W5 | Waiting for approval - mine | `GET /api/documents?mine=true&status=Submitted&pageSize=3` | Rows: no, "Awaiting approval" or "Ready to post", age. Tells the creator where their work is | `document.create` and total > 0 | Clerk: amount; Staff: none | row opens document (they can still Cancel to recall) |
| W6 | Needs my approval | `counts.approvalsWaiting` + `GET /api/documents?status=Submitted&requiresApproval=true` filtered by `canAct`, oldest first | Heading + count (attention style when > 0); rows: no, type, by whom, flags (Override, Large), waiting time, amount where `cost.view` | `document.approve` | amount with `cost.view` | 5 desktop / 3 phone; Review opens the document. "All clear: nothing waiting for you." at zero |
| W7 | Low stock | `summary.lowCount` + `GET /api/stock/low-stock?pageSize=5` | Heading + count; rows: product name, SKU, warehouse, on hand (largest text), Negative or Low chip, shortage; **Receive** shortcut per row for roles with `document.create` | `stock.read` | no | See all opens Low stock. Order is the API order (largest shortfall); backend PR-10 asks for percentage, not a UX dependency |
| W8 | Posted today | `counts.postedToday` | One number with label | Reviewer | no | none (numbers are not links; no dead tiles) |
| W9 | Stock value and products | `summary.totalValue`, `summary.skuCount` | Two numbers: "Stock value" (exactly 2 decimals) and "Products with stock" | value: `valuation.view`; products: `stock.read` | value only with `valuation.view` | link to Stock |
| W10 | Recent movements | `GET /api/reports/movements?pageSize=5` (newest first) | Rows: time, document no, SKU, signed Stock effect, Reversal badge | `reports.read` | none shown (no unit cost or amount in this widget) | See all opens Reports |
| W11 | Recently posted by me | `GET /api/documents?mine=true&status=Posted&pageSize=3` | Rows: no, type, posted time. Footer line: "Wrong quantity? Ask a supervisor to reverse a posted document." (NV-05 copy) | Doer | no | collapsed by default on phone ("Show recent posts"); open on desktop |
| W12 | Rules in force | `GET /api/settings` (via `SystemSettingsStore`) | Three plain lines: "Negative stock: Allowed / Not allowed", "Approve own documents: Allowed / Not allowed", "Low-stock alert cool-down: {n} h" | `settings.manage` | no | Edit goes to Settings |
| W13 | Go to | `MENU_ITEMS` filtered by permission | Up to 5 links: Reports, Audit log (Owner and Admin), Users, Settings (Admin) | Overseer, Admin | no | none |

### 2.4 Role layouts (widgets in order)

Widths: phone = one column; desktop >= 1024px = two columns (content max 76rem) unless stated.

| Role (profile) | Order on phone | Order on desktop (row by row, left then right) |
|---|---|---|
| Warehouse staff (Doer) | W1, W2, W3, W5, W4, W7 (3 rows), W11 (collapsed) | row 1: W1 + W2 (actions on the right as buttons); row 2: W3 + W5; row 3: W4 + W7; row 4: W11 |
| Clerk (Doer) | same as Staff | same as Staff. Differences: button labels say Submit instead of Submit & post inside documents (existing), amounts visible in W4 and W5 |
| Supervisor (Reviewer) | W1, W6, W7, W3, W4, W8, W9, W10 | row 1: W1 + W2 (secondary buttons); row 2: W6 (wide, 2 columns); row 3: W7 + W3/W4 stacked; row 4: W8 + W9 side by side as numbers; row 5: W10 (wide) |
| Owner (Overseer) | W1, W6 (large only), W7, W9, W10, W13 | row 1: W1; row 2: W6 + W7; row 3: W9 (numbers) + W13; row 4: W10 (wide). No W2, W3, W4, W8 (the Owner neither creates nor posts) |
| Admin (Reviewer + extras) | W1, W6, W7, W12, W3, W4, W8, W9, W10, W13 | Supervisor's rows, plus W12 beside W9 in the numbers row and W13 at the end |

Phone wireframe, Warehouse staff (360px):

```
+------------------------------------+
| IMS                       (Siti)   |
+------------------------------------+
| Hi, Siti                           |
| Main WH                            |
| +----------------+ +-------------+ |
| |  [+]           | |  [-]        | |
| |  Receive stock | |  Issue stock| |
| +----------------+ +-------------+ |
|                                    |
| Returned to me                  (2)|   <- hidden when 0
| RCV-2610-0006  [Returned]          |
|   Best Supply            2 h ago   |
| ISS-2610-0011  [Returned]          |
|   Delta Co.              1 d ago   |
| My drafts                       (1)|
| RCV-2610-0007  Acme Trading        |
|   120 PCS            09 Oct 14:05  |
| Low stock                      (3) |
| Widget A blue  WIDGET-A     [LOW]  |
|   Main WH                   12 PCS |
|                         [Receive]  |
| See all low stock                  |
| v Show recent posts                |
+------------------------------------+
|Home|Receive|Issue|Stock|More       |
+------------------------------------+
```

Desktop wireframe, Supervisor (1280px):

```
+-------------------------------------------------------------------------------------------------------+
| IMS      | [ Search or run a command...  Ctrl K ]                                       Siti (Super)    |
|----------+--------------------------------------------------------------------------------------------|
| Menu     | Hi, Siti  | Main WH                                    [ Receive stock ] [ Issue stock ]     |
|          |--------------------------------------------------------------------------------------------|
|          | Needs my approval (3)                                                         See all      |
|          |  ISS-2610-0012  Issue    Ali   [OVERRIDE]                  2 h      5,000.00 est.          |
|          |  RCV-2610-0009  Receipt  Ali   [LARGE]                     1 d     12,400.00               |
|          |  ISS-2610-0010  Issue    Mei                               3 d        310.00 est.          |
|          |--------------------------------------------------------------------------------------------|
|          | Low stock (62)                    See all    | Returned to me (1)                           |
|          |  Washer 8mm  WASH-8  Main WH   0   [LOW]     |  RCV-2610-0006 [Returned] Best Supply  2 h  |
|          |  Widget A    WIDGET-A  Main WH  12  [LOW]    | My drafts (2)                               |
|          |  Bolt M8     BOLT-M8   Store B  60  [LOW]    |  RCV-2610-0007  Acme Trading  120 PCS       |
|          |--------------------------------------------------------------------------------------------|
|          | Posted today          Stock value                 Products with stock                         |
|          |  335                  54,875.50                   303                                         |
|          |--------------------------------------------------------------------------------------------|
|          | Recent movements                                                                   See all     |
|          |  11 Oct 08:51  RCV-2610-0033  QA-002   +600     |  11 Oct 08:40  ISS-2610-0002  QA-002 -60   |
+-------------------------------------------------------------------------------------------------------+
```

The amount column in W6 is absent for a role without `cost.view` (the row simply ends at the waiting time).

### 2.5 Other screens that are role-aware (already true; the refresh keeps them and adds nothing)

| Surface | Rule |
|---|---|
| Phone tab bar | Four role tabs plus More (`PHONE_TABS_BY_ROLE`); unchanged |
| Menu items | From permissions (`MENU_ITEMS`); unchanged |
| List default | Mine for Staff and Clerk, All for others, remembered per user; unchanged |
| Primary page action | Hidden when the user cannot create (Owner); unchanged |
| Document buttons | From `allowedActions` only; unchanged |
| Empty-state wording | Owner gets "No receipts to show." (no create CTA); unchanged |
| Palette content | Built from the same permission filter (6.2) |
| Columns | A column the user may not see is not rendered, not blank (`columnWhen`); skeletons follow |

### 2.6 API gaps (all optional; Home works without them)

| Need | Why | Without it |
|---|---|---|
| `rejectionReason` on `DocumentListItem` | W3 could show the reviewer's reason in one line | Row shows the "Returned" chip only; reason is one tap away on the document |
| `returned=true` filter on `GET /api/documents` | W3 would be exact | Client filters the newest 25 own drafts; a user with more than 25 drafts and an old returned one could miss it. Acceptable for MVP; flagged |
| Draft counts per type (`myDraftReceipts`, `myDraftIssues`) | "See all drafts" could open the right list | See all opens Receipts or Issues by the first row's type; imperfect, noted as open question Q5 |
| Count of products with zero cost (PR-07) | Admin widget "Stock without cost" | Not built in this refresh |

None of these block a slice.

### 2.7 Acceptance criteria (item 1)

| ID | Criterion |
|---|---|
| RA-01 | Signing in as each of the five roles shows exactly the widgets listed in 2.4, in that order (DOM order checked with a test) |
| RA-02 | The profile is derived from permissions: a test user given `document.approve` + `document.post` gets the Reviewer layout whatever the role name |
| RA-03 | Staff Home contains no money, no `0.00`, no "value", in any widget, `aria-label` or `title` (FR-07). Clerk Home shows amounts only in W4 and W5 and never a stock value |
| RA-04 | Owner Home has no Receive/Issue buttons and no "Posted today" |
| RA-05 | On a 360x640 phone, for Staff, Receive and Issue are visible without scrolling and are >= 56px high; the first Low stock row is reachable with one scroll |
| RA-06 | Each widget has a skeleton, an error row with Retry and (where specified) an empty line; making one request fail leaves the others intact |
| RA-07 | A row in W3, W4, W5, W6 opens the right document with one tap or click; keyboard: Tab reaches each row, Enter opens |
| RA-08 | W6 and W7 show a counted heading that equals the sidebar badge and the list total (existing number checks stay green) |
| RA-09 | Home makes at most 3 API calls for a Doer and at most 7 for a Reviewer on first load |
| RA-10 | At 320px and 400% zoom the layout is one column with no horizontal scroll; two-column layout starts at 1024px |
| RA-11 | A user without a warehouse still sees the "You aren't assigned to a warehouse yet. Ask an Admin." empty state instead of widgets (existing) |
| RA-12 | Widgets appear in the same place on every visit (no personalisation, no reordering by usage) |

## 3. Item 2 - Calm, clear design tokens (UX needs only)

The visual designer writes the token and palette spec. This section lists what the behaviour in this file relies on, so the two specs meet. Everything is by token name; component styles never hard-code values (`scss-design.md`).

### 3.1 What the UX needs from the tokens

| Need | Why | Token (existing name or "new") |
|---|---|---|
| Text, muted text, surfaces, borders pass AA in both themes | FR-01 | existing `text`, `text-muted`, `surface`, `surface-2`, `border` |
| A danger colour that gives 4.5:1 for the required asterisk on `surface` AND on `surface-2` in light and dark | FR-06 | `danger` (check on both surfaces; add `on-surface-danger` if `danger` fails on a dark surface) |
| Status colours with an `on-` pair for chips, each AA | FR-08 | existing `status-*`, `on-status-*` |
| Focus ring >= 3:1 against any surface in both themes | FR-01 | existing focus-ring mixin |
| Skeleton colour distinguishable from surface by >= 1.3:1 but not shouting | 7.1 | existing `skeleton` |
| Row-highlight tint (changed line) readable under text | 7.5 | new `highlight` and its `on-` pair |
| Density variables: row min-height, cell padding, filter gap | 5 | new CSS custom properties `--ims-density-*` set on `<html data-density>` (names are fe-designer's call; the two values per variable are in 5.3) |
| Motion tokens: durations and easings in 7.6 | 7 | existing `duration('fast')` 120ms, `duration('base')` 200ms, `easing('smooth')`; new: `easing('enter')`, `easing('exit')`, `duration('hold')` 1500ms for the success tick |
| `color-scheme` and the PWA `theme-color` per theme | 4 | new meta values per theme |
| Money, quantity, SKU use tabular figures | Scan speed (right-aligned columns line up) | font feature, not a colour |
| Calm: one accent colour, shadows only on overlays (dialog, palette, toast), radius from the existing scale | Product owner's "calm" | visual designer |

### 3.2 Hard limits on the visual look (UX veto)

1. No blur/backdrop-filter on any surface, no gradients behind text, no animated backgrounds. A single dim scrim behind dialogs and the palette is allowed.
2. Body text 16px minimum; table text may be 14px only when density is compact (5.3) and never smaller; inputs 16px (prevents mobile zoom-in).
3. Status and flag chips stay icon + text.
4. No colour-only difference between selected/unselected (add an indicator: check, underline, left bar).
5. The danger colour is not used for anything except errors, destructive buttons and the required asterisk.

### 3.3 Required marker (FR-06) - behaviour

Applies to every field in `app-form-field`, radio groups and select controls, in pages and dialogs.

1. Label text, then a single space, then a red asterisk `*` rendered by CSS content or a span with `aria-hidden="true"`. No visually hidden word is added to the label: the name is the label alone ("Quantity") and the `required` state of item 2 supplies the word, in the language of the browser or screen reader (a second, translated word in the label would be announced twice, as "required required"). Result: "Quantity, required, edit text".
2. The control itself has `required` (native) or `aria-required="true"` (custom). This is the only carrier of the word "required" for a single control.
3. Group controls (radio group "Purpose"): the `<legend>` carries the asterisk plus the sr-only word "required" (translated from `ui.required`), because a group has no single control to carry the state; the individual radios do not carry `required` (it would be announced once per option).
4. The legend line "* means required" is shown once per form, directly under the page header or dialog heading and above the first field, in muted text at 14px or larger and 4.5:1. Its asterisk is the same red as the field asterisks. The legend element is `aria-hidden="true"`: it explains a visual symbol that assistive technology never encounters, and the same information is delivered per field by the sr-only word.
5. A form with no required fields shows no legend. Search and filter boxes are never marked.
6. Optional fields are not labelled "(optional)" except where a missing value surprises users (Receipt "Supplier": hint text "Optional" in the hint line, not in the label).
7. The error message for an empty required field says what to do ("Enter a quantity above 0.", "Choose a purpose."), is linked with `aria-describedby`, and does not repeat the word "required".
8. The old "(required)" text in the label is removed everywhere (`mvp-ux.md` 2 `app-form` is superseded).

Copy (add to the i18n catalog, en first): `form.requiredLegend` = "* means required"; `ui.required` (existing) = "required" used only in the sr-only span.

Wireframe:

```
 New issue
 * means required

 Warehouse *            Main WH <v>
 Purpose *              (o) Sale  ( ) Use  ( ) Scrap
 Customer               Delta Co. <v>
 Reference              [ SO-8812          ]
```

Acceptance:

| ID | Criterion |
|---|---|
| RQ-01 | Every required control on Login, Receipt, Issue, Product, Warehouse, Partner, User, Settings forms and every dialog with a required reason has the visible red `*` and `required`/`aria-required` |
| RQ-02 | `*` is `aria-hidden`; the accessible name of a single required control is the label alone (no "required" in it) and the control exposes the required state (`required` / `aria-required`), so "required" is announced once; a required radio group is named "Purpose required" once; NVDA/VoiceOver never reads "star" or "asterisk" for a field |
| RQ-03 | The legend "* means required" appears once above the first field of each such form and in dialogs with a required field; it does not appear on forms without one |
| RQ-04 | Contrast of the asterisk is >= 4.5:1 on `surface` and `surface-2` in light and dark |
| RQ-05 | The text "(required)" no longer appears in any label |
| RQ-06 | At 320px the label, asterisk and sr-only text wrap with the label; the asterisk never sits alone on a line |

Priority: Must. Smallest slice, ships first (S1).

## 4. Item 3 - Dark mode behaviour

### 4.1 Goal

The app looks right in the user's chosen light or dark theme from the first paint, follows the device by default, and remembers an explicit choice per user. The palette itself belongs to the visual designer.

### 4.2 Exact behaviour

1. Default for a new user and for the login page: "Use device setting" (follow `prefers-color-scheme`). While in this mode, a change of the device theme while the app is open takes effect immediately, without reload.
2. Override: the user menu (account dialog, existing "Theme" radio group renamed "Appearance") offers three choices: "Use device setting" with the current result as a sub-line ("Now: Dark"), "Light", "Dark". The choice applies instantly with no transition (no cross-fade; a colour fade would show a half-themed screen).
3. Remembered per user in this browser: stored under the signed-in user's id (same pattern as the list-scope preference). Two people sharing one tablet each get their own choice. Before sign-in (login page) and after sign-out, the device setting applies.
4. No flash of the wrong theme: the theme attribute is applied before first paint. The strict CSP forbids inline script, so the apply step is a tiny same-origin script file loaded synchronously in `index.html` that reads a device-level copy of the last chosen mode (written whenever the preference changes) and sets `data-theme` before the stylesheet paints. The stored mode "system" sets nothing (the stylesheet's `prefers-color-scheme` rule decides). security-engineer to confirm the CSP note (`docs/security/web-headers.md`).
5. `color-scheme` is declared for both themes so native controls (scrollbars, date picker, select popup, spin buttons) match. `<meta name="theme-color">` is set per theme (light and dark values from the visual designer) so the phone browser bar and the installed PWA title bar match.
6. Cross-tab: changing the theme in one tab updates other open tabs of the same user (storage event).
7. Forced colors (Windows High Contrast): the app must remain usable; borders and focus rings use system colours, chips keep their icons. Should.
8. Not in scope: auto-switch by time of day, per-screen theme, any theme besides light and dark, brightness slider.
9. Palette and user menu both offer the same three choices (palette actions: "Appearance: Use device setting", "Appearance: Light", "Appearance: Dark").
10. Images and icons: icons use `currentColor`; no raster illustrations that assume a white background. Charts are not in the product.
11. Print/export are unaffected (Phase 2 print uses the light palette).

### 4.3 Screens affected

All. Directly touched: account dialog (shell), `index.html`, `ThemeService`, palette actions.

### 4.4 Copy

| Key | en |
|---|---|
| shell.appearance | Appearance |
| theme.system | Use device setting |
| theme.systemNow | Now: {mode} (mode = Light or Dark) |
| theme.light | Light |
| theme.dark | Dark |

### 4.5 Acceptance criteria

| ID | Criterion |
|---|---|
| DK-01 | New user, device in dark: app opens dark; device switched to light with the app open: app turns light without reload |
| DK-02 | User A picks Dark; signs out; user B signs in on the same browser: B sees their own setting (default: device); A signs back in: Dark again |
| DK-03 | With "Dark" chosen and a hard reload, there is no light flash before the dark theme (check with a throttled CPU recording, first frame is dark) |
| DK-04 | Native controls (date input popup, scrollbars, select list) are dark in dark theme |
| DK-05 | `npm run contrast` passes for light and dark; axe 0 serious/critical on Home, a list, a document, a dialog and the palette in both themes |
| DK-06 | Status chips, flags, banners, toasts, skeleton, focus ring, row highlight and disabled buttons are legible and AA in dark (screenshot review by the visual designer plus the contrast script) |
| DK-07 | The Appearance control shows the current result when "Use device setting" is selected |
| DK-08 | Changing the theme in tab 1 changes tab 2 within a second |
| DK-09 | With storage blocked (private mode) the app still works, follows the device setting, and the choice lasts until reload |
| DK-10 | In forced-colors mode every control and chip is distinguishable and focus is visible |

Priority: Must (S2).

## 5. Item 4a - Density option

### 5.1 Goal

Desk users who read long lists can fit more rows on screen. Nobody is forced to change; phone users are unaffected.

### 5.2 Exact behaviour

1. Two values: **Comfortable** (default, equals today) and **Compact**. Stored value names: `comfortable`, `compact`. (Implementation note: `UiTableComponent` already has an unrelated `$compact` meaning "phone card has end-aligned fields"; rename one of them so the word means one thing.)
2. Applies only at viewport width >= 1024px, to: data tables (`app-table`), the filter bar spacing above tables, the pager and the document line grid. Not affected: sidebar menu rows, dialogs, and palette result rows (they have their own fixed size).
3. Below 1024px (phones, small tablets) the setting is ignored and the controls are hidden. Phone cards, buttons, tab bar, inputs, dialogs and sheets keep their sizes; touch targets stay 44px minimum (48px for primary controls).
4. Where it appears: (a) user menu "Density" radio group (Comfortable, Compact), shown only at >= 1024px; (b) a small icon button at the right end of the toolbar above every `app-table` on desktop, labelled "Density: Comfortable. Change." which cycles the value (the same global setting); (c) palette actions "Density: Compact" and "Density: Comfortable" (desktop only).
5. Persistence: per user in this browser (same store as theme, section 4.2 item 3). It is one global setting, not per screen.
6. Changing it is instant, no animation, and keeps scroll position and focus.
7. What changes is only vertical rhythm (see 5.3). It never changes: font family, text colour or contrast, column set, column order, truncation rules, which fields are visible, money format, status chips.
8. Default stays Comfortable for every role (open question Q2 asks whether Supervisor/Admin desktops should default to Compact).

### 5.3 What changes (and nothing else)

| Property | Comfortable | Compact |
|---|---|---|
| Table row min height | 48px (today's `touch` token) | 36px |
| Cell vertical padding | 8px (today) | 4px |
| Header row height | 40px | 32px |
| Table body text | 16px | 14px (never smaller) |
| Filter bar vertical gap | 16px | 8px |
| Pager and toolbar height | 48px | 40px |
| Line grid row (document editor) | 56px | 44px (inputs stay >= 36px high) |
| Row hit area | whole row is the link | whole row is the link |
| Minimum target for any separate control in a row (sort button, row action) | 44px | 24px (WCAG 2.5.8) with >= 8px gap |

Wireframe of the same three rows:

```
Comfortable                                         Compact
+---------------------------------------------+    +---------------------------------------------+
| SKU        Product            On hand       |    | SKU       Product          On hand          |
|---------------------------------------------|    |---------------------------------------------|
|                                             |    | WIDGET-A  Widget A blue      12 PCS [LOW]   |
| WIDGET-A   Widget A blue      12 PCS [LOW]  |    | BOLT-M8   Bolt M8 x 40      540 PCS         |
|                                             |    | WASH-8    Washer 8mm          0 PCS [LOW]   |
| BOLT-M8    Bolt M8 x 40      540 PCS        |    | ...                                         |
|                                             |    +---------------------------------------------+
| WASH-8     Washer 8mm          0 PCS [LOW]  |
+---------------------------------------------+
```

### 5.4 Acceptance criteria

| ID | Criterion |
|---|---|
| DN-01 | At 1280px Compact shows at least 30% more table rows in the same height than Comfortable (measured on the Stock list) |
| DN-02 | At 359 to 1023px the density control is absent and rows/cards are identical in both settings |
| DN-03 | Compact still meets FR-01 contrast, 24px minimum targets for separate controls, and the row link covers the row |
| DN-04 | The setting survives reload and sign-out/sign-in of the same user; another user on the same browser is unaffected |
| DN-05 | Switching keeps focus on the control that was used and does not scroll the page |
| DN-06 | At 400% zoom (an effective width < 1024px) the layout falls back to cards and ignores the setting |
| DN-07 | No text is clipped or ellipsised in Compact that is not clipped in Comfortable |

Priority: Should (S5).

## 6. Item 4b - Keyboard-first desktop and command palette

### 6.1 Goal

A supervisor at a desk reaches any screen, document or product by typing, without leaving the keyboard, and the whole app is operable by keyboard. The menus stay: the palette is an accelerator, never the only route.

### 6.2 Command palette - what it is and what it searches

Opens as a modal dialog (native `<dialog>` through `app-dialog`, so focus trap and Esc match every other dialog). Desktop: centred, 40rem wide, top aligned at 12vh, results area max 60vh with its own scroll. Tablet: 32rem. Phone: full screen sheet (6.7).

Sources, each filtered by the signed-in user's permissions:

| Group | Contains | Source | Visible when | Opens |
|---|---|---|---|---|
| Recent | Last 8 documents and products the user opened (most recent first) | Local device store, per user; cleared at sign-out (shared warehouse devices) | at least one | the item |
| Go to | Screens from `MENU_ITEMS` the user may open (Home, Receipts, Issues, Approvals, Stock, Low stock, Products, Catalog, Warehouses, Partners, Reports, Users, Settings, Audit log) | local, instant | per menu permission | the route |
| Actions | New receipt, New issue (`document.create`); Appearance (3 choices); Density (desktop, 2 choices); Language (3); Show keyboard shortcuts; Sign out | local, instant | per permission | runs once, closes the palette |
| Documents | Documents whose number, partner or reference matches | `GET /api/documents?search={q}&pageSize=5` (server already scopes to the user's warehouses and role) | `stock.read` and query >= 2 characters | the document (`/receipts/:id` or `/issues/:id`) |
| Products | Products whose SKU, name or barcode matches | `GET /api/products?search={q}&active=true&pageSize=5` | `masterdata.read` and query >= 2 characters | the product's stock page (`/stock/:productId`, because on hand is what people want); secondary target (Ctrl+Enter) opens the product record when the user has `masterdata.manage` |

Never in the palette (error prevention): any action that changes a document or stock (Post, Approve, Reject, Cancel, Reverse, Submit), delete, import, export, or anything that needs a reason. The palette finds and navigates; the confirmed actions stay on the document where the user sees the lines (`mvp-ux.md` 4.7 note 3).

Result row content: primary text (document number, SKU + name, screen name, action name), a secondary line (document: type, party, status chip text and date; product: category; screen: group name; action: none), the group label at the group heading. No money, ever (0.5). Inactive products are not returned (D8 rule).

Ranking and order:

1. Empty query: Recent, then Go to (first 6 for the role in menu order), then Actions (first 4: New receipt, New issue, Appearance, Language).
2. Typed query: results are grouped, groups are ordered by best match, with these tie rules: query looks like a document number (contains letters then a dash or digits, for example `RCV-26` or `iss 12`) puts Documents first; query equals a known SKU or barcode exactly puts Products first and pre-selects that item; otherwise Go to and Actions (matched by label prefix, then word prefix, then substring, case-insensitive) come before Documents and Products.
3. Local groups are shown at once; Documents and Products fill in after 300ms debounce (same as every search box). Maximum 5 per group, 12 rows total. If a source reports more than 5, a muted line "Refine your search to see more." follows the group.
4. First option is pre-selected so Enter always does something sensible.
5. Matching ignores case and diacritics; the query is trimmed; length capped at 100 (EA-16).
6. Language: screen and action labels come from the i18n catalog in the active language (also searchable by the English name when the UI is zh or ms, so a shared tutorial or support call still works).

### 6.3 Trigger

| Width | Trigger |
|---|---|
| >= 1024px | A visible search-field-look button in the top bar: search icon, placeholder text "Search or run a command", and the key hint "Ctrl K" (shows "Cmd K" on Apple platforms). 240px wide, 40px high (a desktop control; keyboard users have Ctrl+K). It is a real `<button>` with `aria-haspopup="dialog"` and `aria-keyshortcuts="Control+K Meta+K"` |
| 600 to 1023px | An icon button (search icon, 44px) in the top bar with an `aria-label` "Search or run a command" |
| < 600px (phone) | An icon button (search icon, 44px) at the right of the top bar, next to the account button. Opens the full-screen sheet. Priority Should, built after the desktop palette (S9). If product owner declines (Q3) the phone shows nothing |

Keyboard: Ctrl+K and Cmd+K open and close it from anywhere, including inside text fields and the document editor (not a typing key). It calls `preventDefault` so the browser's own Ctrl+K (omnibox search) does not take over. Opening the palette from a dirty form does not trigger the unsaved-changes dialog (nothing navigates until a result is run; running a navigation result follows the normal guard).

### 6.4 Keyboard map

Palette:

| Key | Does |
|---|---|
| Ctrl+K / Cmd+K | Open; when open, close |
| Esc | Close; focus returns to the element that had it before |
| Up / Down | Move the active option; wraps at the ends; skips group headings |
| Home / End | First / last option (only when the caret is at the start or end of the text, otherwise move the caret) |
| Enter | Run the active option |
| Ctrl+Enter / Cmd+Enter | Run the option's secondary target (product record) when shown in the footer hint |
| Tab / Shift+Tab | Moves between the search field and the footer "Show shortcuts" link; options are reached with arrows, never Tab |
| Any character | Goes to the search field (focus never leaves it while arrowing) |
| Backspace on empty | Nothing (does not close) |

App-wide (desktop, when not typing in a field, except those marked "always"):

| Key | Does | Notes |
|---|---|---|
| Ctrl+K | Command palette | always |
| `/` | Focus the screen's search box (Stock, lists, Products, Audit) | single character key: see 6.6 |
| `?` | Open "Keyboard shortcuts" dialog | single character key: see 6.6 |
| Ctrl+S | Save draft in the document editor, Save in forms | always, `preventDefault` to stop the browser Save dialog; only where a Save button exists |
| Ctrl+Enter | Primary action of the document editor (Submit & post or Submit; the Post confirm dialog still appears) | always; never bypasses a confirm dialog |
| Esc | Close dialog or sheet (existing) | |
| Up / Down on a row link in a table | Move to the previous/next row's link | table keeps `table` semantics; arrows are an addition |
| Enter on a row link | Open the row | existing |
| Left / Right on the pager | none (pager stays Tab/Enter) | |

Document line grid (existing, restated): Tab/Enter moves along the line; the last empty row is "type to add"; the product picker accepts a scanned barcode + Enter.

### 6.5 States and copy

| State | What the user sees | Copy |
|---|---|---|
| Open, empty query, has recents | Recent, Go to, Actions | Placeholder: "Search screens, documents and products" |
| Open, empty query, no recents | Go to, Actions only | same |
| Typing 1 character | Local matches only; hint row | "Type 2 or more characters to search documents and products." |
| Loading Documents or Products | The local groups stay; each loading group shows 2 skeleton rows after 300ms | sr-only "Searching documents" / "Searching products" |
| Results | Groups with counts in the sr-only heading | live region "{count} results" |
| No results at all | One block | "Nothing matches \"{query}\"." second line "Try a document number such as RCV-2610-0007, or a SKU." |
| No results in one source only | That group is omitted (no "0 documents") | none |
| A source fails (5xx, network) | Inside that group a single row; other groups unaffected | "Couldn't search documents. Retry" - the row is an option; Enter retries. No toast |
| Source returns 403 | Group omitted silently (role cannot see it) | none |
| Offline | Local groups only, plus a muted line | "You're offline. You can still go to screens and run actions." |
| Rate limited (429) | Same as source failure with the server sentence | server text |
| Footer (desktop) | Key hints | "Up and Down to move, Enter to open, Esc to close" and a link "Keyboard shortcuts" |

### 6.6 Single-character shortcuts and WCAG 2.1.4

`/` and `?` are single printable keys. To satisfy WCAG 2.1.4 (Character Key Shortcuts) they are active only when focus is not in an editable control (input, textarea, select, contenteditable) and the shortcuts dialog contains a switch "Single-key shortcuts: On/Off" (default On at >= 1024px, remembered per user). All other shortcuts use Ctrl/Cmd.

### 6.7 Phone (palette as a search button)

Not shown as a keyboard feature on phones (no key hint, no footer hints). The top-bar search icon opens a full-screen sheet: a 48px search field with autofocus (opens the keyboard, wanted here), a close button (X, 44px) at the top left, rows 48px high. Same groups, same rules. Android back closes the sheet first. The bottom tab bar is hidden while the sheet is open. Recents show first so a worker re-opens the last document in one tap.

### 6.8 Screen reader behaviour

1. Pattern: ARIA 1.2 combobox with listbox. The text field has `role="combobox"`, `aria-expanded="true"`, `aria-controls` (listbox id), `aria-autocomplete="list"`, `aria-activedescendant` (active option id); DOM focus stays in the field.
2. The listbox has accessible name "Results". Groups are `role="group"` with `aria-labelledby` the group heading. Options are `role="option"` with `aria-selected` on the active one.
3. Option accessible name = "{primary}, {secondary}, {group}", for example "RCV-2610-0007, Receipt, Acme Trading, Posted, Documents".
4. A polite live region (one, inside the dialog) announces "{count} results" once after loading settles (not per keystroke; debounced 500ms), "No results for {query}" for the empty case, and "Searching" never (avoids chatter; the skeleton rows are `aria-hidden` and the loading group carries sr-only text).
5. On open the dialog is named "Search or run a command"; focus lands on the field through `data-autofocus` (the repo rule; the `autofocus` attribute is banned).
6. On run of a navigation result, focus moves to the new page's `h1` (the route announcer already speaks the title); on run of an action, focus returns to the opener; the action's effect is announced (for example "Appearance: Dark").
7. Group headings are not in the tab order; the footer link is.
8. The trigger button's name is "Search or run a command, Control K".

### 6.9 Roles respected (examples)

| Role | Go to | Actions | Documents | Products |
|---|---|---|---|---|
| Warehouse staff | Home, Receipts, Issues, Stock, Low stock, Products, Warehouses, Partners | New receipt, New issue, Appearance, Language, Sign out | yes (own warehouses; server scopes) | yes, opens stock page |
| Clerk | same | same | yes | yes |
| Supervisor | + Approvals, Catalog, Reports | same + Density | yes | yes; Ctrl+Enter opens product record |
| Owner | Home, Receipts, Issues, Approvals, Stock, Low stock, Products, Reports, Audit log, Catalog, Warehouses, Partners | Appearance, Density, Language, Sign out (no New receipt/issue) | yes | yes |
| Admin | everything, + Users, Settings | same as Supervisor | yes | yes |

### 6.10 Focus and keyboard rules for the rest of the desktop app

1. Every interactive element is reachable by Tab in visual order; no positive `tabindex`.
2. After a route change, focus goes to the page `h1` or `main` (existing `tabindex=-1` on main); never `body`.
3. After a dialog closes, focus returns to its opener, or to the first invalid field / the error banner if the action failed (existing EA-07 behaviour).
4. A disabled-because-of-a-rule button stays focusable with `aria-disabled` and `aria-describedby` to its reason (existing EA-22 pattern). Applies to every new button.
5. Shortcut keys appear in tooltips and the shortcuts dialog but never as the only way to do anything.
6. Skip link stays first.

### 6.11 Acceptance criteria

| ID | Criterion |
|---|---|
| CP-01 | Ctrl+K and Cmd+K open the palette from Home, a list, the document editor and a text field; pressing again closes; browser omnibox does not open |
| CP-02 | The trigger button is visible at >= 1024px with the key hint, has an accessible name, and opens the palette with Enter and Space |
| CP-03 | As Staff, the palette lists no Approvals, Reports, Users, Settings, Audit; typing "settings" returns no screen. As Admin it returns Settings |
| CP-04 | Typing `RCV-2610-0007` lists that document first; Enter opens it; the search sent to the API is debounced 300ms and capped at 100 characters |
| CP-05 | Typing an exact SKU pre-selects that product; Enter opens its stock page |
| CP-06 | No row, label or live-region text contains an amount or value for any role |
| CP-07 | Arrow keys move the active option without moving DOM focus; `aria-activedescendant` updates; Enter runs; Esc closes and restores focus |
| CP-08 | Failing the Documents call shows the single "Couldn't search documents. Retry" row; Screens and Products still work; no toast |
| CP-09 | Offline: only Recent, Go to, Actions with the offline line |
| CP-10 | Signing out clears Recent; another user does not see the previous user's recents |
| CP-11 | The palette contains no Post, Approve, Reject, Cancel, Reverse, Submit, delete, import or export action |
| CP-12 | A screen reader (NVDA + Firefox, VoiceOver + Safari) hears the dialog name, then option names with group, then "{n} results" once |
| CP-13 | Opening the palette in a dirty editor does not show "Discard changes?"; navigating away from the dirty editor via a result does |
| CP-14 | At 320px (phone) the palette sheet fills the screen, rows >= 48px, no horizontal scroll; at 400% zoom likewise |
| CP-15 | `/` focuses the page search only when focus is not in a field; the "Single-key shortcuts" switch turns `/` and `?` off |
| CP-16 | Ctrl+S saves a draft with the same result as the Save draft button; Ctrl+Enter never skips the Post confirm dialog |
| CP-17 | Up/Down on a row link in a table moves to the neighbouring row's link; Enter opens it |

Priority: palette core Should (S8), search groups Should (S9), phone button Should (S9), `/`, `?`, Ctrl+S, Ctrl+Enter, row arrows Could (S10).

## 7. Item 5 - Loading and feedback (restrained motion)

### 7.1 Skeletons

Rules:

1. A skeleton appears only if the wait exceeds 300ms (nothing flashes for fast answers). Once shown it stays at least 400ms (no flicker on 320 to 400ms answers).
2. It copies the real layout exactly: same row heights at the current density, same column widths, same card heights, so content replaces it without moving anything (no layout shift).
3. It shows only what the user could see: no skeleton for a column or widget their permissions hide (FR-07).
4. Count of rows: the page size capped at 8 on desktop; on phone, enough cards to fill the viewport (about 6). A skeleton never pretends a list is long.
5. Animation: opacity pulse 1.0 to 0.55 over 1.4s (existing). No shimmer sweep, no gradient travel (decorative). Under reduce-motion: static at 0.7 opacity.
6. A skeleton block is `aria-hidden`; one sr-only "Loading" status per screen region (existing `app-skeleton` pattern). `aria-busy="true"` on the region while loading.
7. Refetch (a filter changed): keep the old rows at 60% opacity and show the thin progress line under the header (existing), after the same 300ms delay. Never replace existing rows with a skeleton.
8. Full-page spinner: only during sign-in and the first app start. Never inside the app.

Shapes per screen type:

| Screen type | Skeleton shape |
|---|---|
| Table list (desktop) | Header row real (not skeleton), then N rows with one grey bar per visible column at the column's typical width (primary column wide, numeric columns short and right-aligned) |
| Card list (phone: documents, stock, approvals) | N cards of the real card height: line 1 two bars (number, chip), line 2 one bar, line 3 one short bar (document card = 2 lines per NV-08) |
| Stat/KPI (Home numbers) | A number-height bar (60% width) plus a label bar (40%) inside the real tile outline |
| Home list widget | Widget heading real; 3 rows of one bar + one short bar |
| Document view/edit | Header: title bar + chip block; 4 header-field blocks; then 3 line cards (phone) or 3 grid rows (desktop); the sticky action bar is real (disabled) |
| Form page (product, settings) | Label bars + 48px input blocks in the real order; real buttons disabled |
| Report table | Same as table list; filter form is real and usable while loading |
| Stock ledger / history | Rows of 5 narrow bars |
| Dialog content (reverse, approve) | Not needed (content is local); a busy button instead |
| Palette group | 2 rows of one bar + one short bar (after 300ms) |
| Pager | Hidden until total is known |

```
Table list skeleton (desktop, comfortable):
| SKU       Product             Category    On hand    |   <- real header
| ====      ==========          =======        ==      |
| ====      ===========         ======         ==      |
| ====      ========            =======        ==      |

Card skeleton (phone):
+------------------------------------+
| ==========================   ===== |
| ==============                     |
| ======                             |
+------------------------------------+
```

### 7.2 Buttons

States for every `app-button` that triggers a request:

| State | What changes | Notes |
|---|---|---|
| Idle | Label (verb) | |
| Pressed | Colour step 120ms (hover/active token). No scale, no bounce, no ripple | |
| Busy | Immediately `aria-disabled="true"` and `aria-busy="true"` (stops double submit at once); the spinner and a fixed width appear after 150ms (a fast response shows no spinner); label stays; focus stays on the button | Other buttons in the same bar are also disabled; form fields become read-only for the call (existing) |
| Success | Only for actions that leave the user on the same screen (Save draft, Save settings, Save user, Save product): the label changes to a check icon + "Saved" for 1.5s, then returns to the verb. A polite live region says "Draft saved." | When this state shows, the "Draft saved." toast is not shown (one confirmation channel). Actions that move to another screen or change status (Submit, Post, Approve, Reject, Reverse, Cancel) do not use this state; their result is the new screen state plus one toast |
| Error | Returns to Idle at once; the error appears as an inline banner (7.3) and focus moves to it (or the first invalid field); the button is never coloured red | Network errors offer a "Try again" link in the banner; the typed data stays |
| Disabled by rule | `aria-disabled` + reason text linked by `aria-describedby`, grey fill with muted text at AA | E1, E3 pattern, unchanged |
| Slow (> 8s) | Under the button: "Still working. Don't close this page." (existing) | |

The tapped button shows the spinner, not a neighbour (NV-21).

### 7.3 Inline message placement

| Message kind | Where | Role / announcement | Focus |
|---|---|---|---|
| Field validation | Directly under the field, linked with `aria-describedby`, `aria-invalid` on the control | assertive on submit, otherwise polite on blur | First invalid field |
| Line problem (E1, E2) | On the line, under the quantity | polite | none |
| Several errors on submit | Banner at the top of the form: "{n} things to fix" with links to each field, plus the inline errors | `role="alert"` | The banner, then the first invalid field on next Tab |
| Document state (Returned, Stale E4, Stock changed E5, Reversal blocked E6, Why this needs approval) | Banner directly under the document header (under the sticky header, above the form) | `role="alert"` for new errors, `role="status"` for informational | The banner |
| Dialog action fails | Inside the dialog, above the buttons; the dialog stays open and keeps the typed reason (PS-01 residual) | `role="alert"` | The message |
| List/table load fails | Replaces the table body with the error panel and Retry | one announcement only (the panel; no toast) | none |
| Widget load fails (Home) | Inside the widget | status | none |
| Destructive consent | Confirm dialog (existing) | | |

Rule: one failure produces one announcement. If an inline message is shown for a failure, no toast carries the same text (supersedes `mvp-ux.md` 6.3 for business-rule violations).

### 7.4 Toasts

| Rule | Value |
|---|---|
| Use for | Outcomes the user cannot see on screen: "Posted. Stock updated." when the screen changed anyway, "Sent for approval...", "Returned to {name}...", "{count} products added...", "Document cancelled." |
| Do not use for | Validation errors, any failure that has an inline message, save results already shown on a button, anything the user can read on the page |
| Success | Auto-dismiss 4s; the timer pauses while the pointer is over the toast or focus is inside it; polite live region |
| Error | Stays until dismissed; `role="alert"`; only for failures with no inline home (for example an export that failed) |
| Count | Max 2 visible; a third success replaces the oldest success; errors are never replaced |
| Position | Below the desktop width: an in-flow banner under the top bar, first in the main region, pushing the page down (at most 20dvh, scrolls inside; ruling C6 v3). Desktop (1024px up): bottom right above the sticky action bar (ruling C14). Inside a modal dialog: a copy above the dialog content |
| Content | One sentence, what happened (and what to do on error), optional single action "View" or "Retry" (no "Undo": the product does not offer it) |
| Duplicates | The same text within 2 seconds shows once |
| Motion | 7.6 |

### 7.5 Optimistic updates

Rule: **no server write is optimistic in this product.** A stock document, status, reason, master-data record, user, setting or approval rule changes on screen only after the server confirmed it, because the server owns permissions, row versions and stock (D3, BR-12). The busy state (7.2) is the feedback.

Optimistic (immediate, no waiting) is allowed only for client-only state: theme, density, language, the Mine/All scope, filter chips, expanded/collapsed disclosure, the form's own lines before saving (add/remove a line), and Recents. These never call the API (or are saved without waiting for it) and so cannot disagree with the server.

Row highlight (NV-04): when a line is added or its quantity changes through the Add item sheet or a scan, that line gets the `highlight` tint for 1.5s and a polite message "DEMO-001: now 2." (existing). Under reduce-motion the tint is applied and removed after 1.5s with no fade.

### 7.6 Motion durations and easing

Principle: motion only confirms a state change the user just caused, or shows that work is in progress. Nothing moves on its own. No animation exceeds 200ms except indeterminate loading indicators.

Tokens: `fast` 120ms, `base` 200ms (exist); `hold` 1500ms (new, success tick); easings `smooth` cubic-bezier(0.4, 0, 0.2, 1) (exists), `enter` decelerate (0, 0, 0.2, 1) (new), `exit` accelerate (0.4, 0, 1, 1) (new).

| Element | Trigger | What animates | Duration | Easing | Reduced-motion fallback |
|---|---|---|---|---|---|
| Hover/focus/pressed colour | pointer, keyboard | background and border colour | 120ms | smooth | instant |
| Focus ring | focus | none (appears at once) | 0 | - | same |
| Button busy | request starts | spinner appears after 150ms; spinner rotates 0.8s per turn, linear, infinite | 150ms delay | linear | static spinner arc plus the visible word "Working" in the button (a frozen spinner alone looks broken; the global 0.01ms rule must not be the only fallback) |
| Button success | save done | check icon/label crossfade; stays 1.5s | 120ms in, 1500ms hold, 120ms out | smooth | swap instantly, same 1.5s hold |
| Skeleton | wait > 300ms | opacity pulse 1.0 to 0.55 | 1.4s loop | ease-in-out | static 0.7 opacity |
| Table/page progress line | refetch > 300ms | indeterminate bar | 1s loop | linear | static line at 40% opacity under the header |
| Rows refreshing | refetch | row opacity to 60% | 120ms | smooth | instant |
| Row highlight | line added or changed | tint appears, fades out | 120ms in, 1500ms hold, 1000ms out | smooth | tint 1.5s, removed instantly |
| Toast in | outcome | translateY 8px to 0 and fade | 200ms | enter | appears instantly |
| Toast out | timer or dismiss | fade | 120ms | exit | disappears instantly |
| Dialog open (desktop) | open | fade only (no scale, no slide) | 120ms | enter | instant |
| Bottom sheet (phone: dialog, More, Add item, palette) | open / close | slide up from bottom / down | 200ms / 120ms | enter / exit | instant |
| Scrim | with dialog | opacity | 120ms | smooth | instant |
| Disclosure ("More") | toggle | opacity only; height changes at once (no height animation) | 120ms | smooth | instant |
| Inline error | validation fails | none (appears) | 0 | - | same |
| Route change | navigation | none (no page transition); the announcer speaks the title | 0 | - | same |
| Theme / density switch | choice | none (instant) | 0 | - | same |
| Palette open/close | shortcut | fade | 120ms | enter / exit | instant |
| Palette option move | arrow | none (active style only) | 0 | - | same |
| Count / number change | data updates | none (no count-up, no flip) | 0 | - | same |
| Chart, parallax, hero, confetti, bounce, shake | - | not used | - | - | - |

Reduce-motion contract: with `prefers-reduced-motion: reduce` (a) no transform or opacity transition longer than 0ms runs, (b) infinite loops stop and are replaced by the static fallbacks in the table, (c) no information is lost (busy is still visible as text, success is still visible and announced). The existing global rule that shrinks every animation to 0.01ms stays as a safety net, but components must also carry the explicit fallbacks above.

### 7.7 Acceptance criteria (item 5)

| ID | Criterion |
|---|---|
| FB-01 | With the network throttled to a 200ms answer no skeleton or spinner is ever visible; at 350ms the skeleton appears and stays >= 400ms |
| FB-02 | Replacing a skeleton with content causes zero layout shift (CLS 0 for the list and document screens in the e2e performance check) |
| FB-03 | A skeleton for Staff has no column or widget for cost or value (FR-07) |
| FB-04 | Tapping Save draft: only that button shows busy; double tap sends one request; on success it shows "Saved" with a check for 1.5s and no "Draft saved." toast; a screen reader hears "Draft saved." once |
| FB-05 | Submit & post, Post, Approve, Reject, Reverse, Cancel never show the Saved state; each ends in the new document state plus one toast |
| FB-06 | A failed Save shows one banner (alert) at the top of the form and focus moves to it; no toast with the same text; typed data stays |
| FB-07 | A failed Reverse keeps the dialog open with the reason typed and the message inside the dialog |
| FB-08 | A list load failure shows one announcement (panel only); EA-15/26 re-test passes |
| FB-09 | Toast: pauses on hover and on focus, max two visible, never covers the sticky action bar on a 360px phone |
| FB-10 | With reduced motion on: no pulse, no spinner rotation, no slide, no fade; busy shows the word "Working"; success tick shows 1.5s; every feature still works |
| FB-11 | No server write updates the UI before the response (verify by delaying the API 2s: the status chip, lines and totals change only after) |
| FB-12 | Every animation present is in the table 7.6; there is no other keyframe or transition in the touched styles (stylelint/grep check) |
| FB-13 | Under 320px and 400% zoom the toast and banners do not overlap the focused control |

Priority: skeleton rules, reduce-motion fallbacks and banner/toast de-duplication Must (S3, S4); button success state, highlight, "Last saved" Should (S4).

## 8. Progressive disclosure rules

### 8.1 What goes behind "More" (or a collapse)

| Item | Where | Phone | Desktop |
|---|---|---|---|
| Secondary document actions (Cancel document, Duplicate) | "More actions" menu | yes (existing NV-10) | overflow menu beyond one primary + one secondary |
| Filters beyond search + 2 chips (Status, Warehouse, From, To) | "Filters" button with a count badge | yes | visible inline |
| Receipt line: unit cost, remark | inside the Add item sheet under "More" | yes; shown only with `cost.view` | columns |
| Document header fields once at least one line exists | one-line summary, tap to expand | yes | always visible |
| Table columns marked `hideOnPhone` (category, safety stock, warehouse where the user has one) | card detail page | hidden | shown |
| Home list rows beyond 3 / 5 | "See all" | yes | yes |
| W11 Recently posted by me | collapsed on phone | yes | open |
| Audit entry before/after | "Show" per row (accessible name includes the row) | | |
| Keyboard shortcuts | "?" dialog and palette footer | n/a | yes |
| Developer detail of an error (code, correlation id) | "Details" disclosure inside the error | yes | yes |

### 8.2 What is never hidden

1. Required marks, validation errors, and the reason a button is disabled.
2. Stock availability and shortage on issue lines (E1/E2), and the "Negative" / "Low" chips.
3. Document number, status chip and flags (Returned, Override needed, Large).
4. The primary action of the screen (it is never inside "More").
5. Anything the user must read to decide: the lines and the amount (with `cost.view`) on the approver view, the matching approval rule ("Why this needs approval"), the shortfall table, the reversal consequence in the confirm dialog.
6. Warnings about irreversible actions (the confirm dialog text).
7. Unsaved-changes state, stale-document banner, offline state.
8. On-hand quantity (largest text on a stock card).
9. Essential information is never in a tooltip or `title` (hover-only is forbidden); a tooltip may repeat a visible label or a key hint.
10. Why something is missing for the role: a disabled Approve with the E3 sentence, not an absent button (existing).

### 8.3 Rules for adding a "More"

A new "More" is allowed only if all hold: the content is used by fewer than 1 in 5 visits of the task, it is not needed to avoid an error, the trigger is a labelled button of >= 44px on phone, and the state (open/closed) is announced with `aria-expanded`.

## 9. Empty states

### 9.1 Types and how each one looks

| Type | Meaning | Look | Action |
|---|---|---|---|
| First use | Nothing exists yet | Decorative icon (aria-hidden), one line, one primary CTA | Create it (role permitting) |
| No match | Filters or search found nothing | One line naming the query, one CTA | Clear filters / Clear search |
| All clear | Nothing needs the user | One calm line, no icon fuss, no CTA | none |
| No access | The role cannot have this | The forbidden page, not an empty list | Go to Home |
| Error | The load failed | Error panel (7.3) | Retry |

Rules: one line, one action, plain words, no jokes; never an empty table frame with headers and no row; the CTA is hidden (not disabled) when the role cannot do it, and the text changes to the read-only wording ("No receipts to show."); the empty state sits where the list was (no layout jump); it is a `role="status"` region only when it appears after a filter change.

### 9.2 Copy for the new surfaces (existing screens keep `mvp-ux.md` 6.2)

| Surface | Line | Action |
|---|---|---|
| Home W6 (zero) | "All clear: nothing waiting for you." | none |
| Home W7 (zero) | "Nothing is low on stock." | none |
| Home W10 (no rows) | "No movements yet." | none |
| Home W3, W4, W5 (zero) | hidden | |
| Home, Doer, everything zero | (the two buttons are the content) | |
| Home widget failed | "Couldn't load this. Try again." | Retry |
| Palette, no results | "Nothing matches \"{query}\"." + "Try a document number such as RCV-2610-0007, or a SKU." | none |
| Palette, offline | "You're offline. You can still go to screens and run actions." | none |
| Palette, source failed | "Couldn't search documents. Retry" | Retry |
| Shortcuts dialog (none) | n/a | |

## 10. Table and list patterns

### 10.1 Rules (desktop table)

1. One definition, two renderings (existing): table >= 1024px, cards below.
2. Columns in order of importance: identifier, the thing, quantity/amount, status, time. Identifier and status never hide.
3. Identifier cells (SKU, document no) never wrap; names truncate with an ellipsis to one line (full text on the detail page, and in the cell's `title` as a convenience only); dates never wrap over lines (PR-06, PS-22).
4. Numbers: right aligned, tabular figures, money exactly 2 decimals with thousands separators, quantity up to 3 decimals trimmed, unit in its own text next to the number, negative with a minus and the "Negative" chip.
5. Header: sticky within the table region; sort buttons are real buttons with `aria-sort` and >= 24px (desktop) / 44px (touch); the sorted column has an arrow icon, not only a colour.
6. Rows: 1px divider, no zebra; hover tint; keyboard-active row has a 3px focus outline; the whole row is the link (the primary cell link covers it); at most one inline action per row, others in the document; nothing important is hover-only.
7. Row status: chip with icon + text; flags beside it; the row never changes colour for status.
8. Wide report tables (Reports) keep the existing own-region horizontal scroll (focusable, labelled) and stay under the 1.4.10 data-table exception; SKU and date are nowrap; Partner may drop to a second line. Product, Unit and Unit cost (with `cost.view`) are on screen, not only in Excel (PR-06).
9. Loading, error, empty are inside the table component (existing).
10. Selection and bulk actions: none in this refresh.

### 10.2 Rules (phone card)

1. Two lines of facts: line 1 identifier + status chip; line 2 party / warehouse / quantity / time (NV-08). On stock cards the product name is the heading, "SKU - warehouse" below, large on-hand with unit, Low/Negative chip.
2. A card is one 48px+ tall link target; no second tap target inside except chips that open nothing.
3. Fields without a value are omitted, not shown blank.
4. Filters collapse behind one "Filters" button with a count; search and the Mine/All switch stay visible.

### 10.3 Paging (PS-13, product decision pending, see Q8)

Recommendation: desktop shows First, Previous, "Page 3 of 14", Next, Last and a page size select (25, 50, 100). Phone keeps Previous, Next only and the range "26-50 of 333". After changing page, scroll to the top of the list and move focus to the table caption. Until decided, the existing Previous/Next stays.

### 10.4 Dates in filters (NV-27, PS-15, product decision pending, see Q9)

Recommendation: a text date field in the app format `dd MMM yyyy` with a calendar button, inline validation "From must be on or before To.", an empty state with an action "Change filters" in Reports. Until decided, native date inputs stay.

### 10.5 Acceptance criteria

| ID | Criterion |
|---|---|
| TB-01 | No SKU or document number wraps at 1024px, 1280px; no date wraps over three lines |
| TB-02 | Every money cell has exactly 2 decimals; every quantity <= 3 decimals trimmed |
| TB-03 | Every table has a visible sort indicator that is not colour-only |
| TB-04 | On phone every list row/card is a single 48px+ target and shows at most two fact lines (documents) |
| TB-05 | Reports show Product, Unit and (with `cost.view`) Unit cost on screen |
| TB-06 | Empty tables never show headers without rows; empty state has at most one CTA |

## 11. Do not do

1. No glassmorphism, blur, frosted panels, neumorphism, gradients behind text, glow, or animated backgrounds.
2. No AI chatbot, assistant, suggestion widget or "smart" recommendations, and no layouts that reorder themselves by usage or time.
3. No decorative animation: no count-up numbers, no confetti, no bouncing, no parallax, no page transitions, no skeleton shimmer sweeps, no looping icons.
4. No colour-only meaning (status, required, low stock, selected, error).
5. No hover-only information or actions; no essential text in tooltips.
6. No optimistic update of any server write.
7. No state-changing action in the command palette or behind a keyboard shortcut without the normal confirm dialog.
8. No abbreviated money ("12.4k"), no money without 2 decimals, no `0.00` or "-" placeholder where the API sent `null`.
9. No cost or value for Staff, no valuation for Clerk, anywhere, including skeletons, palette, tooltips, aria-labels, exports.
10. No role-name checks in templates for layout or visibility: use permissions.
11. No horizontal page scroll at 320px or 400% zoom; no fixed bars covering focused controls.
12. No new required tap or step on the receive/issue happy path.
13. No separate toast for a failure that already has an inline message.
14. No reordering of DOM versus visual order with CSS.

## 12. Rollout - small shippable slices

Each slice is independently shippable, green on its own (lint, format, tests, contrast, e2e axe), and estimated under about 1,200 changed frontend lines including specs. Sizes are fe-architect's to confirm. Visual tokens from the visual designer can land before or between any slices; slices reference tokens by name only.

| Slice | Content | Item | Pri | Est. lines | Depends on |
|---|---|---|---|---|---|
| S1 | Required marker + legend: `app-form-field` and radio group rendering (asterisk aria-hidden, `required` on the control, sr-only "required" only in a radio legend), legend component placed in all forms and dialogs, catalog keys, tests; remove "(required)" | 3.3 (FR-06) | Must | 300 | none |
| S2 | Preferences foundation + dark mode behaviour: per-user preference store (theme now, density and recents later), `ThemeService` per user, device-level copy for first paint, synchronous theme-init script file, `color-scheme` and `theme-color` meta, cross-tab sync, "Appearance" control with "Now: Dark" | 4 | Must | 450 | none (CSP check by security-engineer) |
| S3 | Motion and loading foundation: motion tokens, reduce-motion fallbacks (spinner "Working", static skeleton and progress), skeleton delay 300ms + 400ms minimum, skeleton shape variants (table, card, stat, document, form), progress line delay | 7.1, 7.6 | Must | 700 | none |
| S4 | Feedback states: button Saved state + single confirmation channel, banner placement rules and removal of duplicate toasts (EA-15/26), toast pause/dedupe/max-2, dialog failures keep the reason (PS-01), row highlight (NV-04), live regions | 7.2 to 7.5 | Must (banners/toasts), Should (rest) | 900 | S3 |
| S5 | Role-aware Home for Doers (Staff, Clerk): profile from permissions, widget frame (skeleton, error, empty), W1 to W5, W7, W11, phone layout, tests per role | 2 | Must | 800 | S3 |
| S6 | Density: density in the preference store, `data-density` variables, table/pager/filter/line-grid rules, user-menu radio and table-toolbar button, rename `$compact` | 5 | Should | 600 | S2 |
| S7 | Role-aware Home for Reviewers and Overseer: W6, W8, W9, W10, W12, W13, desktop two-column grid, Admin extras, tests | 2 | Should | 900 | S5 |
| S8 | Command palette core: trigger (desktop + tablet icon), dialog, combobox/listbox pattern, Recent, Go to, Actions (theme, density, language, new document, sign out), keyboard map, live region, recents store cleared at sign-out | 6 | Should | 1100 | S2 (store), S3; Actions for density need S6 (omit until then) |
| S9 | Palette search: Documents and Products groups, ranking, loading/error/offline states, "Refine your search", phone search button and full-screen sheet | 6 | Should | 700 | S8 |
| S10 | Keyboard-first extras: shortcuts service (`/`, `?`, Ctrl+S, Ctrl+Enter), shortcuts dialog with "Single-key shortcuts" switch, arrow navigation on table row links, tooltips with key hints | 6 | Could | 800 | S8 |
| S11 | Table and list pattern clean-up: nowrap SKU and date, Reports columns on screen, paging First/Last/page size, empty-state variants, date field (if approved) | 10 | Could | 900 | S6; owner answers Q8, Q9 |

Recommended order: S1, S2, S3, S5, S4, S6, S7, S8, S9, S10, S11. Reason: S1 to S3 are small, remove known QA defects and make everything after safer; S5 benefits the phone users first; S8 to S10 are the desk-user accelerators and come once the preference store and skeletons exist.

Parallelism: S1, S2 and S3 touch different libraries (`shared/ui` form field, `core/theme` + `index.html`, `shared/ui` + `shared/styles`) and can run at the same time. S6 and S7 can run at the same time after their dependencies. S8 and S5 both touch the shell/home areas only lightly; no conflict expected.

Per-slice definition of done (in addition to the repo's): the section's acceptance IDs pass; e2e axe scan on the touched screens in both themes; manual check at 320px and 400% zoom; a Staff and a Clerk walk-through for FR-07; i18n keys in en, zh and ms (zh/ms drafts marked `TODO-review`).

## 13. Open questions for the product owner

Default in brackets is what this specification assumes.

| # | Question | Default assumed |
|---|---|---|
| Q1 | Should the theme and density choice follow the user across devices (saved on the server, needs a small preferences endpoint and migration), or stay per browser? | Per browser, per user (no API) |
| Q2 | Should Supervisor, Admin and Owner desktops default to Compact density? | No: Comfortable for everyone; the choice is one click |
| Q3 | Do phone users get a search button for the command palette (find a document by number)? | Yes, as a Should in S9; if No, drop the phone part |
| Q4 | Are single-key shortcuts (`/`, `?`) wanted at all, given the WCAG 2.1.4 switch they require? | Yes, on by default on desktop with an Off switch |
| Q5 | Home "My drafts" and "Returned to me" cover receipts and issues together. Add a combined "My documents" list or per-type counts (small API change), or accept "See all" opening the list of the first row's type? | Accept the imperfect "See all" for now |
| Q6 | Should the Returned widget show the reviewer's reason (needs `rejectionReason` on the list item), or only the "Returned" chip? | Chip only; reason one tap away |
| Q7 | Should Admin see a "Stock without cost" warning (stock received by Warehouse staff carries zero cost, PR-07)? It needs a new count endpoint and a cost-correction step. | Not in this refresh |
| Q8 | Paging controls (PS-13): First/Last and page size 25/50/100 on desktop? | Yes (S11) |
| Q9 | Date filters (NV-27, PS-15): replace native date inputs with a text date field in the app format `dd MMM yyyy`? | Yes (S11), until then native inputs |
| Q10 | Recent items are cleared at sign-out because warehouse devices are shared. Keep them across sign-ins instead for single-user devices? | Cleared at sign-out |
| Q11 | Reduce motion: honour the device setting only, or also add an in-app switch for people whose device setting is not available (some shared handsets)? | Device setting only |
| Q12 | Open since the MVP and still blocking one dead end: allow Cancel on an Approved document (Q3 of `mvp-ux.md`, PR-02)? Not part of this refresh, but the Home W6 "All clear" and the approver view depend on it being decided | Not changed |

## 14. Handoff notes

| To | Needs |
|---|---|
| fe-designer | Tokens listed in 3.1 (danger asterisk AA on both surfaces and both themes, `highlight`, density variables, `easing('enter'/'exit')`, `duration('hold')`, `theme-color` and `color-scheme` per theme); component look for widget frame, palette dialog, density toggle, success tick |
| fe-architect | Slice plan 12; where the preference store and the palette live (`core`, not `shared/ui`: they use identity and navigation); confirm the CSP note for the theme-init script file; contracts for W3/W6 (optional `rejectionReason`, `returned` filter); decide `app-form-field` legend placement API |
| fe-framework | Build slices in the order of 12 after fe-architect confirms sizes |
| be-framework | Optional only: `rejectionReason` on `DocumentListItem`, `returned` filter, draft counts per type (2.6) |
| security-engineer | CSP and the synchronous theme-init script (4.2 item 4); recents stored locally and cleared at sign-out |
| qa-novice, qa-power, qa-edge | Acceptance IDs RA, RQ, DK, DN, CP, FB, TB and FR in this file are the test list; re-run the NV, PS and EA findings named in section 1 |
| behavior-test-engineer | e2e: per-role Home order (RA-01), required marker (RQ-02), theme per user (DK-02), palette keyboard path (CP-01, CP-04, CP-07), reduce-motion emulation (FB-10) |
