# ADR: Tracking dimensions and module switches

Status: **Proposed** (2026-10-19). Author: be-system-designer. Nothing here is built; the owner approves the order in section 12 before any slice starts (lean scope: a slice needs a named owner requirement).
Inputs: `backend.md`, `data-and-performance-rules.md` (sections 4, 5, 9, 11), `docs/dba/README.md` (1M tier, P95 < 1 s, migration timings), `docs/design/lean-scope.md`, `docs/pm/ROADMAP.md`, `adr-roles-permissions.md`, `adr-guid-keys.md`, and the code at `origin/main` 2148028: `StockBalance`, `StockLedgerEntry`, `StockDocumentLine`, `PostingEngine`, `IssuePostingStrategy`, `ReceiptPostingStrategy`, `BalanceLoader`, `SqlTransactionLocker`, `SystemSetting`, `UpdateSettingsHandler`, `PermissionCatalog`, `UserInfo`, `menu.const.ts`, `requirePermission`.
Scope: design only. The goal is a small core plus switch-on modules for Malaysian SMEs (trading, F&B, light manufacturing, retail) **without painting the stock ledger into a corner**. The ledger is the one part that is expensive to change later, so most of this note is about it.

## 0. Decision in short

1. **One optional tracking dimension, a lot.** A new `StockLot` (batch number with optional expiry, or a serial number) is referenced by a **nullable `LotId`** on `StockLedgerEntry` and in a child table of the document line. `StockBalance` (product x warehouse: quantity, moving average, marker) is **not changed**: it stays the sum over lots and stays the only thing every existing reader, report, low-stock rule and index looks at. A new `StockLotBalance (ProductId, WarehouseId, LotId)` holds the per-lot quantity.
2. **Per-product `TrackingMode` (None, Batch, Serial), default None.** Every row that exists today is None and stays valid. The mode can change only while the product holds no stock, and never back once it has had a lot (section 3.6). There is no conversion of existing stock in v1.
3. **Valuation stays one moving average per product.** No cost per lot. Lots carry quantity and expiry, not money.
4. **Locks stay `stock:{warehouse}:{product}`.** A lot needs neither a finer nor a coarser lock (section 3.4).
5. **Issues allocate FEFO** (earliest expiry first, then oldest lot) at posting time under the lock, unless the person picked a lot. A lot never goes negative. For a tracked product, **available = not expired** quantity (section 3.4).
6. **Module switches** are a code catalog (`ModuleCatalog`, like `PermissionCatalog`) plus one `SystemSetting` row per module (`Module.<code>`). Installation = company: there is no tenant or company id anywhere in `src/` (checked by search), so the switch is installation-wide.
7. **A switch gates the surface, never ledger semantics.** Off means: endpoints answer 404, permissions of the module are unassignable, menu and routes are gone, data is kept. The posting engine reads product data, never the switch, so flipping a switch can not corrupt stock.
8. **Module order:** accounting hand-off (carrying the switch foundation with it, no stand-alone infrastructure slice), stocktake, batch/lot, purchasing, sales and price lists, then returns, assembly, serial as targets only. Pilot default: trading/wholesale (section 8, 12).
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

Verified by the architecture review (PR #130): `SystemSetting` is not row-audited, so the flip handler writes its own event; the movements report reads document lines, not ledger rows (`PostingMovementsSpec`), while the ledger list and its export show one row per piece. Still unverified: whether the production SQL Server edition does a `NOT NULL DEFAULT` column add as a metadata-only change (it does not matter for `Product`, 50 k rows, seconds), and the time of the ledger FK validation at 1M rows (the DBA measures it).

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
| A. Nullable `LotId` on ledger and balance, balance key becomes `(Product, Warehouse, Lot)` | One balance row per lot; null lot for untracked | **Rejected.** Changes the key of the hottest table and of every reader of it (13 files in `Ims.Application` reference `StockBalance`: stock list, low stock, availability, reports, exports); the stock list would have to `SUM` over lots for every page, which is the O(n) the rules forbid and puts the 1M-tier work (low stock 1.7 s CPU, stock list scan) at risk. The moving average and `LastReceiptLineId` are per product, not per lot, so they would have to be split out again. |
| **B. `StockBalance` unchanged; lot detail beside it** | `StockLot` master, `StockLotBalance` per lot, nullable `LotId` on the ledger, child table on the line | **Chosen.** Additive, every existing reader keeps reading one row per product and warehouse, costing code is untouched. |
| C. Separate tracking ledger | A second append-only table for lot movements | **Rejected.** Two ledgers can disagree; "balance rebuildable from one ledger" would become two proofs and the reversal would write to both. |
| D. One row per unit for everything | Each unit is a row | **Rejected.** Millions of rows for fungible goods. Fine only for serials, which B already covers. |
| E. Lot in a free-text field on the line | No master data | **Rejected.** No expiry, no per-lot balance, no FEFO, no recall trace. |

### 3.2 Entities and columns

| Entity / change | Columns and keys | Notes |
|---|---|---|
| `Product.TrackingMode` | `int NOT NULL DEFAULT 0` (0 None, 1 Batch, 2 Serial) | 50 k rows: seconds. Changes only through `Product.ChangeTracking(mode, hasStockMovements)` (same rule shape as `ChangeBaseUnit`). |
| `StockLot : PublicEntity` | `Id` int identity (clustered), `PublicId`, `ProductId`, `Code` (lot or serial number, up to 64), `ExpiryDate` **`date`** NULL (a calendar day, mapped as `DateOnly`, outside the UTC-instant rule of BR-10), audit columns | `UNIQUE (Id, ProductId)` (target of the composite foreign keys). `UX_StockLot_Product_Code (ProductId, Code)` unique and **not filtered by `REC_DELETED`**, like the public id: a lot has no delete path (every foreign key to it is Restrict), so a code is never handed out twice and a soft-deleted lot cannot free a code and split the history. Codes use the database collation (`SQL_Latin1_General_CP1_CI_AS`, as SKUs do): `lot-a` equals `LOT-A` and trailing spaces do not count, which is right for lot and serial numbers; the application trims, and `CHECK (LEN(LTRIM(RTRIM(Code))) > 0 AND Code = LTRIM(RTRIM(Code)))`. A duplicate serial in one request is found in code under the lock and answered "serial already in stock", not left to the unique index (whose violation `UnitOfWork` would retry five times). A lot is identified by product and code; its expiry is **immutable after the first posting** (a receipt that names an existing code with another expiry is refused with 409). One public id because the API addresses lots. |
| `StockLotBalance` | PK `(ProductId, WarehouseId, LotId)` clustered; `QtyOnHand decimal(18,4)`; `ROW_VERSION`; audit columns; FK `(LotId, ProductId)` to `StockLot` (Restrict) | Same shape as `StockBalance`. Rows are never deleted (a zero row is history and rebuildable). `CHECK (QtyOnHand >= 0)` is invariant 3 and the last line of defence if the lock is ever bypassed. A serial balance of 0 or 1 cannot be a CHECK without a denormalised flag: it stays in code and in the reconcile drill. |
| `StockLedgerEntry.LotId` | `int NULL`, **no default**, composite FK `(LotId, ProductId) -> StockLot(Id, ProductId)` | Metadata-only add (numbers in 3.8): old rows are not touched and read as NULL (null = "before tracking" or untracked). New rows pay 4 bytes (108 to 113 bytes once the table is rebuilt). The composite FK makes the database refuse a ledger row whose lot belongs to another product (error 547; a NULL `LotId` passes), which nothing else would catch before the per-lot reconcile check. The FK index is declared explicitly and **filtered**, `IX_StockLedgerEntry_LotId (LotId, ProductId) WHERE LotId IS NOT NULL`, in the model (`HasIndex(...).HasFilter(...)`; EF then does not add its default unfiltered FK index, and a model test asserts the generated migration contains no other index on `LotId`). The principal key is `HasPrincipalKey(x => new { x.Id, x.ProductId })`. No CHECK constraint on the 1M-row ledger (it would scan it). |
| `StockDocumentLineLot` | `Id`, `LineId` (FK cascade from the line, as the draft aggregate is deleted with its document), `ProductId`, `LotId` NULL, `LotCode` NULL, `ExpiryDate` `date` NULL, `BaseQty`; FK `(LotId, ProductId)` to `StockLot` (Restrict); unique `(LineId, LotId)` filtered `LotId IS NOT NULL`, and unique `(LineId, LotCode)` filtered `LotId IS NULL` (drafts) | Receipt lines: the draft holds `LotCode`/`ExpiryDate`, `LotId` is filled at posting (the lot is created or found under the lock; an abandoned draft creates nothing). Issue lines: optional picks in the draft; at posting the allocation rows are written. One line can have several rows. Untracked lines have none. `CHECK (BaseQty > 0)` and `CHECK (LotId IS NOT NULL OR LotCode IS NOT NULL)`. |

The ledger gets one row per (line, lot) piece. Each piece carries the product-level `BalanceAfter` after that piece, `UnitCost` (receipt: the line cost; issue: the product average), and its `LotId`. A line of an untracked product writes exactly the row it writes today.

### 3.3 Invariants (proved by tests and by `docs/dba/08-reconcile-drill.sql`)

1. `StockBalance.QtyOnHand` = `SUM(ledger.QtyDelta)` by (product, warehouse), unchanged.
2. For a tracked product: `StockLotBalance.QtyOnHand` = `SUM(ledger.QtyDelta)` by (product, warehouse, lot), and `SUM(StockLotBalance)` = `StockBalance.QtyOnHand`. A product can become tracked only at zero stock (3.6), so its ledger rows with `LotId IS NULL` (the history before tracking) sum to zero per warehouse and the sums by lot are the whole story.
3. A `StockLotBalance` is never negative. A serial lot balance is 0 or 1.
4. `StockBalance.AvgCost`, the marker and every snapshot column behave as today: the product-level numbers are updated once per line by the existing `Receive`, `Issue`, `Unreceive` code, never per piece.
5. The ledger stays append-only; the trigger is not touched.

Both balances are rebuildable from the ledger; the drill gets a fifth check (per-lot sums).

### 3.4 Posting, reversal, transfer, negative stock

The strategies keep their signature and their code. The engine and `PostingContext` gain one input, the lot data of the tracked products of the document (mode, lots, lot balances), which `BalanceLoader` reads in the same locked step; for a document with no tracked product the input is empty. The engine asks one question per line: is the product tracked, or does the line already carry lot rows? If not it runs today's code path unchanged (this is what the oracle fixtures hold). Otherwise it runs `TrackedPostingStep` (a Domain service, pure like the strategies): it applies the line to the product-level `StockBalance` exactly once, then applies the lot pieces to `StockLotBalance` rows and emits one ledger entry per piece.

| Case | Behaviour |
|---|---|
| Receipt, Batch | The line must name a lot code (expiry required if the product says it expires: `Product.RequiresExpiry`, set with the mode; Malaysian food and pharma need it, other goods may not). Lot found or created under the product lock; qty added to the lot and to the product. |
| Receipt, Serial | Qty must equal the number of serials listed and be a whole number of base units; one lot per serial, balance 1. A serial already in stock (balance 1) is refused 409. A serial seen before and issued (balance 0) may come back only through a return (module `returns`), not a plain receipt. |
| Issue, Batch | Picks given: each must have enough, else 409. No picks: FEFO allocation over lots of this (product, warehouse) with balance > 0 and not expired, ordered by `ExpiryDate` (NULL last), then `LotId`. Not enough in non-expired lots: 409 `InsufficientStock` naming the shortfall. |
| Issue, Serial | The person lists the serials; no auto allocation. |
| Reversal of an issue | Each piece goes back into its own lot (expired or not), found from `StockDocumentLineLot`. |
| Reversal of a receipt | Taken out of its lot. If the lot holds less than the receipt (part was issued) the reversal is blocked by the same rule as today, now per lot: `FindReversalDeficits` returns deficits per (product, lot) for tracked lines. Lines of one product are still reversed last-posted first. |
| **Reversal of a document posted before the product was tracked** | **Refused (409)**: "Posted before lot tracking; correct it with a new document." A line of a product that is tracked now, with no `StockDocumentLineLot` rows, has no lot to go back to or come out of; reversing it would leave the product quantity above or below the sum of its lots (invariant 2) or put a NULL-lot row back into a tracked product. The alternative, an "unlotted" bucket, would be a permanent exception to invariant 2 and to every lot report, for a rare event (a pre-tracking document reversed after the product was converted at zero stock). The check is in the reversal use case, before any balance is touched (`PostingEngine` already refuses before changing stock). The reverse case is also defined: a line that has lot rows is always reversed through its lots, whatever the product's mode is now (a product that has had a lot cannot go back to None, 3.6). |
| Negative stock (BR-03) | **Does not apply to a tracked product, in posting, in reversal and in the deficit check.** A lot cannot go negative, and a product quantity above the sum of its lots would break invariant 2. Today `FindReversalDeficits` returns at once when `AllowNegativeStock` is on, `Unreceive` takes the setting as its `allowNegative`, and the issue strategy reads it; for a tracked line the engine ignores the setting on all three paths (deficits are always computed per lot, `Unreceive` gets `allowNegative: false`, the issue strategy is not used for the quantity check). `stock.negative.override` has no effect on a tracked product; the shortfall is a 409 that says to receive the stock first. Untracked products behave as today. This is the one behaviour the owner may dislike; see question Q5. |
| **Availability** | For a tracked product, **available = the sum of the lots that are not expired**; this is the number every check uses: the shortage test at submit and approval (`StockAvailability`), the posting check, the line editor and the 409 text. Expired = `ExpiryDate` earlier than today's Kuala Lumpur date (`Paging.StartOfDayUtc` convention): a lot is usable through the whole of its expiry date. The stock list and the low-stock rule keep reading `StockBalance.QtyOnHand` (they must not join lots; section 3.9), so expired stock counts there until it is scrapped; the expiry report and a Home count list expired lots so someone scraps them. The 409 says what is wrong: "Only 0 of 5 available: 10 on hand are expired (lots L1, L2). Issue them as Scrap or receive new stock." (`InsufficientStockException` gets an optional expired quantity.) Test: 10 on hand, all expired, issue of 5 fails at submit with this text, never first at posting. |
| Transfer (not built) | When the transfer strategy is built it moves pieces lot by lot (out of the source lot, into the same lot in the destination). It needs one lock order across two warehouses: `(warehouseId, productId)` ascending, which the locker does not do today (it orders products inside one warehouse). That is a precondition of the transfer slice, not of this ADR. |
| Expired lot, Scrap | An ordinary issue never takes an expired lot, by FEFO or by pick (a pick is refused). An issue with purpose `Scrap` (already in `IssuePurpose`) with no picks allocates over **all** lots of the product with a balance, earliest expiry first (expired ones included), so a person can scrap "everything expired" without picking each lot; with picks it takes the picks. Expired stock therefore leaves only through Scrap, which keeps the write-off visible at the average cost. |
| Lot required | A line of a tracked product on a receipt must name a lot code (and an expiry if `RequiresExpiry`); this is validated when the draft is saved and again at submit, and in the opening-stock import (one more column for tracked products; the check sits next to `ImportOpeningStockHandler`'s product checks), so a missing lot is never first found at posting. Lots are never deleted or deactivated (they are history); an empty lot stays at balance 0. |
| Unit conversion | Unchanged. The lot quantity is the base quantity (`BaseQty`); a serial line needs a whole base quantity. |

### 3.5 Locks and deadlock freedom

- **Keep `stock:{warehouse}:{product}`.** A finer lock (per lot) would let two issues of one product run in parallel, but both update the same `StockBalance` row (quantity, average, marker), which is the real serialisation point; the finer lock would only add a second lock level and a possible cycle. A coarser lock (per warehouse) would put every product behind one queue; the burst test in `docs/dba` (150 postings on 5 hot products, write P95 698 ms) shows the per-product grain is what keeps this flat.
- **Read only what the document names** (section 3.9, "Lot reads"): explicit lots by primary-key seek, FEFO only for lines without picks and bounded by `TOP`. Inside the lock, lot rows are written in `(ProductId, LotId)` ascending order. Every writer holds the product lock first, so the order of the `StockLotBalance` and `StockBalance` writes is deterministic. That is the only part where a cycle is impossible: `StockLot` is global and the product locks are per warehouse (next point).
- **Lot creation race and the one real deadlock risk.** Two receipts of the same new lot code in two different warehouses hold different locks and both insert `StockLot (P, 'L1')`; the unique index rejects the second. Worse, receipt A (warehouse 1) creating L1 then L2 and receipt B (warehouse 2) creating L2 then L1 take the unique-key locks in opposite order: a real deadlock (1205). Two measures: new lots of a document are created in ascending `(ProductId, Code)` ordinal order (a total order, whatever the order of the lines) (removes the cycle within one product), and `UnitOfWork.ExecuteAsync` already retries 1205 and unique-key races (max 5 attempts, randomised back-off), so a loser re-runs and finds the lot. The claim is "recovered by retry", not "impossible". If the retry rate ever shows up in `11-snapshot.sql`, take an extra lock `lot:{productId}` for receipts that create lots (same ascending order, taken before the stock locks). F2 tests both the same-code and the opposite-order case.
- Lot master rows (expiry, code) are written only by receipts, under the product lock of the receiving warehouse.

### 3.6 Changing the mode of an existing product (v1: only at zero stock, no conversion)

| Situation | Rule |
|---|---|
| No ledger row for the product | Free change (None, Batch, Serial). |
| Has movements, on hand is 0 **and in transit is 0** in every warehouse (a negative balance is not zero: BR-03 allows it for untracked products, and a negative lot balance is forbidden), no open document line (draft, submitted, approved) for it, and it has never had a lot | None to Batch or Serial is allowed; old rows stay `LotId NULL`; the audit event records the change. |
| Has stock | **Refused (409)**: "Bring the stock to zero first (a stocktake that counts 0, or a Scrap issue), then receive it again with lot numbers." The cost: when stock is zero the next receipt sets the average to its own cost (`StockBalance.Receive`), so the person types the cost again. |
| The product has had a lot (any `StockLot` row) | The mode never changes again: no return to None, no Batch to Serial. Lots are history, and a line with lot rows must keep reversing through them. |

**Conversion of existing stock into lots is deferred, on purpose.** The earlier draft proposed a "conversion posting" of two net-zero Adjustment rows. The code cannot express it: `StockDirection.Of` throws for `Adjust` (a test pins the sign convention against the movements SQL), a line carries no direction and `StockDocumentLine.Create` rejects a quantity of 0 or less, the ledger needs a `SourceDocId` (which document?), the intermediate `BalanceAfter` of the two rows would be a fake zero, the movements report would show noise (`stockEffect` plus and minus X), and its reversibility is undefined. Doing it right is a design of its own (its own document kind and strategy). Trigger to design it: the first customer who has stock of a product they must convert and cannot bring to zero. Until then the zero-stock path above is the rule; this keeps `tracking.convert` and the conversion slice out of v1.

Note for the stocktake slice (not this ADR): `Adjust` also needs a signed quantity per line and its own `StockDirection` rule; that work belongs to the stocktake design, and is why it is sized L in section 8.

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
| `AddProductTrackingMode` | `Product.TrackingMode int NOT NULL DEFAULT 0`, `RequiresExpiry bit NOT NULL DEFAULT 0` | 1.0 s and 21 MB of log for 50,002 rows (DBA measurement); fine. Never do this on the ledger or the lines table | drop the columns |
| `AddStockLots` | create `StockLot` (with `UNIQUE (Id, ProductId)`), `StockLotBalance`, `StockDocumentLineLot`; add `StockLedgerEntry.LotId int NULL` (**no default**) and `ProductId`-composite FK `(LotId, ProductId) -> StockLot(Id, ProductId)` | tables empty. DBA measurements at 1M ledger rows (SQL Server 2025 **Express**): `ADD LotId int NULL` is metadata-only (2 ms, 1 KB of log; the row grows from 108 to 113 bytes, +4.6%, at the next rebuild). `ADD ... NOT NULL DEFAULT 0` on the ledger is **not** metadata-only on Express (1.7 s, 232 MB of log, pages double, peak space 2x the table; Standard/Enterprise untested), so the column is nullable with no default. FK validation: 540 ms schema lock single-column, **140 ms composite**. | **throws if any `StockLot` row exists** (the lot dimension would be lost); otherwise drops the FK and the index first, then the column and the tables |
| Backfill | none: every existing row is None/NULL, which is valid | 0 | |

- Fill factors: `FILLFACTOR` 100 on append-only tables (the ledger), 90 on random-key indexes (`UX_StockLot_PublicId`, as in `PublicIdColumn`), `StockLotBalance` like `StockBalance`. After the migration run `docs/dba/07-maintenance.sql` (the clustered index of the ledger grows 4 bytes per row at the next rebuild; no maintenance window beyond the weekly one, since nothing rewrites the table).
- No downtime beyond the normal deploy. The Up test (`AverageCostMigrationTests` pattern) runs Up, Down, Up on a database that holds rows, and Down with a lot present must fail.
- Single API instance (lean scope 1.4) and the migration runs at start-up, so there is no mixed-version window. If a second instance is ever deployed, the module stays off until every instance runs the new build.

### 3.9 Performance at the 1M-document tier

| Item | Estimate and rule |
|---|---|
| Ledger growth | +4 bytes a row; one extra row per additional lot piece of a tracked line only. Untracked products: no extra rows, no extra statements. |
| `StockLotBalance` rows | tracked products x warehouses x live lots. Example: 5,000 tracked products (10% of 50 k), 2 warehouses, 40 live lots = 400 k rows, about 70 bytes a row = about 30 MB. Smaller than `StockDocument`'s one index. Zero-balance rows stay. |
| Posting, tracked line | Untracked document: **no extra statement**: `DocumentForUpdateSpec` already loads the lines and joins their product, so `Product.TrackingMode` rides in that query (assumption to confirm in the slice: the spec must add the product to its projection; if it does not, the mode is snapshotted on the line in a new nullable column, which also records which path the line used). Tracked document: +1 statement for the lot rows of its lines (receipt lot codes, picks), +1 for the lot balances named or allocated (above), writes of the lot balances and the extra ledger rows. Budget: write P95 within 5% of today (k6 `write-mix`); the statement-count test asserts "same as the freeze" for untracked and the exact count for tracked. |
| Stock list, low stock, summary | **Unchanged**, they read `StockBalance`. This is the main reason for option B. |
| Indexes | Migration: only the filtered ledger index `IX_StockLedgerEntry_LotId (LotId, ProductId) WHERE LotId IS NOT NULL` (3.2). Measured by the DBA on 999,636 ledger rows: an unfiltered `LotId` index is 17.5 MB (a 7th structure on the hottest table, 460 ms to build); the filtered one is 0.00 MB (69 ms); `IX_StockDocument_ReversedBy` is the precedent. Writing a 5-row ledger insert costs 0.63 to 0.65 ms with any variant: noise, so the saving is space, not time. With the expiry report (not before): `IX_StockLot_Expiry (ExpiryDate) INCLUDE (ProductId) WHERE REC_DELETED = 0 AND ExpiryDate IS NOT NULL` **and** the live-lot index `IX_StockLotBalance_Live (LotId) INCLUDE (ProductId, WarehouseId, QtyOnHand) WHERE QtyOnHand > 0` (also the FK index of `StockLotBalance`, about 10.5 MB for 800 k rows), because the expiry index alone is not enough: the 30-day report reads 7,812 pages and 190 to 260 ms of CPU with only the expiry index and 1,344 pages and 109 ms with both. The report must repeat `QtyOnHand > 0` or the filtered index is not used. |
| Lot reads (O(1) per request) | A statement reads the lots **the document names**, never the whole product (`data-and-performance-rules.md` 4): explicit picks, serials and receipt lots by `(ProductId, WarehouseId, LotId) IN (...)` (PK seeks, one per named lot); FEFO only for a line without picks, one statement per such product: PK prefix seek on `(ProductId, WarehouseId)` joined to `StockLot` for the expiry, `QtyOnHand > 0` filtered before the join, `TOP (Limits.MaxLotsPerLine)` (for example 200; a line that needs more pieces is refused with "split the line"). Measured by the DBA at 1M tier: 40 live lots cost 4 pages of balance plus 131 reads of `StockLot` (3.3 reads a live lot) and 0 ms CPU; 400 extra zero-balance rows add 4 balance pages and no join reads, so history is cheap. A serial product with 5,000 serials in stock is never read whole, because a serial issue names its serials. No denormalised expiry. |
| To re-measure | k6 `write-mix` and `write-hot` with 20% tracked products; the FEFO statement with a list of 1 and of 20 products (parameter sniffing: tag the lot reads `QueryHint.NoSniffing`, and the per-lot trace `Recompile`, as in `docs/dba` 4b and 4d); the ledger page with the new LEFT JOIN `StockLot` for `lotCode` (one seek per row: 50 per page, up to 50,000 for an export); `07-maintenance.sql` plan; stock list, low stock and movements at the 1M seed (expect equal reads). Scripts to update with the batch slice: `01-seed-perf.sql` (`TrackedShare`), `05-backup-restore.sql` (row-count list), `08-reconcile-drill.sql` (check 5, per lot). |

Import: the opening-stock import needs a lot column for tracked products (one more template column, only shown when the module is on); products import can set the mode only for products with no movements.

## 4. Module switches

### 4.1 Where they live

| Part | Decision |
|---|---|
| Catalog | `ModuleCatalog` in `Ims.Domain` (static, like `PermissionCatalog`): `ModuleDefinition(Code, Name, Description) { Requires = [...] }`. Codes are stable once released. Architecture tests: unique codes, `Requires` acyclic. |
| State | One `SystemSetting` row per module: key `Module.<code>` (well under the 100-character key limit), value `true`/`false`. A missing row is off. No new table, no company column (installation = company; confirmed: no tenant or company concept exists in `src/`). |
| Not in `SettingsDto` | `PUT /api/settings` writes a fixed three-field record and replaces all of it; modules get their own endpoints so an old client cannot flip one by omission. |
| Read | `IModuleSwitches` (scoped) shares the one read of the whole `SystemSetting` table that `DbSystemSettings` already makes once per request (a PK table of a few rows; one page), instead of a second round trip. A missing row is off, so no seed is needed. No cross-request cache: one API instance, lean scope 1.4; add one only if a measurement shows the read matters. |
| Defaults | An upgraded or new installation has every module off: its behaviour equals today's. |

### 4.2 API

| Endpoint | Policy | Notes |
|---|---|---|
| `GET /api/modules` | any signed-in user | `[{ code, name, enabled, requires }]`. The web reads it at start. |
| `PUT /api/modules/{code}` | `modules.manage` (new, core, dangerous, Admin only by default) | Body `{ enabled, reason }` (reason at least 5 characters, like cancellation). Enabling needs every `Requires` module on (409 otherwise). Disabling is refused (409 with a count) while a dependent module is on or the module's own `CanDisable` check fails (section 4.4). Writes `module.enabled` / `module.disabled` with who, reason, before and after. |
| `UserInfo.modules` | | Enabled codes, built at sign-in, refresh and `/me`. Not claims. |

Contract notes for the foundation slice: `GET /api/modules` has no permission policy and goes on the fixed no-policy list in `EndpointCoverageTests` (it sits in the signed-in-only group beside `/me`); the new endpoints are added to `EndpointCatalog`. `UserInfo` is a positional record, so the new `modules` parameter changes its constructor for every caller and test double; the OpenAPI document and the generated web client change in one named batch (ROADMAP 3.3), not in the API PR. Ledger rows gain `lotCode` (`LedgerRow`, the ledger list and its Excel export, as a module-gated column), because a line split over lots otherwise shows two rows with the same document and no reason; the movements report is built from document lines and keeps one row per line, with no lot column in v1.

### 4.3 What "off" means

| Surface | Off |
|---|---|
| API of the module | **404** (the same ProblemDetails as an unknown route). Not 403: 403 says "it exists, you lack the right" and lets a caller probe an installation; 404 matches the ids rule (out of scope is 404, never 403). Done by `[RequiresModule("batch")]` on the controller (endpoint metadata) and a small `ModuleGateMiddleware` placed **between `UseAuthentication()` and `UseAuthorization()`** in `PipelineSetup` (lines 46 and 48 today). It is middleware, not an MVC filter, on purpose: MVC authorization filters run before resource and action filters, so a filter would give 403 to everyone without the permission and 404 only to holders, which is the probe it is meant to prevent. Order: no valid token goes on to authorization (401 as today); a signed-in caller of a disabled module gets 404 whatever their permissions; an enabled module falls through to the policy (403 or 200). Test per case: anonymous, signed-in without the permission, with it, module off and on. An architecture test requires the attribute on every controller in a module namespace, like `EndpointCoverageTests` does for policies. |
| Optional fields on core endpoints | A document line with `lotCode`, `picks` or `serials` while `batch`/`serial` is off: 400 "not available". Responses omit the lot fields (like cost fields are withheld). |
| Permissions | `PermissionDefinition.Module` (null = core). `GET /api/permissions` lists only codes of enabled modules; `PUT /api/roles/{id}/permissions` rejects a code of a disabled module (400); existing grants stay as rows and come back on re-enable. **The role write must not delete them:** `PUT /api/roles/{id}/permissions` replaces the whole set (`Role.SetPermissions(wanted)`), so `ReplaceRolePermissionsHandler` merges: the set it saves is the submitted codes plus the role's existing grants of disabled modules (never counted as added or removed, never audited as removed, validated for `Requires` closure only among the visible codes). Re-enabling a module gives these grants back at once, including dangerous ones, so the enable reply lists how many roles regain grants and the audit event records it. The token still carries the codes the role holds (no change to the claim shape, no `acv` bump): the gate middleware, not the claim, decides, so a flip takes effect on the next request. |
| Web | `MenuItem.module?`; `NavigationService` shows an item if the module is on AND any listed permission is held; `requireModule('batch')` beside `requirePermission`, sending to the not-found page, not the 403 page. Screens that embed a module field (the lot input on a line) are removed by the same signal. |
| Reports and exports | Lot columns carry `Module` next to `VisibleWith` on `ReportColumn`; a column of a disabled module is left out, header and cells, by `IReportWriter` (the first module that has such a column adds the property; `ReportColumn` has none today). Module reports are module endpoints (404). |
| Data | **Kept, hidden, and back on re-enable.** Core tables keep their lot columns; core responses omit them while off. Nothing is deleted or archived. |
| Ledger and posting | **Unchanged by the switch.** The engine reads product data. Because a posted document of a tracked product must still reverse correctly, tracking never depends on the switch. |

### 4.4 Disabling safely

Each module declares `CanDisable` (a query, run in the flip handler; added with the second module, the first has nothing to check):

| Module | Refuse while |
|---|---|
| `batch`, `serial` | any product of that mode has stock in a warehouse or a line on a draft/submitted/approved document |
| `purchasing`, `sales` | any open order |
| `stocktake` | any open count |
| `assembly` | any open work order |
| `accounting`, `returns` | never refused |

A tracked product whose module is off cannot be used on a new document line (the same refusal as an inactive product, `Product.EnsureUsableOnDocuments`), so no untracked quantity can enter a product whose lots would then be out of step. The flip handler runs under the existing application-lock pattern with a new resource `modules` (`ITransactionLocker.LockModulesAsync`, `retry: false` like `roles`, so a busy lock is a 409) (never held with a stock lock), so two flips do not interleave; a posting that races the check cannot corrupt anything because posting ignores the switch (worst case: a module is off while it holds stock; it is switched on again).

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
| F2 | Two receipts of one new lot code in two warehouses race on `UX_StockLot_Product_Code`; two receipts creating (L1, L2) and (L2, L1) in opposite order in two warehouses | Parallel receipts, 25 rounds each: all succeed (a 1205 is retried), one lot per code, balances correct; creation in `Code` order is asserted. |
| F3 | A receipt names an existing lot with another expiry | 409, nothing posted, the stock unchanged. |
| F4 | Reversal of a receipt after part of the lot was issued, with `AllowNegativeStock` off AND on | Blocked with a per-lot deficit in both cases; stock and ledger unchanged. |
| F5 | Reversal of a multi-lot issue puts a piece in the wrong lot | Property test: post a random issue then reverse it; every `StockLotBalance` and the average equal the start. |
| F6 | A retry after a deadlock victim repeats lot creation or writes the allocation twice | Fault-injection: force 1205 once inside `ExecuteAsync`; one lot, one set of ledger rows. |
| F7 | Switching a module off while a tracked product holds stock | 409 with a count; stock untouched. |
| F8 | The switch flips between a draft and its posting | Post a tracked document after the flip: posting works (the engine ignores the switch); the line editor is 404. |
| F9 | A document posted before the product was tracked is reversed after the product became tracked at zero stock (it would leave the product quantity out of step with its lots) | Post an issue, bring the product to zero, make it Batch, reverse the old issue: 409 "Posted before lot tracking", stock, lots and ledger unchanged. Also: a line with lot rows reverses through its lots. |
| F10 | Migration `Down` silently drops lot data | Down with one lot present throws; Up/Down/Up on a database with rows. |
| F11 | A tracked product's quantity exceeds the sum of its lots after a manual SQL edit | `08-reconcile-drill.sql` check 5 reports it; rebuild script replays the ledger by `(product, warehouse, lot)` in `Id` order. |
| F12 | A permission of a disabled module is still assignable or still works | Generated tests: for each module code, every module permission is rejected by the role API while off and every module endpoint answers 404 while off, 403/200 as the role says while on. Role edit while a module is off: saving a role keeps its grants of the disabled module (the merge in 4.3), the `removed` list in the reply and the audit row never names them, and re-enabling gives them back; the enable reply says how many roles get grants back. |
| F13 | 10 on hand, all expired; an issue of 5 passes a check on `QtyOnHand` and fails at posting | Submit fails with "Only 0 of 5 available: 10 on hand are expired", the same text at approval and posting; a Scrap issue without picks takes the expired lots, earliest expiry first; the day boundary (expiry date = today is still usable, yesterday is not; Kuala Lumpur date) with a fake clock. |
| F14 | A tracked receipt line with no lot code is first found at posting | 400 at draft save and at submit; opening-stock import rejects the row. |

## 8. Modules, order, first hits

Dependencies on the tracking dimension are marked. "First hit" is the module that makes a segment want to buy.

| # | Module code | What | Requirement | Size | Needs tracking? | New permissions (module-owned) |
|---|---|---|---|---|---|---|
| 0 | (core) module foundation, **not a slice of its own** | Built inside the first approved module's slice with one catalog entry: `ModuleCatalog`, `IModuleSwitches`, `GET/PUT /api/modules`, `[RequiresModule]` and the gate middleware, web `module` gating, `modules.manage`. `Requires`, `CanDisable`, `Module` on report columns and a first-run screen are added when a second module needs them. | enables the first module | about M of its own (about 1,500 lines with specs and web) inside that slice | no | `modules.manage` (core) |
| 1 | `accounting` | Export of posted documents and stock movements in one fixed layout (Excel/CSV, one row per line: date, document no., type, partner, SKU, quantity, unit cost, amount, tax code) for the accountant to import into SQL Account, AutoCount or Xero; **a file, not an integration**. Tax-ready optional fields: `Product.HsCode` (classification), `Product.SstCode`, `Partner.Tin`, `Partner.SstRegNo`, validated for shape only | R1 | S to M | no | `accounting.export` |
| 2 | `stocktake` | A count document per warehouse (all or by category), counted quantity entry (phone), variance, approval, posting via a new **Adjust** strategy (`LedgerReason.Adjustment`) that needs a signed quantity per line and its own `StockDirection` rule (today `StockDirection.Of` throws for `Adjust`). With `batch` on, count per lot | R3 | **L** (the strategy, the count document, the phone screen) | optional | `stocktake.count`, `stocktake.post` |
| 3 | `batch` | Section 3: lots, expiry, FEFO, expiring and expired report, lot trace (no conversion of existing stock in v1) | R5 | L (3 slices: domain and posting; web and import; reports and indexes) | **is it** | none (uses `document.*`; Q8) |
| 4 | `purchasing` | Purchase order, receive against PO (a receipt linked to PO lines, partial receipts), reorder suggestions from safety stock | R2 | L | optional (lot on the receive line) | `po.create`, `po.approve` |
| 5 | `sales` | Sales and delivery orders, delivery posts an Issue with purpose Sale; price lists in a second slice | R4, R9 | L | optional (FEFO on delivery) | `so.create`, `pricelist.manage` |
| 6 | `returns` | Customer and supplier returns linked to the original document, same lot back | R6 | M | needs `batch` to return into the right lot | `returns.create` |
| 7 | `assembly` | Kits and simple assembly: a bill of materials, a work order that consumes components (Issue-like, FEFO) and produces the item (Receipt-like) at the sum of the consumed average costs | R8 | L | optional (lot trace of consumed lots) | `bom.manage`, `workorder.post` |
| 8 | `serial` | Section 3 with `TrackingMode = Serial`: same tables, one lot per serial | R7 | M | needs `batch` tables | `serial.trace` only if wanted |

Rows 6 to 8 (`returns`, `assembly`, `serial`) are **targets only**: they are listed so the tracking design leaves room for them (lot-aware returns, lots consumed by a work order, a serial as a lot of one), and nothing more is designed here until the owner approves them.

First hit by segment: trading and wholesale `accounting`, then `purchasing` and `sales`; F&B and central kitchen `batch`, then `stocktake` per lot; light manufacturing `assembly`, then `purchasing`; retail `stocktake` (POS is not built, section 9).

**Why this order.** The first approved module carries the switch foundation. `accounting` changes no ledger column. `stocktake` builds the Adjust strategy and the signed line direction that later modules (`assembly`, `returns`) need. `batch` is the only module that touches the ledger and posting, so it comes once the oracle fixtures exist and before the larger document modules (`purchasing`, `sales`), which would otherwise be built twice, once without lots and once with. If the pilot is F&B, `batch` moves first and carries the foundation; the design above does not change.

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

1. **Oracle fixtures first** (before slice `batch` changes anything), the technique used for roles, extended to the changed engine input (`PostingContext` and the balances map gain the lot data, so the oracle drives the engine, not only the strategies) (`tests/Ims.Domain.Tests/Identity/Oracle`). New folder `tests/Ims.Domain.Tests/Stock/Oracle`: a generator run at the commit before the change writes compact CSVs of: scripted sequences of receipts, issues, reversals, negative-stock overrides, unit conversions and multi-line same-product documents, with for each step the resulting ledger rows (`QtyDelta`, `BalanceAfter`, `UnitCost`, `Reason`), the final `StockBalance` (`QtyOnHand`, `AvgCost`, `LastReceiptLineId`), the line snapshots (`AvgCostBefore/After`, `PreviousLineId`) and the exceptions raised. New code must reproduce every value for None-tracked products. Plus a seeded random-sequence test (the same sequences on the frozen reference implementation kept in the test project and on the engine).
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
| Q4 | May expired stock leave only by a Scrap issue (which, with no picks, takes every lot earliest expiry first)? | Yes. |
| Q5 | May a tracked product ever go negative (posting, reversal or the setting `AllowNegativeStock`)? | No: tracked lines ignore the setting on all three paths (3.4); revisit on the first complaint. |
| Q6 | Who may switch modules? | Admin only (`modules.manage`), with a reason, audited. |
| Q7 | Is the accounting hand-off a file in one fixed layout enough, or is a target package (SQL Account, AutoCount, Xero) named? | One fixed layout first; adapt it to the first customer's package. |
| Q8 | Do lots need their own permissions? | No; use the document permissions. |

## 13. Rollout

| Step | Content | Size | Gate |
|---|---|---|---|
| 0 | Oracle fixtures and statement-count test on current `main` (tests only) | S | Merged before anything else |
| 1 | First approved module (default `accounting`) with the module foundation inside it (section 8, row 0) | S to M, plus about M for the foundation | Owner yes on R1; review gate; one migration: `modules.manage` for Admin |
| 2 | `stocktake` (Adjust strategy, signed direction) | L | Owner yes on R3 |
| 3 | `batch` slice 1: mode, lots, posting, reversal, locks, migration | L | DBA measurement, oracle green, concurrency tests |
| 4 | `batch` slice 2: web and import | M | e2e |
| 5 | `batch` slice 3: reports (expiring, expired, lot trace), indexes | M | 1M-tier numbers |
| 6+ | `purchasing`, `sales`, `returns`, `assembly`, `serial` in the order above | L, L, M, L, M | Each: owner yes, id in `TASKS.md`, review gate |

Docs to update when step 1 and step 3 merge: `backend.md` (a "Modules" and a "Tracking" section, short), `data-and-performance-rules.md` section 9 (the new indexes), `docs/dba/README.md` (the measurements), `TASKS.md`.
