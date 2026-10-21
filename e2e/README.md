# IMS end-to-end behaviour tests (Playwright)

Browser tests for the Phase 1 flows in [`docs/design/mvp-ux.md`](../docs/design/mvp-ux.md), written as given / when / then titles and
named after the requirement or QA finding they protect. They drive the real web app against the real API and SQL Server, sign in
through the real login form, and create their data through the public API with unique names. **The suite starts nothing itself.**

| File | Scenario | Protects |
|---|---|---|
| `01-receive` | staff saves a draft (exactly one, no discard dialog), submits, posts; stock rises. Back in the Post dialog creates no second document. List search by remark. Typing over the default quantity gives 12, a decimal erases with Backspace, a double click on Save draft sends one save | IN-01, NV-01, NV-02, NR-01, NR-07, NR-08 |
| `02-issue-shortfall` | issue above available stock: shortfall sentence, "Set to N", submit blocked until fixed | OUT-01, BR-03, E1 |
| `03-approval-override` | override issue: inbox, approve with a reason of 5+ characters, post, stock and ledger; self-approval blocked | OUT-02, BR-07, E3, PS-09 |
| `04-reversal` | reversal blocked (message lists the products), unblocked by reversing the later issue; plain reversal; staff has no Reverse | BR-02, E6, PS-01 |
| `05-stale-document` | two sessions edit one draft: stale banner, locked buttons, Reload | BR-12, E4, PS-08 |
| `06-roles` | menu per role, polite forbidden page, deep link, Clerk creates but cannot edit a partner (409 duplicate sentence) | AUTH-02, E8 |
| `32-roles-list` | Roles from the menu: the five standard roles with their kind in words (Admin "System, locked"), the 5-minute note, search and its empty sentence; the Clerk and the Owner have no Roles item and `/roles` is the forbidden page; 360 px cards without sideways scroll | AUTH-02, RPN-01, RPN-03, RPL-01 |
| `33-roles-editor` | the role page (RP02, view only), real API: a person holding only roles.read opens a role from the list and sees it as "View only" with the permissions ticked by section in disabled boxes and no Save or Delete; the Administrator role says it always holds every permission and cannot be changed; an address that names no role (a GUID nobody has, an old number) is "Page not found"; 360 px without sideways scroll and every box named | RP02, RPE view |
| `35-service-worker` | the only file that runs with the service worker on (needs the production build served with `API_URL`, skips itself on the dev server): after master data was browsed and the user signed out, the same API addresses go to the server and are refused with 401, and no `:data:` cache exists; a product created through the API after a search found nothing shows in Add item after a reload | QA round S1, defect 2, frontend.md decision 18 |
| `34-users-custom-role` | a QA custom role is chosen by name in the Users form, the list shows the name and not the code, and the same user saved again keeps the role; a new user with no warehouse and not "Sees every warehouse" is stopped with a sentence and nothing is sent | QA finding 1, QA finding 5 |
| `07-scan` | barcode / SKU + Enter hit, repeat, miss, deactivated; search box focused on open | D8, NV-03, PS-05 |
| `08-session` | wrong password (E7), the banner goes away on any edit and never sits with empty-field messages or survives a reload, reload restores the session from the cookie, no token in web storage, return URL, a session that expires during Save draft (refresh refused): no "Discard changes?", the sign-in notice says the changes are kept, and the receipt is back after signing in | AUTH-01, SEC-34, D3, NR-06 |
| `09-language` | en / zh / ms switch keeps the half-filled form; choice survives a reload | PRD 9 |
| `10-phone` | scenario 1 at 360x740, no horizontal scroll on the main screens | D1, EA-04 |
| `11-accessibility` | axe (WCAG 2.2 AA) finds no serious or critical violation on the main screens and on the sign-in page at 360 and 1280 px, light and dark, with and without a failure; exceptions need a finding id (`KNOWN` in the file, empty today) | EA-03, EA-21 |
| `25-login` | the sign-in page (R25), failures stubbed so no real attempt is spent: typing and Enter inside a slowed entrance; reduced motion and `data-motion=off` collapse the entrance and the shake; a wrong password shakes once, exactly one announcement, no toast, retry ready; lockout / 429 / 500 sentences; offline notice; password-manager attributes, clean storage, eye button; split page at 1280 and one column below 1024 (landmarks); 44px targets and a 56px button at 360; no sideways scroll at 320 and 360 at 150% and 200% text, and at 320 in en, zh and ms | R25, ER-11, ER-16, EA-24, EA-26 |
| `12-low-stock` | never-stocked product is listed, "New receipt" shortcut is prefilled, product leaves the list after posting | T16, PS-04, NV-13 |
| `13-cost-visibility` | Warehouse staff see no cost or value on the receipt, issue, stock, home and report screens (no money-formatted number anywhere, no Amount or Value column, no Reports); Clerk has the unit cost input, the total and the Amount column but no value; Supervisor sees stock value, sorts by it, and the movements report shows the signed stock effect, the amount and the Reversal row | EA-23, mvp-ux 1.4, 3.9 |
| `15-containment` | 300-character names and 500-character remarks (unbroken Latin, with spaces, Chinese, emoji) and a 1.2-billion amount on the partner, product, user, receipt, stock and report lists, the editor, the Add item sheet, a dialog and a 500-character error toast: no sideways page scroll from 320 to 1440 px, only named scroll regions scroll, no number, date, code or chip cell is broken or cut at 1024 px and up, a cut name opens with Show more by keyboard | L00, N15 |
| `14-required-marker` | red asterisk is aria-hidden, the control carries the `required` state and its name has no "required" in it (the radio legend carries the word), one "* means required" legend on login, receipt, issue and a dialog, none on a list, no "(required)" text, the star stays with the last word at 320px | FR-06, RQ-01..RQ-06, D4 |
| `17-feedback` | a Save draft that fails (API stubbed to 503) is announced once, as one toast, and the Save draft button is not covered (boxes do not overlap, a tap at its centre reaches it) at 360 and 1280; the next write clears the old error; a document that cannot be loaded shows one alert with Retry and no toast; a list that cannot be loaded is announced once; two quick successful saves show "Draft saved." once | R11, ER-02, ER-11, EA-15, EA-26 |
| `16-shell` | phone tab bar at the bottom with 44px+ cells and the current page marked, the sticky action bar directly above it and Submit not covered; skip link (Tab, Enter) moves focus to the content without a reload; the top progress bar never shows for a fast call, shows (named, no value) for a slow one and does not sweep under reduced motion; Mine / All tabs control a labelled tabpanel | R10, ER-09, ER-18 |
| `30-document-corrections` | a wrong receipt is taken back with "Edit items" in the Post dialog (same number, fixed, posted, one document); Escape leaves a Submitted page with Edit next to Post and Edit needs no dialog there; an issue waiting for approval asks first, leaves the inbox, the Supervisor and the Owner never see Edit, a smaller quantity then posts without approval; an Approved issue is cancelled by the Supervisor with a reason of 5+ characters (stock unchanged, list updated) while its creator only sees whom to ask; an Approved Large issue is cancelled by the Owner and not by the Supervisor; an approved issue that stock can no longer cover shows one alert with Cancel document | RQ4, NR-02, PR-02, NR-11, DC-01..DC-06 |
| `17-opaque-ids` | an old integer bookmark (`/receipts/31`, `/issues/31`, `/products/31`) is the not-found page and asks the API for nothing; the id of a document in a warehouse the user is not assigned to shows exactly the page of an id that names nothing | SEC-11, ADR guid-keys 3.2 |
| `26-date-field` | the From / To filters are text date fields in the app format (R23): a date typed as `09/10/2026`, `09 Oct 2026` or `2026-10-09` reaches the API as `yyyy-MM-dd` and is shown as `dd/MM/yyyy` in every language; `31 Feb 2026` says what to type (error linked with `aria-describedby`); From after To puts the sentence under To and sends no request; Malay and Chinese show the same `dd/MM/yyyy` with their own pattern hint (`hh/bb/tttt`, `日/月/年`) and still read a typed month name or `2026年10月15日`; the calendar button is a 44px tab stop with a name ("Choose date From") and a visible focus ring, the hidden picker input is no tab stop | R23, NV-27, PS-15, EA-29 |
| `31-table-toolbar` | the table toolbar (TB), the audit list answered by a route stub of 333 rows: at 1280 First and Last go to pages 1 and 14 (range `326-333 of 333`, request page 14), Previous to 13, Rows per page 50 starts again on page 1 with `pageSize=50`; the focus stays on the pressed button (Last and Next are aria-disabled by the new page, not disabled), ArrowDown on Rows per page goes 25, 50, 100 without leaving the select, and the pager has one polite live region reading `Page 2 of 14, 26-50 of 333`; the Columns popover opens with the focus on its first item, offers neither the identifier nor the action column, hides and restores a column, closes with Escape (focus back on the button) and with a click outside; at 360 only Previous, Next and the range remain, no Columns button, no sideways scroll | TB, A05.1, A03.1 |
| `36-reflow` | the Products list on a 360 px phone is a compact card (under 130 px each) and Next page after scrolling down starts the table at the top of the window; at 320 px with the text at 200% (plain viewport, not `isMobile`, which would zoom out and hide the overflow) both Home buttons end inside the window, the tab labels stay whole and the bar scrolls instead of the page, 17 admin screens and a new role page do not scroll sideways | QA round 2026-10-19 novice 2, edge 7, WCAG 1.4.4, 1.4.10 |
| `32-overlays` | the dialog fades and pops over 200ms / 280ms from tokens and ends still; reduced motion sets the lift to 0px, the pop scale to 1 and every transition to under 1ms; Escape returns the focus to the account button; the scrim is the overlay colour at 0.5 (light) and 0.6 (dark); at 360 and 320 px the dialog is a bottom sheet on the bottom edge with 16px top corners, slide 100%, and its buttons stay inside | R13 |
| `38-change-password` | Change password (`/account/password`), real API, every change made by a throw-away user with a made-up password (turned off afterwards), never a shared QA or demo user: from the Account dialog link, two fields (no repeat field) with `current-password` and `new-password` hints and an eye button each (one constant name, `aria-pressed`), a good change signs out, opens sign-in with the notice, the old password is refused by the API and the new one signs in, and nothing typed is in the address, web storage or a toast; a wrong current password is the API sentence under Current password (linked by `aria-describedby`, focused, no banner or toast, still signed in, typing clears it); the checks (empty, 11 characters, same as current, judged again when the current one changes) send nothing and focus the first wrong field; the Users hint names Account > Change password; 360 px, and 320 px with the text at 200% in en, ms and zh, no sideways scroll or clipping with the messages showing; axe (light and dark) | Change password screen, WCAG 1.4.4, 1.4.10, 3.3.1, 4.1.3 |
| `39-modules` | Settings > Modules (PL1.4): on the real API the Admin opens it from the menu, the list matches `GET /api/modules`, accounting is a switch like any other (not disabled, no "Available soon"; the PL1.4 gate is gone, `40-accounting` flips it) and opens no dialog until it is used, Settings is not the active item on it; the Clerk and the Owner have no Modules item and `/settings/modules` is the forbidden page; against a stubbed `/api/modules` with a made-up second module (accounting is never turned on): the reason dialog (under 5 characters sends nothing), one toast with "2 roles regain their saved permissions", the list is read again, the focus returns to the switch, off again, cancel sends nothing, 400 / 403 / 404 / 409 each say it once in one alert and no toast, and a 404 from another screen after the module was turned off is explained once as "This feature was turned off"; 360 px, 320 px at 200% text in en, ms and zh, axe light and dark | PL1.4, plan risk 11, ADR modules 4.5 |
| `40-accounting` | the accounting module, forms and import, on the real API (PL1.8a), serial, one throw-away product and partner, Ims_Dev put back as found (the module state is read first and restored last, also after a failure; tax values are cleared and the rows deactivated, found by the spec's own name prefixes in `src/accounting.ts`): the Admin turns accounting ON from Settings > Modules (a reason under 5 characters sends nothing, one toast, the switch keeps the focus); the product form shows HS code and SST code, refuses `0901-21` in words with the focus on the field, saves ` 0901.21 ` as `0901.21`, shows it after a reload, keeps both codes when only the name is edited (round trip against the API) and clears one on purpose; the partner dialog does the same for TIN and SST registration no. (an edit of the contact alone keeps both); the product import page names HsCode and SstCode and the Text format; 320 px at 200% text in en, ms and zh (product form), axe light and dark (product form, partner dialog); then the Admin turns it OFF with a reason: the fields and the import hint are gone and the API leaves the stored codes out | PL1.8, plan risk 11, plan acceptance PL1.8 items 1, 5 and 6 |
| `41-accounting-export`    | Reports > Accounting export on the real API (PL1.8b), serial, Ims_Dev put back as found (module state read first and restored last, also after a failure; one throw-away product with codes, partner with tax numbers and posted receipt made through the API, the receipt reversed, the rows deactivated with their tax values cleared): the Admin opens Accounting export from the menu (Reports is not the current item there), an empty period sends nothing and names the missing date with the focus on From, a double click sends one request, the file is `ims-accounting-<from>-<to>.xlsx`, sheet `Accounting`, the 17 fixed headers, and the row of the SKU carries document no., partner, TIN, SST reg. no., HS code, SST code, quantity 10 and amount 50 (read from the .xlsx itself by `src/xlsx.ts`); the row-cap sentence of the API (stubbed) is one alert, no toast, no file, the button usable again; From after To says so under To and sends nothing; the Supervisor and Warehouse staff have no menu entry and a typed `/reports/accounting` is the forbidden page, the Owner has the entry; 320 px at 200% text in en, ms and zh, axe light and dark (with and without the error showing); module OFF: the entry goes, `/reports/accounting` is the not-found page and the API answers 404 | PL1.8, plan acceptance PL1.8 items 2 to 4                   |
| `42-purchase-orders`      | purchase orders on the real API (PL2.7a menu, guards and list, PL2.7b the read-only detail; the action bar follows in PL2.7c), serial, module state read first and restored last (also after a failure; Ims_Dev is left as found); throw-away suppliers, product and orders made through the API, every order cancelled again, the suppliers and product stay: purchasing OFF has no menu entry and `/purchase-orders` is the not-found page, the Admin turns it ON from Settings > Modules (a reason under 5 characters sends nothing) and the entry appears; the Clerk, Supervisor, Warehouse staff and Owner (all hold po.read) have the entry; the list filters by status, supplier, number and date (an empty result says so and Clear filters brings the rows back), the Supervisor finds the Submitted order marked Waiting for you and the submitter does not; a row opens the detail (header, status, supplier, warehouse, remark, lines, total, history); a part-received order shows ordered 10, received 6, remaining 4 with the receipt linked to `/receipts/:id`, and once closed short says how and why; an id that is not a GUID is the not-found page with no request; 360 px cards, 320 px at 200% text in en, ms and zh, axe light and dark at 360 and 1280 px; purchasing OFF again: the entry goes and the API answers 404                                                                                                                                                                                                                                                                                                                     | PL2.7, plan acceptance PL2.7 items 1 and 2                  |

A scenario that finds a product bug is marked `test.fixme` with the finding id in the title and the bug is written up in
`docs/team/HANDOFFS.md`. None are open today.

## Run it

### The whole suite in one sequence (clean checkout, Git Bash, from the repository root)

Takes about 5 minutes for the 131 scenarios on a fresh database, plus the one-off installs and builds (about 3 minutes).
Use ports that are free on your machine; the database is a throw-away one that you drop at the end (never point this at a
database somebody uses: the suite creates data and changes settings and approval rules while it runs).

```bash
export API=http://localhost:5611 WEB=http://localhost:5612
DB='Server=(localdb)\ImsDev;Database=Ims_E2E;Trusted_Connection=True;TrustServerCertificate=True'   # or any SQL Server

# 1. one-off: install and build
dotnet build src/Ims.Api/Ims.Api.csproj -c Release
(cd web && npm ci && npm run build:prod)
(cd e2e && npm ci && npx playwright install chromium)

# 2. start the API (fresh database: migrations and the dev seed run on start) and the web build, in the background
(cd src/Ims.Api/bin/Release/net10.0 && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=$API \
  Cors__Origins=$WEB "ConnectionStrings__Default=$DB" nohup dotnet Ims.Api.dll > /dev/null 2>&1 &)
(cd web && PORT=5612 API_URL=$API nohup npm run serve:prod > /dev/null 2>&1 &)
until curl -fsS $API/health > /dev/null && curl -fsS $WEB/login > /dev/null; do sleep 2; done

# 3. run everything (or name spec files, see docs/team/E2E-HEALTH.md for a smoke set)
(cd e2e && IMS_API_URL=$API IMS_WEB_URL=$WEB npm run test:local)

# 4. stop the two servers, then drop the database
sqlcmd -S "(localdb)\ImsDev" -Q "ALTER DATABASE Ims_E2E SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE Ims_E2E"
```

A run on a database that already holds data from an earlier run is supported and passes too (names are unique, the settings a
scenario changes are put back, and posting through the API approves a document that an existing approval rule holds back).
For a clean slate drop and recreate the database. The kept sessions in `e2e/.cache` of an earlier run do no harm: a session the API
does not know is dropped and the form signs in.

The numbered steps below explain each part.

Needs Node 20+ (the web build needs the Node the web app asks for, see `web/README.md`), .NET 10, and a SQL Server the API can use.
Examples use the free ports 5611 (API) and 5612 (web); any ports work.

1. **API** with a throw-away database. Demo users are only created when `ASPNETCORE_ENVIRONMENT=Development` and the database is
   local. `Cors__Origins` must list the exact origin of the web app (the refresh cookie only travels to listed origins).

   ```bash
   dotnet build src/Ims.Api/Ims.Api.csproj -c Release
   cd src/Ims.Api/bin/Release/net10.0          # start here so appsettings*.json are found
   ASPNETCORE_ENVIRONMENT=Development \
   ASPNETCORE_URLS=http://localhost:5611 \
   Cors__Origins=http://localhost:5612 \
   "ConnectionStrings__Default=Server=(localdb)\\ImsDev;Database=Ims_E2E_Scratch;Trusted_Connection=True;TrustServerCertificate=True" \
   dotnet Ims.Api.dll
   ```

   PowerShell: set `$env:ASPNETCORE_ENVIRONMENT`, `$env:ASPNETCORE_URLS`, `$env:Cors__Origins`, `$env:ConnectionStrings__Default`, then
   `dotnet Ims.Api.dll`. The demo passwords come from the `Seed` section of `appsettings.Development.json` or `Seed__AdminPassword` /
   `Seed__DemoPassword`.

2. **Web**: any build works; the tests point the app at `IMS_API_URL` themselves (they answer `assets/config.json`).

   ```bash
   cd web && npm ci && npm run build:prod       # Node 22.6: NODE_OPTIONS=--experimental-require-module
   PORT=5612 API_URL=http://localhost:5611 npm run serve:prod   # or: npm run dev (port 4200); API_URL is for 35-service-worker
   ```

3. **Tests**:

   ```bash
   cd e2e && npm ci && npx playwright install chromium
   export IMS_API_URL=http://localhost:5611 IMS_WEB_URL=http://localhost:5612
   npm run test:local                           # reads the dev passwords from appsettings.Development.json for this run only
   ```

   Or set the five passwords yourself and use `npm test`:
   `IMS_E2E_PASSWORD_ADMIN`, `IMS_E2E_PASSWORD_OWNER`, `IMS_E2E_PASSWORD_SUPERVISOR`, `IMS_E2E_PASSWORD_STAFF`, `IMS_E2E_PASSWORD_CLERK`
   (users `admin`, `owner`, `supervisor`, `staff`, `clerk`). Passwords are never printed, logged or committed. Defaults:
   `IMS_WEB_URL=http://localhost:4200`, `IMS_API_URL=http://localhost:5109`. The phone scenarios
   set their own 360x740 viewport. Single scenario: `npm run test:local -- tests/03-approval-override.spec.ts`.

   A missing server or password stops the run with one message (`src/global-setup.ts`). Reports: `npm run report`; failures keep a
   screenshot and a trace in `test-results/`.

4. **Clean up**: stop the API and the web server, then
   `sqlcmd -S "(localdb)\ImsDev" -Q "ALTER DATABASE Ims_E2E_Scratch SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE Ims_E2E_Scratch"`.

## Things to know

- **One worker, on purpose.** Two scenarios switch the global setting "allow negative stock" (restored afterwards) and every
  sign-in shares the API's login limit. Run the suite against a database nobody else is using.
- **Rate limit and kept sessions.** Sign-in is limited to 20 requests a minute per address (the token refresh a page load makes has its
  own limit of 300). The suite does not sign in through the form for every scenario: a scenario hands the refresh cookie of its
  role back to `e2e/.cache` when it ends (`src/session-cache.ts`), and the next scenario of that role starts from it (the API
  rotates the cookie on every use, so an entry is single-use; a signed-out, expired or unknown one falls back to the form). What
  still signs in through the form (the login scenarios, the first scenario of each role) spends from a paced budget of 15 a
  minute (`src/rate-limit.ts`, its log is in `e2e/.cache` too). This took a full run from about 20 minutes to about 5. A
  scenario about the form itself (return URL, wrong password) must use `signInThroughForm`, not the `signIn` fixture.
- **Data.** Each scenario creates products, partners and documents with unique names (`E2E-<token>`) and leaves them; use a
  throw-away database. Scenarios do not depend on what an earlier run left: the two that change a global setting restore it, the
  approval rule one finds or creates one rule and switches it off, and `api.post` approves a document that a rule holds back. Nothing is deleted through the API (posted documents are append-only by design).
- **Page objects** live in `src/pages` (`shell`, `documents`, `screens`), API seeding in `src/api.ts`, sign-in fixtures in
  `src/fixtures.ts`. Prefer roles and accessible names; the few CSS selectors are in the page objects with a reason.

## CI

`.github/workflows/e2e.yml` (manual `workflow_dispatch` and weekly, never on pull requests): SQL Server service container, API from
its build output, web production build served on :4200, Chromium, HTML report and traces uploaded on failure.
