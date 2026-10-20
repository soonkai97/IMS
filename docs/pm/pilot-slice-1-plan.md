# Pilot slice 1 plan: module foundation, tax-ready fields and the accounting export

Author: project-manager (be-architect and business-analyst points marked). Date: 2026-10-19. Status: plan for the owner and the team; nothing here is built.
Inputs: `docs/architecture/adr-modules-and-tracking-dimensions.md` (Accepted 2026-10-19; sections 4, 8, 12, 13), `docs/design/lean-scope.md` (section 1 rule), `docs/pm/ROADMAP.md` (sections 3.2, 3.3, 5, 8), `TASKS.md`, `docs/team/PR-REVIEW.md` section 8, the PRD (`docs/private/prd-v0.2.txt`, read on the owner's checkout), and the code at `origin/main` dd66ea2 (`Partner`, `Product`, `ExportMovementsHandler`, `PermissionCatalog`, `SystemRoleDefaults`).
Owner decisions this plan takes as given (2026-10-19): Q1 pilot is a small trading / wholesale business; build order is (1) accounting export plus tax-ready fields carrying the module foundation, (2) purchase orders with receive-against-PO and reorder, (3) delivery / sales orders and returns, (4) stocktake; batch/expiry and serial come later. Q6 only an Admin flips a module, with a reason, audited. Q7 one fixed export layout. Standard approval is by a Supervisor, Large by the Owner, no Owner fallback.

## 1. Which requirement needs this (lean-scope rule)

| Piece | Requirement it serves | Note |
|---|---|---|
| Accounting export | PRD goal 2.1 ("export to accounting software so customers do not re-key data"), INT-01 (accounting export templates, P1, Phase 3), narrowed by the owner's Q7 to one fixed layout; ADR R1 (the owner's ranked market list, first place) | INT-01 asks for per-package templates with configurable mapping; we build neither. |
| Tax-ready fields (HS code, SST code, partner TIN, SST registration no.) | PRD non-goal 2.2 ("we only export the data needed for invoices to the accounting software"); ADR R1 | Shape validation only. No e-invoice submission. |
| Module foundation (catalog, `Module.<code>` rows, `modules.manage`, 404 when off, `UserInfo.modules`) | Not a requirement of its own: ADR section 8 row 0 says it is built inside the first module's slice, because it has no user value alone. Owner decision Q6. | The one configuration surface this slice adds. Justified by Q6; nothing else here is configurable. |

Checklist answers. Simpler way? Yes for the switch (a `SystemSetting` row, no table) and for the export (extend the movements export, no new report engine); both are already the plan. New dependency? None: ClosedXML and the report writer exist. New configuration surface? One, the module switch (Q6).
Gap to say plainly: PRD priorities put INT-01 in Phase 3 and the owner ranked it first for the pilot. That is a re-ranking by the owner, recorded here, not a scope creep.

## 2. Scope

| In | Out (with the reason or trigger) |
|---|---|
| `ModuleCatalog` with one entry, `accounting`; switch stored as `Module.accounting` in `SystemSetting` (missing row = off) | `Requires` between modules and `CanDisable` (added with the second module, PL2); first-run screen; per-user or per-role switches (ADR section 5) |
| `GET /api/modules`, `PUT /api/modules/{code}` (`{enabled, reason}`), permission `modules.manage` (core, dangerous, Admin only), `UserInfo.modules` | A public (anonymous) config endpoint (not needed) |
| 404 for every endpoint of an off module (middleware), module-aware permission list and role save (hidden grants kept) | Switch effect on posting or the ledger (none by design) |
| `Product.HsCode`, `Product.SstCode`, `Partner.Tin`, `Partner.SstRegNo`: nullable, shape-validated, on the product and partner forms and the product import template, shown only while the module is on | Tax calculation, SST rates, lookups, checksum validation, e-invoice (MyInvois) submission, `Partner.Code` (deferred in lean scope) |
| `GET /api/accounting/export`: one fixed `.xlsx` layout of posted document lines in a date range, reversals as negative rows, refused (not cut) above the row cap | CSV, a second layout, per-package adapters, a mapping screen, API push, an "already exported" marker (trigger: an accountant imports a period twice and asks for it), selling prices (arrive with price lists in PL3) |
| Oracle fixtures and statement-count test on current `main` (ADR rollout step 0), in parallel | Any change to posting, the ledger or `StockBalance` |
| Web: modules screen, menu and route gating, tax fields in two forms, export screen, en/zh/ms keys, e2e on the real API | Home tiles, charts, notifications for the export |

## 3. Sub-slices, in build order

Size = changed lines, tests included, generated client excluded. Rate basis: `ROADMAP.md` section 5 (web about 135 lines per agent-hour all in) and lean-scope 6.1 (roles backend about 70 lines per agent-hour including review); one agent-day = 6 productive hours. "Days" = build plus review, then with the 25% rework allowance of the roadmap. Tier: see section 8.

| Id | Sub-slice | Lines | Role (build) | Depends on | Review tier | Days | With rework |
|---|---|---|---|---|---|---|---|
| PL1.0 | Oracle fixtures and statement-count test (tests only, ADR section 10 items 1, 2) | 700 | integration-test-engineer, unit-test-engineer | none | Standard | 1.0 | 1.5 |
| PL1.1 | Module foundation, backend: catalog, switch service, `GET/PUT /api/modules`, `modules.manage`, `[RequiresModule]` and gate middleware, `UserInfo.modules`, audit event, `modules` lock, grant migration | 900 | be-framework | none (baton: migration 1) | **Full** | 2.0 | 2.5 |
| PL1.2 | Module-aware roles: `PermissionDefinition.Module`, `accounting.export` (Requires `cost.view`), permission list filter, role save keeps hidden grants, enable reply counts regained grants, grant migration | 600 | be-framework | PL1.1 (baton: migration 2) | **Full** | 1.5 | 2.0 |
| PL1.3 | Contract batch CS6: regenerate the web client (modules, permission `module` field, `UserInfo.modules`); nothing else in the PR | 300 generated | fe-framework | PL1.1, PL1.2 merged; "contract freeze CS6" in HANDOFFS | Standard | 0.5 | 0.5 |
| PL1.4 | Web foundation: `ModulesStore`, `MenuItem.module`, `requireModule`, not-found for an off module, "turned off" refetch, Settings > Modules screen (switch with reason), e2e | 800 | fe-framework | PL1.3 | **Full** (shell and navigation wide) | 1.0 | 1.5 |
| PL1.5 | Tax-ready fields: migration `AddTaxReadyFields`, domain shape rules, product and partner create/update/get, import columns, tests | 800 | be-framework, be-dba (migration) | PL1.1 (needs the switch); baton: migration 3 after PL1.2 merges | **Full** | 2.0 | 2.5 |
| PL1.6 | Accounting export: query, handler, fixed layout, row-cap refusal, signed reversal rows, scope, audit event, tests; DBA timing at the 1M tier | 700 | be-framework, be-dba (timing) | PL1.2, PL1.5 | **Full** | 1.5 | 2.0 |
| PL1.7 | Contract batch CS7: regenerate for tax fields and the export endpoint | 300 generated | fe-framework | PL1.5, PL1.6 merged; "contract freeze CS7" | Standard | 0.5 | 0.5 |
| PL1.8 | Web accounting: tax fields in product and partner forms, product import hint, export screen and menu entry, e2e | 800 | fe-framework | PL1.4, PL1.7 | Standard (plus a cost-visibility DOM scan) | 1.0 | 1.5 |
| | **Total** | about 4,800 hand-written (5,400 with generated) | | | | **11.0** | **14.5** |

Why this split. Nothing is over about 900 lines (limit about 1,000). The backend is four slices because each carries a different review concern (pipeline and auth; role data loss; database shape; money). Two client batches instead of one let the web foundation (PL1.4) run beside PL1.5 and PL1.6 instead of waiting for them. ADR section 8 sized "foundation plus accounting" at M to L (about 1,500 lines foundation with web, plus M); this plan is higher (foundation 2,300 with the roles merge and web) because the ADR's review rounds added the hidden-grant merge after that size was written, and because tests are counted. If the owner wants less, the first cut is PL1.2's enable-reply count and the import columns of PL1.5 (about 150 lines together).

### Order and parallelism

```
PL1.0 (tests, parallel lane, any time before PL3 starts)
PL1.1 -> PL1.2 -> PL1.3 (CS6) -> PL1.4 (web foundation) ----------------+
            \-> PL1.5 (code starts after PL1.1; migration after PL1.2) -> PL1.6 -> PL1.7 (CS7) -> PL1.8
```

One migration open at a time (`ROADMAP.md` 3.2): `GrantModulesManage` (PL1.1, data only), `GrantAccountingExport` (PL1.2, data only), `AddTaxReadyFields` (PL1.5, schema). Each `migrations add` waits for the previous merge. The migration chain on `main` ends at `DropLegacyRoleColumns` (20261017125040); new ones follow it.

## 4. API and database changes

| Where | Change | Rules |
|---|---|---|
| `SystemSetting` | rows `Module.accounting` = `true`/`false`; no table, no column, no seed (missing = off) | Not row-audited, so the flip handler writes its own event `module.enabled` / `module.disabled` (actor, reason, before, after, roles regaining grants). Key length well under 100. Written under the application lock resource `modules` (`retry: false`, a busy lock is a 409 `LockBusy`, never held with a stock lock). |
| `RolePermission` | rows for `modules.manage` (Admin) and `accounting.export` (Admin and Owner, see question 5) added by data-only migrations | Test on a fresh and on a migrated database fails the build if Admin lacks a catalog code (`ROADMAP.md` 3.2 rule 6). `Down` removes exactly those rows. Tokens are 5 minutes: an Admin gets `modules.manage` at the next refresh after deploy; put that in the deploy note. |
| `Product` | `HsCode`, `SstCode` `NULL`, no default | Proposed types: same string type as `Sku`; `HsCode` at most 16 characters, digits and dots; `SstCode` at most 20, letters, digits and hyphen. be-dba and the business-analyst confirm sizes and shapes against the accountant's package. Add is metadata-only (50 k rows, seconds). |
| `Partner` | `Tin`, `SstRegNo` `NULL`, no default | Proposed: `Tin` at most 20, letters and digits; `SstRegNo` at most 30, letters, digits and hyphen. Same checks. |
| Concurrency and audit | Existing `ROW_VERSION` of `Product` and `Partner` covers the new columns; no new token. Row-level audit (audit columns, `REC_*`) records old and new values of the new columns automatically. | `Down` of `AddTaxReadyFields` refuses while any value is set (same philosophy as ADR 3.8: do not lose data silently); roll back by restoring the backup. be-dba may overrule with a documented reason. Up, Down, Up on a database with rows is a required test. |
| `GET /api/modules` | any signed-in user; `[{code, name, enabled, requires}]` | No policy: goes on the fixed no-policy list of `EndpointCoverageTests`. |
| `PUT /api/modules/{code}` | policy `modules.manage`; body `{enabled, reason}` (reason at least 5 characters); unknown code 404 | New endpoints added to `EndpointCatalog`. |
| `UserInfo.modules` | enabled codes, built at sign-in, refresh and `/me`; not a claim | Positional record: the new parameter changes the constructor for every caller and test double. Done in PL1.1; the client changes in CS6. |
| `GET /api/permissions`, `PUT /api/roles/{id}/permissions` | module permissions listed and accepted only while their module is on; the save merges the role's hidden grants back (never audited as removed); `Requires` closure checked on the merged set | `RoleContainment` unchanged (counts hidden codes like any other). |
| `Product`/`Partner` endpoints | fields accepted and returned only while `accounting` is on; off: a request carrying them is 400 "not available", responses omit them, **a PUT without them leaves stored values alone** | Review point: data loss on partial input (`PR-REVIEW.md` section 8). |
| `GET /api/accounting/export` | `[RequiresModule("accounting")]`, policy `accounting.export`; query `from`, `to` (required), `warehouseId` (optional) | Out-of-scope warehouse id is 404 (never 403). |

Fixed export layout (one sheet `Accounting`, English headers, not localised, because it is a file contract): Date (Kuala Lumpur posting date), Document no., Type, Purpose, Reversal of, Warehouse, Partner, Partner TIN, Partner SST reg. no., SKU, Product, HS code, SST code, Unit, Quantity, Unit cost, Amount. Quantity and Amount carry the sign of the stock effect (an issue is negative, a reversal is the opposite of its original) so a column sum is net. Amount is the stored line amount (2 decimals, half away from zero), Unit cost 4 decimals. File name `ims-accounting-<from>-<to>.xlsx`. The business-analyst and the owner confirm the column set against the pilot's accountant (question 2) before PL1.6 starts.

## 5. Acceptance criteria

Given / when / then, one block per sub-slice. Each criterion is a named test; the requirement id is in the test name.

**PL1.0 Oracle**
1. Given `main` before any pilot change, when the generator runs, then compact CSVs of scripted and seeded-random sequences (receipts, issues, reversals, negative-stock override, unit conversion, multi-line same-product documents) are written with ledger rows, final balances, line snapshots and raised exceptions.
2. Given the fixtures, when the current engine replays them, then every value matches; changing a cost calculation makes the test fail.
3. Given an untracked document posted through the real host, then the SQL statement count equals the frozen count.
It must be merged before PL3 or PL4 starts (they ride on posting). It is not a gate for PL1.1 to PL1.8 (no posting change).

**PL1.1 Foundation, backend**
1. Given an installation with no `Module.*` row, when a signed-in user calls `GET /api/modules`, then `accounting` is listed with `enabled: false`; an anonymous caller gets 401.
2. Given an Admin with `modules.manage`, when `PUT /api/modules/accounting {enabled: true, reason: "Pilot start"}`, then 200, `Module.accounting` is `true`, exactly one `module.enabled` audit event holds actor, reason, before and after, and the next GET shows `enabled: true`.
3. Given a reason of fewer than 5 characters or an unknown code, then 400 or 404, no setting change and no audit row.
4. Given a caller without `modules.manage`, then 403.
5. Given the module is off, when a signed-in caller of any role calls a `[RequiresModule("accounting")]` endpoint, then 404 with the same problem details as an unknown route; anonymous 401; module on and no permission 403; with permission 200. (Generated test, one row per case.)
6. Given two simultaneous flips, then one waits or answers 409 `LockBusy`; the audit shows each flip once with the right before and after.
7. Given a flip, then `UserInfo.modules` at sign-in, refresh and `/me` reflects it, and the server gate reflects it on the very next request.
8. Architecture tests: module codes unique; every controller in a module namespace carries `[RequiresModule]`; Domain posting types do not reference `IModuleSwitches` (a switch never changes ledger behaviour); every catalog permission is held by Admin; `NoRoleDecisionTests` and `EndpointCoverageTests` green; with all modules off the OpenAPI document equals today's minus the module paths.

**PL1.2 Module-aware roles**
1. Given `accounting` is off, then `GET /api/permissions` omits `accounting.export`; on, it is listed with `Requires: cost.view, masterdata.read, reports.read` (widened in the PL1.6 review, the file shows partners and products).
2. Given it is off, when a role save includes `accounting.export`, then 400 "not available".
3. Given a role holds `accounting.export` and the module is off, when an administrator changes another permission of that role, then the role still holds `accounting.export` afterwards and neither the reply's `added`/`removed` lists nor the audit row name it.
4. Given the editor removes `cost.view` from a role that holds the hidden `accounting.export`, then 400 naming the dependency (closure on the merged set).
5. Given the module is turned on again, then the reply of `PUT /api/modules/accounting` states how many roles regain the grant, and the audit event records it.
6. Given a delegated role manager who does not hold `accounting.export`, when they edit a role that holds it, then containment refuses it like any other code.
7. Given a person holds the permission while the module is off, then their token still carries the claim and the export endpoint answers 404, not 403.
8. Given a fresh and a migrated database, then Admin and Owner hold `accounting.export` and the other three system roles do not; `Down` removes the rows.
Decision for be-architect at the start of this slice: whether a role's detail (`GET /api/roles/{id}`) lists hidden grants. Default: omit them, so the editor shows no row it cannot save.

**PL1.4 Web foundation**
1. Given `UserInfo.modules`, then a menu item with `module` shows only when the module is on and any listed permission is held (unit matrix: module x permission).
2. Given the module is off, when a user opens a module route by URL, then the not-found page shows, not the 403 page.
3. Given an Admin with `modules.manage`, when they open Settings > Modules, flip a module and give a reason of 5 or more characters, then the result is announced once, the list reloads from `GET /api/modules`, and a person without the permission has no entry and a forbidden route. **As built:** `accounting` (the only module) is shown as an off switch that cannot be used yet (risk 11), so the flip is proven in the browser against a stubbed `/api/modules` with a made-up second module and in unit tests; flipping `accounting` itself is PL1.8's.
4. Given a stale screen receives the 404 "This feature was turned off", then the web refetches the module list and says so in words.
5. Given 360 px and 1280 px, light and dark, en/zh/ms keys present (zh and ms `TODO-review`), no role-name literal, the switch announces its state in words.
6. e2e on the real API: the list equals `GET /api/modules`, `accounting` is off and its switch cannot be used (the e2e must not turn `accounting` on, risk 11), a user without the permission cannot reach the screen. **As built:** the on/off flip with a reason is e2e-tested against the stub of item 3; the real-API flip (Admin turns `accounting` on and off, both states listed) moves to PL1.8, when the gate is lifted.

**PL1.5 Tax-ready fields**
1. Given `accounting` is on, when a user with `masterdata.manage` saves a product with `hsCode` and `sstCode` or a partner with `tin` and `sstRegNo`, then they are stored, returned and shown in the row-level audit as old and new values.
2. Given a malformed value (wrong characters, too long), then 400 with the field named; no checksum or lookup is attempted.
3. Given the module is off, when a request carries any of the four fields, then 400 "not available"; a GET omits them; a PUT without them leaves stored values unchanged.
4. Given the module is on, when a PUT sends `null` (or a blank) for a field, then it is cleared; when the key is NOT in the body, then the stored value is kept (a PUT replaces the other fields, but each tax key is decided on its own). **As built:** first shipped as "null clears" with an omitted key also clearing (the DTO default `null` could not tell the two apart), which made switching `accounting` ON unsafe for any client that never sends the keys (risk 11). The API follow-up of PL1.8 (`Optional<T>`: absent / null / value, see ADR 4.3 "As built in the PL1.8 API follow-up") closed it: an absent key keeps, an explicit null, `""` or whitespace clears, a value replaces after the shape check, a pair with one key changes only that key; on create an absent key is none; with the module off an absent key is nothing, a blank is ignored and a value is a 400. Pinned by `TaxFieldPresenceTests` (real SQL Server, raw JSON bodies), `OptionalTests` and the "null clears" cases of `TaxReadyFieldsTests`, which stay.
5. Given a products import file with optional `HsCode` and `SstCode` columns, then rows are validated by the same shape rules and bad rows are listed with their errors; with the module off the columns are not accepted.
6. Given a database with rows, then Up, Down, Up works; Down refuses while a value exists; the add is metadata-only.
7. Given a partner TIN, then it is not written to the application log and is returned only to callers who can already read partners.

**PL1.6 Accounting export**
1. Given the module is on and the caller holds `accounting.export`, when they request `from` 1 Oct to `to` 31 Oct 2026, then they receive one `.xlsx` with the fixed headers and one row per posted document line in that range, dated by posting date in Kuala Lumpur.
2. Given a posted receipt of 10 at 5.00 later reversed, then the file has a row with 10 and 50.00 and a row with -10 and -50.00 marked "Reversal of <doc no.>", and the column sums are 0.
3. Given more rows than `Limits.MaxExportRows`, then 400 "Too many rows: narrow the date range" and no file. (The existing movements export cuts silently at that cap; for an accountant a silently short file is a wrong book, so this export must not.)
4. Given the module is off, 404; given no permission, 403; given a warehouse id outside the caller's scope, 404; rows of warehouses outside the scope never appear.
5. Given a posted document, then the file Amount for each line equals the amount on screen and the document total equals the sum of its lines; the export total for a range equals the sum of the ledger value for the same range (reconcile test).
6. Given a product or partner with no tax values, then the cells are blank and the export succeeds.
7. Given a role without `cost.view`, then it cannot be given `accounting.export` (the closure rule).
8. Given an export, then one audit event records actor, range, warehouse and row count (security-engineer confirms; the file carries cost).
9. DBA at the 1M tier (`Ims_Perf`, exclusive): the timing of a one-month export at the cap is recorded in `docs/dba/README.md`; the query uses the existing movements indexes.

**PL1.8 Web accounting**
1. Given the module is on and the user may manage master data, then the product form has HS code and SST code and the partner form has TIN and SST registration no., with shape errors in words; with the module off the fields are absent.
2. Given the module is on and the user holds `accounting.export`, then Reports has an "Accounting export" entry with a required date range, an optional warehouse and a Download button; the row-cap error is shown in words; the entry is absent otherwise.
3. Given a user without `cost.view`, then no cost or value appears on the new screens (and the DOM scan of `13-cost-visibility` stays clean).
4. e2e on the real API: switch on, create a product with an HS code, receive and post, download the export, check the row including the HS code; switch off, the direct URL shows not-found.
5. **Round trip (gate, see risk 11).** Given the module is on, then the product form and the partner form ALWAYS send the four fields (`hsCode`, `sstCode`, `tin`, `sstRegNo`), with the values they loaded, so saving an unrelated edit leaves them as they were (while the API treated an omitted key as a clear, acceptance PL1.5 item 4 as first shipped; since the API follow-up an omitted key keeps the value, and the forms keep sending the four fields because an explicit value or a clear must still be expressible). A form-level test loads a record that has all four, edits only the name, saves, and asserts the request body carries the four unchanged values.
6. **Gate lifted (from PL1.4).** The Settings > Modules screen keeps `accounting` as an off switch that cannot be used (`NOT_YET_SWITCHABLE_ON`); this slice deletes that entry in the same PR as item 5, and the real-API e2e of PL1.4 item 6 is added here: an Admin turns `accounting` on and off with a reason, the list shows both states, and the module is left off afterwards.

**As built (PL1.8, split in two pull requests because of size):** PL1.8a is items 1 (forms), 5 and 6 plus the product import hint; PL1.8b is items 2 to 4 (the export screen). (1) The forms send both keys of a pair whenever the module is on: a loaded value goes back as loaded, an emptied field goes as `""` (the server stores a blank as none), nothing is sent while the module is off. (2) A record that carries a tax value proves the server had the module on, so a screen whose module list is older (another tab switched it on) still shows and sends the value instead of clearing it. (3) The shape rules run on the trimmed value, the same as `TaxFieldShapes` (a spec reads the C# limits and characters). (4) The gate is lifted on both sides in the one PR (web `NOT_YET_SWITCHABLE_ON`, API `NotYetSwitchableOn` and its handler guard). (5) The Accounting export is its own menu item in Insight (`/reports/accounting`, tied to the module and to `accounting.export`), not a tab of the Reports page, so the menu rule of PL1.4 does the hiding. (6) e2e `40-accounting` (8a) and `41-accounting-export` (8b) flip the module on the real API and leave Ims_Dev as they found it.

## 6. Risks

| # | Risk | L / I | Containment |
|---|---|---|---|
| 1 | The gate middleware or the permission merge is wrong and every endpoint answers 404, or a disabled module leaks 403 (a probe) | M / H | Full review with the security-engineer; generated tests for each case; the all-modules-off OpenAPI equality test; ADR 4.3 order (between authentication and authorization). |
| 2 | Saving a role while a module is off deletes its hidden grants | M / H | The merge is its own slice (PL1.2) with the F12 tests of the ADR; reviewed by the security-engineer and be-architect. |
| 3 | The export is silently wrong: sign, rounding, a dropped reversal, a warehouse leak, a cut file | M / H | Reconcile test against the ledger, signed rows, refusal above the cap, scope tests; data-flow-analyst traces each column; money review. |
| 4 | The layout does not match what the accountant imports; the column order is hard to change once a pilot uses it | M / M | Question 2 (a sample import) before PL1.6 starts; a change of layout later is a new, announced version, not a quiet edit. |
| 5 | A PUT with missing fields wipes tax values while the module is off | M / M | Acceptance PL1.5 item 3; review checks the update handlers. **Closed for the module-on case too** by the PL1.8 API follow-up: an absent key is never a write, whatever the module state (`TaxFieldPresenceTests`). |
| 6 | TIN of a sole proprietor is personal data (PDPA) and leaks to logs, audit or export | L / H | Not logged; audit is already admin-only; export needs a dedicated permission and an audit event; security-engineer confirms. |
| 7 | `UserInfo` constructor change touches many callers and tests; the `DbSystemSettings` refactor (shared settings read) changes a hot path | M / M | Do the record change first in PL1.1; if the shared read grows beyond about 150 lines, fall back to `ModuleSwitches` reading its few rows itself (one small query, only for module requests) and say so in the PR. |
| 8 | Three serial migrations and two contract batches stall the web lane | H / M | Section 7; PL1.4 starts at CS6, not after PL1.6. |
| 9 | The web slices compete with the open lean-refresh slices (L02, Q1, R14+R15, A07, R26, R29+R30) for the same lanes | H / M | Question 1: the backend lane is free now; web pilot slices wait for a spare slot unless the owner reorders. |
| 10 | After deploy an Admin cannot flip the module for up to 5 minutes (old token) | H / L | Deploy note: sign out and in, or wait for the refresh. |
| 11 | With `accounting` ON, a web form that does not send the four fields clears them on every save (a PUT replaces all fields): the forms of PL1.5 and earlier never send them, and a browser that still holds an old cached bundle does the same after PL1.8 | H / M | Gate: **do not switch `accounting` ON in any environment until PL1.8 has shipped** (deploy note of PL1.5 and PL1.8). PL1.8 acceptance item 5 (round trip). After PL1.8, an old cached bundle is a residual risk: the service worker update prompt, and the accounting switch is changed only after a reload of all clients. **Closed on the API side by the PL1.8 API follow-up** (an absent key keeps the stored value, so an old bundle, a script or a stale form cannot clear a tax value): `accounting` may be switched ON in a shared environment once that change is deployed. The web refusal of PL1.8a (a screen whose module list is older than the switch) is now a second line of defence, not the only one; dropping it is a separate web decision. |

## 7. Effort, critical path and calendar

Effort: 11.0 agent-days of build and review, 14.5 with the 25% rework allowance (about 87 agent-hours at 6 hours a day). Backend lane PL1.1 to PL1.6 is serial (the migration baton): 2.5 + 2.0 + 2.5 + 2.0 = 9.0 days plus about 0.5 day per PR of merge wait (CI twice, review) = about 11 days. The web lane runs beside it (PL1.3 and PL1.4 beside PL1.5 and PL1.6), then PL1.7 and PL1.8 add about 2 days: **about 13 working days end to end (range 11 to 17)**.

Calendar for a part-time team of three (the work is done by agents; the three people decide, review and merge, so the bottleneck is their review time). `ROADMAP.md` section 5 gives the conversion: three working days a week instead of five multiplies the calendar by about 1.6. Full time that is about 13 working days; at three days a week about 21. Example, if PL1.1 starts Monday 26 October 2026: done about Wednesday 11 November at full time, about Monday 23 November at three days a week. These are not promises: the first real throughput check comes with the WD 10 re-estimate of the roadmap. M6 of the lean refresh (Wed 2 Dec) is not moved by this plan if the web slices wait for a spare slot (question 1).

## 8. Test plan and review gates

Tier rule (`PR-REVIEW.md` section 8, read as two tiers): **Full** = independent reviewer who did not write it, plus the specialist role for the topic, for anything touching authentication or permissions, money, a database migration or shape, posting, or a layout-wide web change; **Standard** = one independent reviewer with the lean check. Pure generated or test-only PRs are Standard.

| Sub-slice | Tier | Reviewers beyond the independent one | Why |
|---|---|---|---|
| PL1.0 | Standard | be-architect | Tests only; guards later posting work. |
| PL1.1 | Full | security-engineer, be-architect, be-system-designer | Authorization pipeline, a new dangerous permission, audit, lock. |
| PL1.2 | Full | security-engineer, be-architect | Permissions, role data loss, containment. |
| PL1.3, PL1.7 | Standard | fe-framework peer | Generated client, merged alone. |
| PL1.4 | Full | fe-architect, fe-designer, qa-edge-accessibility | Navigation and route guards are layout-wide. |
| PL1.5 | Full | be-dba, security-engineer | Migration on two tables; personal data. |
| PL1.6 | Full | be-dba (timing), data-flow-analyst, security-engineer | Money and cost visibility; a file that leaves the system. |
| PL1.8 | Standard | qa-novice-warehouse-staff on 360 px | Forms and one screen; cost scan keeps cost visibility honest. |

| Layer | What |
|---|---|
| Unit (Domain) | Catalog (unique codes, acyclic `Requires`), permission catalog (`accounting.export` requires `cost.view`, `masterdata.read` and `reports.read`), tax-field shape rules, role merge function, export row builder (sign, rounding, reversal pairing). |
| Architecture | `[RequiresModule]` on every module controller; Domain posting does not reference the switch; Admin holds every catalog code. |
| Integration (real SQL Server) | Gate cases (anonymous, signed in without permission, with permission, off, on); flip audit and lock race; role save with hidden grants; export scope, cap, reconcile; migrations Up/Down/Up with rows; `UserInfo.modules`; OpenAPI equality with all modules off. |
| Web (Jest) | Navigation matrix, `requireModule`, store, forms, export screen. |
| e2e (real API, `Ims_QA2`, never port 4200) | Modules flip; the export story of PL1.8; one scenario file per web sub-slice. |
| Security gate | Retest by the security-engineer after PL1.2 and PL1.6 merge: probe an off module as every role, role-save data loss, export scope, TIN handling. |
| Performance | DBA timing of the export at the 1M tier (PL1.6); no other hot path changes. |

Definition of done per sub-slice: `PROTOCOL.md` (warnings-as-errors build, tests of what changed, full suites once for migrations, permissions and money), the lean definition of done of `lean-scope.md` 3.5 for web, the review tier above, a HANDOFFS entry, and the TASKS.md line ticked only after CI is green on `main`. When PL1.6 merges, `backend.md` gets a short "Modules" section (ADR section 13).

## 9. What PL1 leaves for the next slices

| Slice | Needs from PL1 |
|---|---|
| PL2 purchasing | Foundation as built; adds `Requires` and `CanDisable` (open orders) to the catalog with its own entry; a PO number prefix in `DocumentSequence`. |
| PL3 sales and returns | PL1.0 merged (posting oracle); price lists later add selling price columns to the export layout as a new announced version. |
| PL4 stocktake | PL1.0 merged; the `Adjust` strategy and signed line direction touch the posting engine and `StockDirection`. |

## 10. The other three pilot slices (outline only; sized by lines at the ADR bands, refined by be-system-designer when each starts)

| Slice | Requirement | Content | Sub-slices (planned) | Lines | Days with rework |
|---|---|---|---|---|---|
| PL2 Purchase orders, receive against PO, reorder | PRD IN-01 ("create receipt ... from purchase order"), PRD Phase 2 purchase orders, AI-02 (reorder suggestion, in its simplest form from safety stock); ADR R2 | PO create / approve / close, receipt created from PO lines with partial receipts, reorder suggestion that turns low-stock rows into a PO draft. Decision to take at its start (not an owner question): approval reuses the two approval tiers (owner's rule) or the ADR's `po.approve` permission. | PL2.1 PO domain, API, permissions, `CanDisable`; PL2.2 receive against PO; PL2.3 web PO screens; PL2.4 reorder suggestions | 3,500 | 10.5 |
| PL3 Delivery / sales orders and returns | ADR R4, R9, R6 (owner ranked list); PRD OUT-01 (issue, purpose Sale), OUT-04 (delivery note print, P1). No PRD id exists for sales orders or returns: the business-analyst records ids with the owner. | Sales and delivery order posting an Issue with purpose Sale; returns linked to the original document; price lists as a second slice | PL3.1 order and delivery backend; PL3.2 web; PL3.3 returns; PL3.4 price lists (own PR) | 4,500 | 13.5 |
| PL4 Stocktake | PRD CNT-01 to CNT-04 (P1, Phase 2), BR-08; ADR R3 | Count document per warehouse, phone count entry, variance, approval, posting via the new `Adjust` strategy (signed quantity per line, its own `StockDirection` rule) | PL4.1 Adjust strategy and signed direction (oracle-guarded, DBA); PL4.2 count document and API; PL4.3 phone count screen; PL4.4 variance review, posting, e2e | 5,000 | 15 |

Pilot total about 54 agent-days with rework (PL1 14.5, PL2 10.5, PL3 13.5, PL4 15.0). Known conflict to settle at PL4 start: PRD CNT-03 wants counts kept offline, and the web rule N9 says commands are never queued offline; the owner decides which one yields.

## 11. Open questions for the OWNER (max 5; the default applies if no answer comes)

| # | Question | Recommended default | Needed by |
|---|---|---|---|
| 1 | Do the pilot slices go ahead of the open lean-refresh web slices (L02, Q1, R14+R15, A07, R26, R29+R30)? | Backend PL1.0 to PL1.2 start now (the backend lanes are free). Web PL1.4 and PL1.8 take the next spare web slot after Q1 and L02; the refresh milestones keep their order. | Before PL1.1 starts |
| 2 | Which accounting package will the pilot's accountant import the file into, and can we get one sample import file or its import template? | Assume SQL Account (or AutoCount) takes a plain Excel with the columns of section 4; the accountant tries one real file from the first build and we adjust the column set once, before PL1.6 merges. | Before PL1.6 starts |
| 3 | Is the export at cost acceptable for now? IMS has no selling price, so the export of an issue shows cost value, not the invoice value; selling price arrives with price lists in PL3. | Yes: the first export serves purchases and the stock journal (cost); invoices stay in the accounting package until PL3. | Before PL1.6 starts |
| 4 | Posted documents only, with reversals as negative rows, no "already exported" marker? | Yes. The accountant imports by period; the file name carries the range; a marker is built only if a period is imported twice. | Before PL1.6 starts |
| 5 | Who holds `accounting.export` by default? | Admin and Owner (both already see cost and value); anyone else through the role editor. The Supervisor and below do not. | Before PL1.2 starts |
