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
| `07-scan` | barcode / SKU + Enter hit, repeat, miss, deactivated; search box focused on open | D8, NV-03, PS-05 |
| `08-session` | wrong password (E7), the banner goes away on any edit and never sits with empty-field messages or survives a reload, reload restores the session from the cookie, no token in web storage, return URL | AUTH-01, SEC-34, D3, NR-06 |
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
| `17-opaque-ids` | an old integer bookmark (`/receipts/31`, `/issues/31`, `/products/31`) is the not-found page and asks the API for nothing; the id of a document in a warehouse the user is not assigned to shows exactly the page of an id that names nothing | SEC-11, ADR guid-keys 3.2 |
| `26-date-field` | the From / To filters are text date fields in the app format (R23): a date typed as `09 Oct 2026`, `2026-10-09` or `09/10/2026` reaches the API as `yyyy-MM-dd` and is shown as `dd MMM yyyy`; `31 Feb 2026` says what to type (error linked with `aria-describedby`); From after To puts the sentence under To and sends no request; Malay and Chinese month names are written and read back; the calendar button is a 44px tab stop with a name ("Choose date From") and a visible focus ring, the hidden picker input is no tab stop | R23, NV-27, PS-15, EA-29 |

A scenario that finds a product bug is marked `test.fixme` with the finding id in the title and the bug is written up in
`docs/team/HANDOFFS.md`. None are open today.

## Run it

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
   PORT=5612 npm run serve:prod                 # or: npm run dev (port 4200)
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
- **Rate limit.** Sign-in and token refresh share 20 requests a minute per address, and every full page load of a signed-in app
  refreshes the session. The suite therefore navigates inside the app (`goTo`) and paces its sign-ins and page loads
  (`src/rate-limit.ts`, which keeps a small log in the temp folder); a full run takes about five minutes because of it.
- **Data.** Each scenario creates products, partners and documents with unique names (`E2E-<token>`) and leaves them; use a
  throw-away database. Nothing is deleted through the API (posted documents are append-only by design).
- **Page objects** live in `src/pages` (`shell`, `documents`, `screens`), API seeding in `src/api.ts`, sign-in fixtures in
  `src/fixtures.ts`. Prefer roles and accessible names; the few CSS selectors are in the page objects with a reason.

## CI

`.github/workflows/e2e.yml` (manual `workflow_dispatch` and weekly, never on pull requests): SQL Server service container, API from
its build output, web production build served on :4200, Chromium, HTML report and traces uploaded on failure.
