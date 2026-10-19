# ADR: Tracking dimensions and module switches

Status: **Proposed** (2026-10-19). Author: be-system-designer. Nothing here is built; the owner approves the order in section 12 before any slice starts (lean scope: a slice needs a named owner requirement).
Inputs: `backend.md`, `data-and-performance-rules.md` (sections 4, 5, 9, 11), `docs/dba/README.md` (1M tier, P95 < 1 s, migration timings), `docs/design/lean-scope.md`, `docs/pm/ROADMAP.md`, `adr-roles-permissions.md`, `adr-guid-keys.md`, and the code at `origin/main` 2148028: `StockBalance`, `StockLedgerEntry`, `StockDocumentLine`, `PostingEngine`, `IssuePostingStrategy`, `ReceiptPostingStrategy`, `BalanceLoader`, `SqlTransactionLocker`, `SystemSetting`, `UpdateSettingsHandler`, `PermissionCatalog`, `UserInfo`, `menu.const.ts`, `requirePermission`.
Scope: design only. The goal is a small core plus switch-on modules for Malaysian SMEs (trading, F&B, light manufacturing, retail) **without painting the stock ledger into a corner**. The ledger is the one part that is expensive to change later, so most of this note is about it.

## 0. Decision in short

1. **One optional tracking dimension, a lot.** A new `StockLot` (batch number with optional expiry, or a serial number) is referenced by a **nullable `LotId`** on `StockLedgerEntry` and in a child table of the document line. `StockBalance` (product x warehouse: quantity, moving average, marker) is **not changed**: it stays the sum over lots and stays the only thing every existing reader, report, low-stock rule and index looks at. A new `StockLotBalance (ProductId, WarehouseId, LotId)` holds the per-lot quantity.
2. **Per-product `TrackingMode` (None, Batch, Serial), default None.** Every row that exists today is None and stays valid. The mode cannot change once the product has stock movements, except through the conversion path of section 3.6.
3. **Valuation stays one moving average per product.** No cost per lot. Lots carry quantity and expiry, not money.
4. **Locks stay `stock:{warehouse}:{product}`.** A lot needs neither a finer nor a coarser lock (section 3.4).
5. **Issues allocate FEFO** (earliest expiry first, then oldest lot) at posting time under the lock, unless the person picked a lot. A lot never goes negative.
6. **Module switches** are a code catalog (`ModuleCatalog`, like `PermissionCatalog`) plus one `SystemSetting` row per module (`Module.<code>`). Installation = company: there is no tenant or company id anywhere in `src/` (checked by search), so the switch is installation-wide.
7. **A switch gates the surface, never ledger semantics.** Off means: endpoints answer 404, permissions of the module are unassignable, menu and routes are gone, data is kept. The posting engine reads product data, never the switch, so flipping a switch can not corrupt stock.
8. **Module order:** foundation (switches), accounting hand-off, stocktake, batch/lot, purchasing, sales and price lists, returns, assembly, serial. Pilot default: trading/wholesale (section 8, 12).
9. **Not built:** full accounting, POS, payroll, e-invoice submission, marketplaces (section 9).
10. **Proof:** the old posting behaviour is frozen as oracle fixtures before any change (section 10).

Sizes: S = up to about 600 changed lines, M = up to about 1,500, L = more than that and split into slices of at most 1,500 (the limit the team already uses).

## 1. What the code does today (facts)

| Fact | Where |
|---|---|
| `StockBalance` has a composite key `(ProductId, WarehouseId)` and holds `QtyOnHand`, `QtyInTransit`, `AvgCost` (`decimal(22,10)`), `LastReceiptLineId`, `ROW_VERSION`. | `StockBalance.cs`, `AppDbContext` |
| `StockLedgerEntry` is append-only (DbContext refusal plus triggers): `ProductId`, `WarehouseId`, `QtyDelta`, `BalanceAfter` (stored, set under the lock), `UnitCost`, `SourceDocId`, `Reason`. Indexes: `(ProductId, WarehouseId, OccurredAt)`, `SourceDocId`, `OccurredAt`. | `StockLedgerEntry.cs`, `AppDbContext` |
| Only `Receipt` and `Issue` have posting strategies. `Transfer`, `Adjust`, `TransferOut/In`, `Adjustment`, `Opening` exist as enum values, with no strategy. A document has one warehouse. | `DocumentType.cs`, `LedgerReason.cs`, `DependencyInjection` |
| Posting: `PostDocumentHandler` runs in `uow.ExecuteAsync`; `BalanceLoader` takes the application lock `stock:{wh}:{product}` for each product in ascending id, then reads the balances. `PostingEngine` applies lines in `ProductId, Id` order; the strategy gets `(line, balance, ctx)` and is pure. | `PostDocumentHandler`, `BalanceLoader`, `SqlTransactionLocker`, `PostingEngine` |
| Reversal is a method of the strategy; receipts are undone with `Unreceive` (average restored from the line snapshot while the line is still the marker). `FindReversalDeficits` decides by product quantity. | `StockBalance`, `PostingEngine` |
| Quantities are converted to the base unit on the line (`BaseQty`); the ledger only knows base units. | `StockDocumentLine.Create`, `Product.ToBaseQuantity` |
| "No change once there is history" already exists for the base unit (BR-04): `Product.ChangeBaseUnit(uom, hasStockMovements)` with `ProductHasMovementsSpec` (any ledger row). | `Product.cs`, `ProductWriter` |
| Settings are a key/value table (`SystemSetting`, key up to 100 characters, value up to 500) read once per request by `DbSystemSettings`. `PUT /api/settings` writes exactly three keys from a fixed record and writes one explicit audit event. `settings.manage` guards it. | `SystemSetting.cs`, `UpdateSettingsHandler`, `SettingsController` |
| Permissions are a code catalog (21 codes) with group, dangerous flag, `Requires`; roles are rows; the access token (5 minutes) carries one `perm` claim per code, `allwh`, `wh`, `acv`. `UserInfo` carries `Permissions`. | `PermissionCatalog`, `adr-roles-permissions.md` section 3 |
| Web: `MENU_ITEMS` with `permissions[]` (any-of), `requirePermission(...)` route guard, `PermissionService`. | `menu.const.ts`, `identity.guards.ts` |
| Performance baseline: 1.0 M ledger rows, 175 k balances, 50 k products, 5 warehouses, every interactive endpoint under 1 s P95; the ledger row is about 110 bytes; a migration that rewrites the ledger costs about a minute per million rows. | `docs/dba/README.md` |

Unverified (assumptions to confirm when the first slice starts): that `SystemSetting` rows are not row-audited (the settings handler writes an explicit event, so the module switch will too); that the movements report reads document lines, not ledger rows, so a line that splits into several ledger rows does not change its row count; whether the production SQL Server edition does a `NOT NULL DEFAULT` column add as a metadata-only change (it does not matter for `Product`, 50 k rows, seconds).

## 2. Requirements this serves

No PRD id exists for these yet (the PRD is not in the repository). The "owner requirement" of each module below is the ranked market list the owner has seen, quoted as R1 to R9. Each module slice still needs the owner's yes and an id recorded in `TASKS.md` before it is built.

| Id | Need (market ranking) | Module (section 8) |
|---|---|---|
| R1 | Accounting hand-off; tax-ready fields (classification/HS code, SST code, partner TIN) | `accounting` |
| R2 | Purchase orders, receive against PO, reorder | `purchasing` |
| R3 | Stocktake / cycle count with variance | `stocktake` |
| R4 | Sales and delivery orders | `sales` |
| R5 | Batch/lot with expiry (FEFO) | `batch` |
| R6 | Returns | `returns` |
| R7 | Serial numbers | `serial` |
| R8 | Kits and simple assembly | `assembly` |
| R9 | Price lists | `sales` (second slice) |

## 3. The tracking dimension

### 3.1 Options

| Option | How | Verdict |
|---|---|---|
| A. Nullable `LotId` on ledger and balance, balance key becomes `(Product, Warehouse, Lot)` | One balance row per lot; null lot for untracked | **Rejected.** Changes the key of the hottest table and of every reader of it (about 20 files in `Ims.Application`: stock list, low stock, availability, reports, exports); the stock list would have to `SUM` over lots for every page, which is the O(n) the rules forbid and puts the 1M-tier work (low stock 1.7 s CPU, stock list scan) at risk. The moving average and `LastReceiptLineId` are per product, not per lot, so they would have to be split out again. |
| **B. `StockBalance` unchanged; lot detail beside it** | `StockLot` master, `StockLotBalance` per lot, nullable `LotId` on the ledger, child table on the line | **Chosen.** Additive, every existing reader keeps reading one row per product and warehouse, costing code is untouched. |
| C. Separate tracking ledger | A second append-only table for lot movements | **Rejected.** Two ledgers can disagree; "balance rebuildable from one ledger" would become two proofs and the reversal would write to both. |
| D. One row per unit for everything | Each unit is a row | **Rejected.** Millions of rows for fungible goods. Fine only for serials, which B already covers. |
| E. Lot in a free-text field on the line | No master data | **Rejected.** No expiry, no per-lot balance, no FEFO, no recall trace. |

### 3.2 Entities and columns

| Entity / change | Columns and keys | Notes |
|---|---|---|
| `Product.TrackingMode` | `int NOT NULL DEFAULT 0` (0 None, 1 Batch, 2 Serial) | 50 k rows: seconds. Changes only through `Product.ChangeTracking(mode, hasStockMovements)` (same rule shape as `ChangeBaseUnit`). |
| `StockLot : PublicEntity` | `Id` int identity (clustered), `PublicId`, `ProductId`, `Code` (lot or serial number, up to 64), `ExpiryDate` date NULL, audit columns | `UX_StockLot_Product_Code (ProductId, Code) WHERE REC_DELETED = 0` unique. A lot is identified by product and code; its expiry is **immutable after the first posting** (a receipt that names an existing code with another expiry is refused with 409). One public id because the API addresses lots (stock by lot, pick on an issue line). |
| `StockLotBalance` | PK `(ProductId, WarehouseId, LotId)` clustered; `QtyOnHand decimal(18,4)`; `ROW_VERSION`; audit columns | Same shape as `StockBalance`. Rows are never deleted (a zero row is history and rebuildable). |
| `StockLedgerEntry.LotId` | `int NULL`, FK to `StockLot`, **no default, nullable** | Metadata-only add: old rows are not touched and read as NULL (null = "before tracking" or untracked). New rows pay 4 bytes (about 4% of the 110-byte row) once the table is rebuilt. No index in the migration (section 3.9). |
| `StockDocumentLineLot` | `Id`, `LineId`, `LotId` NULL, `LotCode` NULL, `ExpiryDate` NULL, `BaseQty`; unique `(LineId, LotId)` filtered | Receipt lines: the draft holds `LotCode`/`ExpiryDate`, `LotId` is filled at posting (the lot is created or found under the lock; an abandoned draft creates nothing). Issue lines: optional picks in the draft; at posting the allocation rows are written. One line can have several rows. Untracked lines have none. |

The ledger gets one row per (line, lot) piece. Each piece carries the product-level `BalanceAfter` after that piece, `UnitCost` (receipt: the line cost; issue: the product average), and its `LotId`. A line of an untracked product writes exactly the row it writes today.

### 3.3 Invariants (proved by tests and by `docs/dba/08-reconcile-drill.sql`)

1. `StockBalance.QtyOnHand` = `SUM(ledger.QtyDelta)` by (product, warehouse), unchanged.
2. For a tracked product: `StockLotBalance.QtyOnHand` = `SUM(ledger.QtyDelta)` by (product, warehouse, lot), and `SUM(StockLotBalance)` = `StockBalance.QtyOnHand`. Ledger rows with `LotId IS NULL` of a tracked product are legacy rows and net to zero after the conversion (3.6).
3. A `StockLotBalance` is never negative. A serial lot balance is 0 or 1.
4. `StockBalance.AvgCost`, the marker and every snapshot column behave as today: the product-level numbers are updated once per line by the existing `Receive`, `Issue`, `Unreceive` code, never per piece.
5. The ledger stays append-only; the trigger is not touched.

Both balances are rebuildable from the ledger; the drill gets a fifth check (per-lot sums).

### 3.4 Posting, reversal, transfer, negative stock

The strategy interface does not change. The engine asks one question per line: is the product's mode None? If so it runs today's code path unchanged (this is what the oracle fixtures hold). Otherwise it runs `TrackedPostingStep` (a Domain service, pure like the strategies): it applies the line to the product-level `StockBalance` exactly once, then applies the lot pieces to `StockLotBalance` rows and emits one ledger entry per piece.

| Case | Behaviour |
|---|---|
| Receipt, Batch | The line must name a lot code (expiry required if the product says it expires: `Product.RequiresExpiry`, set with the mode; Malaysian food and pharma need it, other goods may not). Lot found or created under the product lock; qty added to the lot and to the product. |
| Receipt, Serial | Qty must equal the number of serials listed and be a whole number of base units; one lot per serial, balance 1. A serial already in stock (balance 1) is refused 409. A serial seen before and issued (balance 0) may come back only through a return (module `returns`), not a plain receipt. |
| Issue, Batch | Picks given: each must have enough, else 409. No picks: FEFO allocation over lots of this (product, warehouse) with balance > 0 and not expired, ordered by `ExpiryDate` (NULL last), then `LotId`. Not enough in non-expired lots: 409 `InsufficientStock` naming the shortfall. |
| Issue, Serial | The person lists the serials; no auto allocation. |
| Reversal of an issue | Each piece goes back into its own lot (expired or not), found from `StockDocumentLineLot`. |
| Reversal of a receipt | Taken out of its lot. If the lot holds less than the receipt (part was issued) the reversal is blocked by the same rule as today, now per lot: `FindReversalDeficits` returns deficits per (product, lot) for tracked lines. Lines of one product are still reversed last-posted first. |
| Negative stock (BR-03) | **Does not apply to a tracked product.** A lot cannot go negative, and a product quantity above the sum of its lots would break invariant 2. `stock.negative.override` has no effect on a tracked product; the shortfall is a 409 that says to receive the stock first. Untracked products behave as today. This is the one behaviour the owner may dislike; see question Q5. |
| Transfer (not built) | When the transfer strategy is built it moves pieces lot by lot (out of the source lot, into the same lot in the destination). It needs one lock order across two warehouses: `(warehouseId, productId)` ascending, which the locker does not do today (it orders products inside one warehouse). That is a precondition of the transfer slice, not of this ADR. |
| Expired lot | Never auto-allocated. An explicit pick of an expired lot is refused unless the issue purpose is `Scrap` (already in `IssuePurpose`): expired stock leaves through a scrap issue, which also keeps the value write-off visible. |
| Unit conversion | Unchanged. The lot quantity is the base quantity (`BaseQty`); a serial line needs a whole base quantity. |

### 3.5 Locks and deadlock freedom

- **Keep `stock:{warehouse}:{product}`.** A finer lock (per lot) would let two issues of one product run in parallel, but both update the same `StockBalance` row (quantity, average, marker), which is the real serialisation point; the finer lock would only add a second lock level and a possible cycle. A coarser lock (per warehouse) would put every product behind one queue; the burst test in `docs/dba` (150 postings on 5 hot products, write P95 698 ms) shows the per-product grain is what keeps this flat.
- Inside the lock, lot rows are read in one statement (`WHERE ProductId IN (...) AND WarehouseId = @w`, PK prefix seek) and written in `(ProductId, LotId)` ascending order. Every writer holds the product lock first, so the order inside is deterministic and no cycle is possible.
- **Lot creation race.** Two receipts of the same new lot code in two different warehouses hold different locks and both insert `StockLot (P, 'L1')`. The unique index rejects the second; `UnitOfWork.ExecuteAsync` already treats a unique-key race as retryable, re-runs the use case, and the retry finds the lot. Test in section 10.
- Lot master rows (expiry, code) are written only by receipts, under the product lock of the receiving warehouse.

### 3.6 Changing the mode of an existing product

| Situation | Rule |
|---|---|
| No ledger row for the product | Free change (None, Batch, Serial). |
| Has movements, on hand is 0 in every warehouse, no open document line (draft, submitted, approved) for it | Allowed; old rows stay `LotId NULL`; the audit event records the change. |
| Has stock | **Refused (409)** with the message "Move the stock into lots first". The defined path is the **conversion posting** (slice in module `batch`, M): for each warehouse that holds the product it writes two `Adjustment` ledger rows, one out of the null lot and one into a lot `OPENING` (or a code the person types, with an expiry), both at the current average, net zero for the product (`StockBalance` is not changed), and creates the `StockLotBalance`. Needs a new permission `tracking.convert`, a reason, an audit event; it runs under the product lock. |
| Batch to Serial or back, with stock | Refused. Convert to None only through zero stock. |

Serials and batches do not coexist on one product (Q2).

### 3.7 Valuation: one average per product (recommended)

| Option | Verdict |
|---|---|
| Moving average per product (today) | **Chosen.** Zero change to `StockBalance.Receive/Issue/Unreceive`, whose reversal arithmetic is the most delicate code in the posting path (`backend.md`, "Costing and reversal"). The money shown stays one number per product. |
| Average per lot | Rejected for now. Every cost snapshot column, the marker and `Unreceive` would become per lot; reports of value would sum over lots. It matters for goods whose lots differ greatly in price (not typical for SMEs). |
| FIFO layers / specific identification | Rejected for now. Same reasons plus a different reversal algebra. Revisit when an accountant of a pilot customer needs it (section 9). |

Assumption outside the code: that a weighted average is acceptable to the customers' accountants. It is what the system already does (BR-05); a different method would be a separate decision.

### 3.8 Migration on a database that has data

One migration per step, created only after the previous one is on `main` (ROADMAP 3.2 baton). Each `Down` is real.

| Step | Up | Cost at 1M ledger rows | Down |
|---|---|---|---|
| `AddProductTrackingMode` | `Product.TrackingMode int NOT NULL DEFAULT 0`, `RequiresExpiry bit NOT NULL DEFAULT 0` | seconds (50 k rows) | drop the columns |
| `AddStockLots` | create `StockLot`, `StockLotBalance`, `StockDocumentLineLot`; add nullable `StockLedgerEntry.LotId` with FK | tables empty; the ledger column add is metadata-only (no rewrite, unlike `AddLedgerBalanceAfter`, which had to fill a NOT NULL column) | **throws if any `StockLot` row exists** (the lot dimension would be lost); otherwise drops the column and tables |
| Backfill | none: every existing row is None/NULL, which is valid | 0 | |

- Fill factors: `FILLFACTOR` 100 on append-only tables (the ledger), 90 on random-key indexes (`UX_StockLot_PublicId`, as in `PublicIdColumn`), `StockLotBalance` like `StockBalance`. After the migration run `docs/dba/07-maintenance.sql` (the clustered index of the ledger grows 4 bytes per row at the next rebuild; no maintenance window beyond the weekly one, since nothing rewrites the table).
- No downtime beyond the normal deploy. The Up test (`AverageCostMigrationTests` pattern) runs Up, Down, Up on a database that holds rows, and Down with a lot present must fail.
- Rolling deploy hazard: an old API instance does not know `LotId`; it can post only untracked products (a tracked product cannot exist before the module is switched on after the deploy), so mixed versions are safe if the module stays off until every instance is new.

### 3.9 Performance at the 1M-document tier

| Item | Estimate and rule |
|---|---|
| Ledger growth | +4 bytes a row; one extra row per additional lot piece of a tracked line only. Untracked products: no extra rows, no extra statements. |
| `StockLotBalance` rows | tracked products x warehouses x live lots. Example: 5,000 tracked products (10% of 50 k), 2 warehouses, 40 live lots = 400 k rows, about 70 bytes a row = about 30 MB. Smaller than `StockDocument`'s one index. Zero-balance rows stay. |
| Posting, tracked line | +2 statements (read the product mode and lot balances in two statements for the whole document, not per line; write the lot rows) plus the extra ledger rows. Untracked: the mode read is one `SELECT Id, TrackingMode ... WHERE Id IN (...)` per document (PK seeks). Budget: write P95 within 5% of today (k6 `write-mix`). |
| Stock list, low stock, summary | **Unchanged**, they read `StockBalance`. This is the main reason for option B. |
| New indexes | None in the migration. An index needs a named consumer (`data-and-performance-rules.md` 9): `IX_StockLot_Expiry (ExpiryDate) INCLUDE (ProductId) WHERE REC_DELETED = 0 AND ExpiryDate IS NOT NULL` arrives with the expiry report; `IX_StockLedgerEntry_LotId (LotId) WHERE LotId IS NOT NULL` (filtered, so untracked rows cost nothing) arrives with the lot trace screen. Each is measured on the 1M seed first. |
| FEFO pick | PK prefix seek on `(ProductId, WarehouseId)` plus a join to `StockLot` for the expiry: tens of rows under the lock. No denormalised expiry. |
| To re-measure | k6 `write-mix` and `write-hot` with 20% tracked products; `07-maintenance.sql` plan; `08-reconcile-drill.sql` with the per-lot check; stock list, low stock and movements at the 1M seed (expect equal reads); `docs/dba/01-seed-perf.sql` gets a `TrackedShare` parameter. |

Import: the opening-stock import needs a lot column for tracked products (one more template column, only shown when the module is on); products import can set the mode only for products with no movements.

## 4. Module switches

### 4.1 Where they live

| Part | Decision |
|---|---|
| Catalog | `ModuleCatalog` in `Ims.Domain` (static, like `PermissionCatalog`): `ModuleDefinition(Code, Name, Description) { Requires = [...] }`. Codes are stable once released. Architecture tests: unique codes, `Requires` acyclic. |
| State | One `SystemSetting` row per module: key `Module.<code>` (well under the 100-character key limit), value `true`/`false`. A missing row is off. No new table, no company column (installation = company; confirmed: no tenant or company concept exists in `src/`). |
| Not in `SettingsDto` | `PUT /api/settings` writes a fixed three-field record and replaces all of it; modules get their own endpoints so an old client cannot flip one by omission. |
| Read | `IModuleSwitches` (scoped, memoised per request like `DbSystemSettings`, one 10-row read; no cross-request cache: one API instance, lean scope 1.4). Add a cache only if a measurement shows the read matters. |
| Defaults | An upgraded or new installation has every module off: its behaviour equals today's. A new install may switch modules on in the first-run screen (Admin). |

### 4.2 API

| Endpoint | Policy | Notes |
|---|---|---|
| `GET /api/modules` | any signed-in user | `[{ code, name, enabled, requires }]`. The web reads it at start. |
| `PUT /api/modules/{code}` | `modules.manage` (new, core, dangerous, Admin only by default) | Body `{ enabled, reason }` (reason at least 5 characters, like cancellation). Enabling needs every `Requires` module on (409 otherwise). Disabling is refused (409 with a count) while a dependent module is on or the module's own `CanDisable` check fails (section 4.4). Writes `module.enabled` / `module.disabled` with who, reason, before and after. |
| `UserInfo.modules` | | Enabled codes, built at sign-in, refresh and `/me`. Not claims. |

### 4.3 What "off" means

| Surface | Off |
|---|---|
| API of the module | **404** (the same ProblemDetails as an unknown route). Not 403: 403 says "it exists, you lack the right" and lets a caller probe an installation; 404 matches the ids rule (out of scope is 404, never 403). Done by `[RequiresModule("batch")]` on the controller, enforced by an endpoint filter that asks `IModuleSwitches`. An architecture test requires the attribute on every controller in a module namespace, like `EndpointCoverageTests` does for policies. |
| Optional fields on core endpoints | A document line with `lotCode`, `picks` or `serials` while `batch`/`serial` is off: 400 "not available". Responses omit the lot fields (like cost fields are withheld). |
| Permissions | `PermissionDefinition.Module` (null = core). `GET /api/permissions` lists only codes of enabled modules; `PUT /api/roles/{id}/permissions` rejects a code of a disabled module (400); existing grants stay as rows and come back on re-enable. The token still carries the codes the role holds (no change to the claim shape, no `acv` bump): the endpoint filter, not the claim, decides, so a flip takes effect on the next request. |
| Web | `MenuItem.module?`; `NavigationService` shows an item if the module is on AND any listed permission is held; `requireModule('batch')` beside `requirePermission`, sending to the not-found page, not the 403 page. Screens that embed a module field (the lot input on a line) are removed by the same signal. |
| Reports and exports | Lot columns carry `Module` next to `VisibleWith` on `ReportColumn`; a column of a disabled module is left out, header and cells, by `IReportWriter`. Module reports are module endpoints (404). |
| Data | **Kept, hidden, and back on re-enable.** Core tables keep their lot columns; core responses omit them while off. Nothing is deleted or archived. |
| Ledger and posting | **Unchanged by the switch.** The engine reads product data. Because a posted document of a tracked product must still reverse correctly, tracking never depends on the switch. |

### 4.4 Disabling safely

Each module declares `CanDisable` (a query, run in the flip handler):

| Module | Refuse while |
|---|---|
| `batch`, `serial` | any product of that mode has stock in a warehouse or a line on a draft/submitted/approved document |
| `purchasing`, `sales` | any open order |
| `stocktake` | any open count |
| `assembly` | any open work order |
| `accounting`, `returns` | never refused |

A tracked product whose module is off cannot be used on a new document line (the same refusal as an inactive product, `Product.EnsureUsableOnDocuments`), so no untracked quantity can enter a product whose lots would then be out of step. The flip handler runs under the existing application-lock pattern with a new resource `modules` (never held with a stock lock), so two flips do not interleave; a posting that races the check cannot corrupt anything because posting ignores the switch (worst case: a module is off while it holds stock; it is switched on again).

### 4.5 Freshness with the 5-minute token

- **Server side is immediate**: the filter reads the table (one small read) per module request.
- **Web side is as fresh as the next refresh**: `UserInfo.modules` arrives with sign-in, refresh (every 5 minutes) and `/me`; the admin's own session reloads `GET /api/modules` right after a flip. Another user sees a menu change within about 5.5 minutes, the same bound as a role change (ADR roles 3.3). A user who uses a stale screen gets a 404 with the message "This feature was turned off" and the web refetches the module list.
- Not in the token: switches are installation-wide, not per user, and putting them in claims would need an `acv` bump and repeat what the server already knows.
- No public (anonymous) config endpoint is needed: the login page has no module content.

### 4.6 Who may flip

`modules.manage`: core, dangerous, held by Admin by default, added by a migration like every new permission (the catalog test requires Admin to hold every code). It is in the role editor under Administration. A module's own permissions are listed in section 8.

## 5. Alternatives rejected

| Alternative | Why not |
|---|---|
| Separate ledger or schema per module | Two sources of truth for stock; reversal and valuation across ledgers is the hard part. |
| EAV / custom fields for lot, serial, tax codes | No typed constraints, no indexes, no FEFO; the tax and lot fields are few and known. |
| Fork or branch per customer | Fixes and migrations multiply; this is exactly what a module catalog avoids. |
| Feature flags per user or per role | Roles already do that; a switch is for what the installation has, not who may use it. |
| A module marketplace or runtime plug-in loading | Code is shipped as one build; a switch hides, it does not load. |
| Tenant id on every table | One installation per customer is the stated model; multi-tenancy is a separate project with a separate trigger (section 9). |
| Lot id in the balance key (3.1 A) | See 3.1. |
| Cost per lot now | See 3.7. |
| Making `LotId` NOT NULL with a placeholder "no lot" row | A backfill of the whole ledger (about a minute per million rows, schema lock) for no benefit; NULL already means "none". |
| Sparse columns for `LotId` | EF Core has no mapping for them and the saving is 4 bytes a row. |

## 6. Interaction with what exists

| Area | Effect |
|---|---|
| Roles ADR | Catalog gets `Module`; roles API filters by enabled modules; no change to the token shape. `NoRoleDecisionTests` unaffected. |
| Cost visibility | Lots carry no money. `[VisibleWith]` rules are unchanged. |
| Public ids | `StockLot` is a `PublicEntity`; the API addresses it by public id; the ledger row exposes `lotId` as a GUID joined from the row, never a per-row lookup (the `DocumentFlipTests` statement-count technique). `OpenAPI` id test: the new `lotId` is a UUID. |
| Document numbering | New order types add a prefix and use `DocumentSequence` with the same lock; gap-free behaviour is by the same increment inside the transaction. |
| Approvals | Unchanged. A tracked line's shortfall is checked at approval and posting. |
| Corrections | Recall and cancel on an Approved document do not touch the ledger; only posting and reversal write lots. |

## 7. Failure scenarios and the test that catches each

| # | Scenario | Test |
|---|---|---|
| F1 | Two postings issue the same product from different lots at once and the product quantity or a lot balance goes wrong | 25 rounds of parallel issues, with picks and with FEFO; assert invariants 1 to 3 (the same style as `AdministrabilityTests`). With the lock commented out the test must fail. |
| F2 | Two receipts of one new lot code in two warehouses race on `UX_StockLot_Product_Code` | Parallel receipts: both succeed, one lot, balances correct. |
| F3 | A receipt names an existing lot with another expiry | 409, nothing posted, the stock unchanged. |
| F4 | Reversal of a receipt after part of the lot was issued | Blocked with a per-lot deficit; stock and ledger unchanged. |
| F5 | Reversal of a multi-lot issue puts a piece in the wrong lot | Property test: post a random issue then reverse it; every `StockLotBalance` and the average equal the start. |
| F6 | A retry after a deadlock victim repeats lot creation or writes the allocation twice | Fault-injection: force 1205 once inside `ExecuteAsync`; one lot, one set of ledger rows. |
| F7 | Switching a module off while a tracked product holds stock | 409 with a count; stock untouched. |
| F8 | The switch flips between a draft and its posting | Post a tracked document after the flip: posting works (the engine ignores the switch); the line editor is 404. |
| F9 | Old code writes a ledger row without a lot for a tracked product (mixed-version deploy) | Invariant 2 check in the reconcile drill; the module stays off until all instances are new (deploy note). |
| F10 | Migration `Down` silently drops lot data | Down with one lot present throws; Up/Down/Up on a database with rows. |
| F11 | A tracked product's quantity exceeds the sum of its lots after a manual SQL edit | `08-reconcile-drill.sql` check 5 reports it; rebuild script replays the ledger by `(product, warehouse, lot)` in `Id` order. |
| F12 | A permission of a disabled module is still assignable or still works | Generated tests: for each module code, every module permission is rejected by the role API while off and every module endpoint answers 404 while off, 403/200 as the role says while on. |

## 8. Modules, order, first hits

Dependencies on the tracking dimension are marked. "First hit" is the module that makes a segment want to buy.

| # | Module code | What | Requirement | Size | Needs tracking? | New permissions (module-owned) |
|---|---|---|---|---|---|---|
| 0 | (core) `modules` | `ModuleCatalog`, `IModuleSwitches`, `GET/PUT /api/modules`, `[RequiresModule]`, `Module` on permissions and report columns, web `module` gating, `modules.manage` | enables all below | M | no | `modules.manage` (core) |
| 1 | `accounting` | Export of posted documents and stock movements in one fixed layout (Excel/CSV, one row per line: date, document no., type, partner, SKU, quantity, unit cost, amount, tax code) for the accountant to import into SQL Account, AutoCount or Xero; **a file, not an integration**. Tax-ready optional fields: `Product.HsCode` (classification), `Product.SstCode`, `Partner.Tin`, `Partner.SstRegNo`, validated for shape only | R1 | S to M | no | `accounting.export` |
| 2 | `stocktake` | A count document per warehouse (all or by category), counted quantity entry (phone), variance, approval, posting via the new **Adjust** strategy (`LedgerReason.Adjustment`). With `batch` on, count per lot | R3 | M | optional | `stocktake.count`, `stocktake.post` |
| 3 | `batch` | Section 3: lots, expiry, FEFO, conversion posting, expiring report, lot trace | R5 | L (3 slices: domain and posting; web and import; reports and conversion) | **is it** | `tracking.convert` plus lot permissions only if the owner wants them (default: none, uses `document.*`) |
| 4 | `purchasing` | Purchase order, receive against PO (a receipt linked to PO lines, partial receipts), reorder suggestions from safety stock | R2 | L | optional (lot on the receive line) | `po.create`, `po.approve` |
| 5 | `sales` | Sales and delivery orders, delivery posts an Issue with purpose Sale; price lists in a second slice | R4, R9 | L | optional (FEFO on delivery) | `so.create`, `pricelist.manage` |
| 6 | `returns` | Customer and supplier returns linked to the original document, same lot back | R6 | M | needs `batch` to return into the right lot | `returns.create` |
| 7 | `assembly` | Kits and simple assembly: a bill of materials, a work order that consumes components (Issue-like, FEFO) and produces the item (Receipt-like) at the sum of the consumed average costs | R8 | L | optional (lot trace of consumed lots) | `bom.manage`, `workorder.post` |
| 8 | `serial` | Section 3 with `TrackingMode = Serial`: same tables, one lot per serial | R7 | M | needs `batch` tables | `serial.trace` only if wanted |

Segment paths:

| Segment | First hit, then | Why |
|---|---|---|
| Trading / wholesale | `accounting`, `purchasing`, `sales` (+ price lists), `stocktake`, `returns` | Four of five need no ledger change; the accountant hand-off and PO/receive are the daily pain. |
| F&B / central kitchen | `batch` (expiry, FEFO), `stocktake` (per lot), `assembly` (recipes), `purchasing` | Expiry and recall are the buying reason; recipes consume lots. |
| Light manufacturing | `assembly`, `purchasing`, `stocktake`, `batch` (+ `serial` for finished goods) | Components in, finished goods out, then trace. |
| Retail | `stocktake`, `purchasing`, `accounting` | POS is not built (section 9); the shop's POS sends sales to the accountant. |

**Why this order.** Slice 0 is a prerequisite for every other module. `accounting` and `stocktake` change no ledger column and build the **Adjust strategy** that the lot conversion and `batch` reuse. `batch` is the only slice that touches the ledger and posting, so it comes when the oracle is in place and the Adjust strategy exists, before the larger document modules (`purchasing`, `sales`) that would otherwise be built twice, once without lots and once with.

## 9. What we will not build, and when to revisit

| Not built | Instead | Trigger to revisit |
|---|---|---|
| Full accounting (GL, AR/AP, tax returns) | `accounting` export file | Three paying customers ask for the same ledger posting API, or an accounting vendor offers an import API worth a connector |
| POS | Import daily sales as an issue (a file) | A retail customer cannot run without it and a partner POS is not an option |
| Payroll | Not in scope | None planned |
| E-invoice submission (MyInvois) | Tax-ready fields only (TIN, classification, SST); issue the e-invoice from the accounting package | The accounting packages of two customers do not submit and a pilot is blocked on it |
| Marketplace sync (Shopee, Lazada) | Export/import files | Five customers run marketplace listings from IMS stock |
| Cost per lot, FIFO layers | Moving average per product | An accountant requires it for a customer |
| Multi-tenant | One installation per customer | The hosted offering has more than about 20 customers on one database |

## 10. Test strategy

1. **Oracle fixtures first** (before slice `batch` changes anything), the technique used for roles (`tests/Ims.Domain.Tests/Identity/Oracle`). New folder `tests/Ims.Domain.Tests/Stock/Oracle`: a generator run at the commit before the change writes compact CSVs of: scripted sequences of receipts, issues, reversals, negative-stock overrides, unit conversions and multi-line same-product documents, with for each step the resulting ledger rows (`QtyDelta`, `BalanceAfter`, `UnitCost`, `Reason`), the final `StockBalance` (`QtyOnHand`, `AvgCost`, `LastReceiptLineId`), the line snapshots (`AvgCostBefore/After`, `PreviousLineId`) and the exceptions raised. New code must reproduce every value for None-tracked products. Plus a seeded random-sequence test (the same sequences on the frozen reference implementation kept in the test project and on the engine).
2. **Statement count**: a test that posts an untracked document through the real host and asserts the same number of SQL statements as at the freeze, plus the one mode read.
3. **Property tests** for invariants 1 to 3 after random sequences, tracked and untracked mixed, with concurrency (F1, F2).
4. **Generated module tests** (as `EndpointCoverageTests`): each module endpoint answers 404 while off; each module permission is unassignable while off; each module's report column disappears; every controller of a module namespace carries `[RequiresModule]`. A test with every module off asserts the existing API contract is unchanged (the committed OpenAPI minus module paths equals today's).
5. **Migration tests**: Up, Down, Up with data; Down refuses when lots exist.
6. **Web**: unit tests for `NavigationService` (module x permission matrix), `requireModule`, and one real-API e2e (an Admin switches `batch` on, a clerk sees the lot field, off again with no stock, the field and menu entry are gone).
7. **Performance**: the DBA measurements of section 3.9 before the slice merges.

## 11. Risk register (top 5)

| # | Risk | L / I | Containment |
|---|---|---|---|
| 1 | The lot change alters untracked behaviour or the cost arithmetic | M / H | Option B leaves `StockBalance` code untouched; the oracle fixtures and statement-count test; the slice cannot merge red. |
| 2 | Per-lot negative and the missing negative override annoy users (late-entered receipts) | M / M | Q5; the message says what to do; revisit with a "pending lot" only if a customer asks. |
| 3 | Modules multiply states to test (2^n combinations) | M / M | Switches gate the surface only; the ledger never reads a switch; tests are generated from the catalog; a dependency rule limits combinations. |
| 4 | Scope creep: each module grows toward accounting or ERP | H / M | Section 9; every module slice names its requirement id and size before it starts (lean scope); modules ship in slices of at most 1,500 lines. |
| 5 | Mixed-version deploy writes a tracked ledger row without a lot | L / H | Module off until all instances run the new build; invariant 2 in the reconcile drill; deploy note in the runbook. |

## 12. Questions for the owner (each has a default that applies if no answer comes)

| # | Question | Recommended default |
|---|---|---|
| Q1 | Which pilot segment first? | Trading / wholesale: its first modules need no ledger change, so the lot design gets measured while customers already use the product. |
| Q2 | Do serials and batches need to coexist on one product? | No: one mode per product. A serial inside a batch is rare for SMEs. |
| Q3 | Is a weighted average cost per product acceptable for tracked goods (no cost per lot)? | Yes; confirm with one accountant. |
| Q4 | May expired stock leave only by a Scrap issue? | Yes. |
| Q5 | May a tracked product ever go negative? | No (the override does not apply to tracked products); revisit on the first complaint. |
| Q6 | Who may switch modules? | Admin only (`modules.manage`), with a reason, audited. |
| Q7 | Is the accounting hand-off a file in one fixed layout enough, or is a target package (SQL Account, AutoCount, Xero) named? | One fixed layout first; adapt it to the first customer's package. |
| Q8 | Do lots need their own permissions? | No; use the document permissions. |

## 13. Rollout

| Step | Content | Size | Gate |
|---|---|---|---|
| 0 | Oracle fixtures and statement-count test on current `main` (tests only) | S | Merged before anything else |
| 1 | Module foundation (section 8, row 0) | M | Review gate; no migration other than `modules.manage` seeding |
| 2 | `accounting` | S to M | Owner yes on R1 |
| 3 | `stocktake` (Adjust strategy) | M | Owner yes on R3 |
| 4 | `batch` slice 1: mode, lots, posting, reversal, locks, migration | L | DBA measurement, oracle green, concurrency tests |
| 5 | `batch` slice 2: web and import | M | e2e |
| 6 | `batch` slice 3: reports, conversion posting, indexes | M | 1M-tier numbers |
| 7+ | `purchasing`, `sales`, `returns`, `assembly`, `serial` in the order above | L, L, M, L, M | Each: owner yes, id in `TASKS.md`, review gate |

Docs to update when step 1 and step 4 merge: `backend.md` (a "Modules" and a "Tracking" section, short), `data-and-performance-rules.md` section 9 (the new indexes), `docs/dba/README.md` (the measurements), `TASKS.md`.
