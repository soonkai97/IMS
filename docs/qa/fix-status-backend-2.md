# Backend fix status for the supervisor re-test (2026-10-11)

Report: `retest-power-supervisor.md` (PR-01 .. PR-14, and the old PS findings it re-checked). The report itself is not edited. First round: `fix-status-backend.md`.

Delivered as three pull requests, **each based on `main` and independent of the others** (none is stacked). Merge order: **1, 2, 3**. The three branches merge into one tree without a conflict, in any order (checked), and the combined tree builds and passes every test. Only the first and the third add a migration; they change different tables' columns, so the model snapshot merges as text.

| PR | Branch | Holds |
|---|---|---|
| 1 | `feature/avg-cost-and-money-precision` | PR-01: reversing a receipt takes its cost out of the moving average; internal cost precision 10 decimals (migration `AverageCostSnapshotAndCostScale`) |
| 2 | `feature/avg-cost-and-money-precision-2` | Product-owner decision: money has exactly 2 decimals, rounded half away from zero (no schema change) |
| 3 | `feature/avg-cost-and-money-precision-3` | PR-04, PR-05, PR-08, PR-10, PR-11, PS-12, PS-14, this list (migrations `AddDocumentReversedBy`, `AddLedgerBalanceAfter`; the QA review of the first version is fixed in `feature/qa-backend-fixes-5`) |

Legend: **fixed** = done and covered by a test; **not done** = no change made, reason given; **needs decision** = waits for a product answer; **UI** = the frontend owns it (the contract it relies on is in `docs/team/HANDOFFS.md`, "Contract changes for the frontend").

## New findings (PR)

| ID | Status | Notes |
|---|---|---|
| PR-01 | fixed (PR 1) | Cause: `ReceiptPostingStrategy.Reverse` only called `balance.Issue`, which never changes the average, so the reversed receipt's cost stayed blended into it. Now each posted receipt line remembers the average before and after it (`AvgCostBefore`, `AvgCostAfter`); `StockBalance.Unreceive` puts the average back to `AvgCostBefore` while nothing but issues came after (exact, however many units were issued in between: the reported case gives 735 PCS at 4.76 again, value 3,498.60, a 60-unit issue worth 285.60, no approval); if a later receipt blended into the average the receipt's `qty x cost` leaves the stock value and the rest is shared by the remaining units; where that has no sound answer the average is kept. Rule and examples: `docs/design/mvp-ux.md` 1.5. Tests: `ReceiptReversalCostingTests` (domain: the reported scenario, receive-receive-reverse the first and the second, receive-issue-reverse, right after posting, zero stock, negative stock, two lines of one product, legacy receipts without a snapshot, precision) and `ReceiptReversalCostTests` (API: the full scenario, stock value, average cost and issue estimate). |
| PR-02 | needs decision | Q3: Cancel (and Reject back to Draft) on an Approved document for Supervisor and Admin, a shortage warning in the Approve dialog. No backend change until decided; `lines[].available` against `baseQty` is already in the DTO. |
| PR-03 | UI | The API already answers `400` "The 'from' date is after the 'to' date." under `errors.to` on every report endpoint (tested in `InputValidationTests`); the screen must map it. |
| PR-04 | fixed (PR 3) | One definition, `Ims.Domain.LowStockRule`, used by the low-stock list and export, the Home count, the badge, the stock list with `lowOnly=true` and its export, and the `isLow` flag of a stock row. `GET /api/stock?lowOnly=true` now includes the active products with a safety level that have never been stocked IN A WAREHOUSE (quantity 0, `avgCost` and `value` null), once per active warehouse in scope that has no balance row for them (per warehouse: stock elsewhere does not matter, and a scoped caller learns nothing about other warehouses), so it has exactly the rows of `GET /api/stock/low-stock` (documented on `StockFilter` and `StockRow`). `isLow` is false for an inactive product and in an inactive warehouse, as on the low-stock list. **Low means on-hand at or below the safety level** (design 3.8: "on-hand <= safety stock"; the first version said "equal is not low"). The sets are built once in `LowStockSets`; the `isLow` projection asks `LowStockRule` for its verdict. Tests: `LowStockPerWarehouseTests`, `LowStockOneDefinitionTests` (set equality per warehouse and in total, the Home count, both exports, row shape, filters and sorts on both kinds of row, paging, warehouse scope, the flag) and `LowStockRuleTests`. |
| PR-05 | fixed (PR 3) | The documents search finds the number (whole or part), the remark and the partner name, case-insensitive; `%`, `_` and `[` are plain text. Every matching partner is found, however many match (up to 50 matching partners as an id list, more through a join; nothing is capped or refused); the comparison is case-folded and binary (about ten times less CPU than the linguistic collation); a text of fewer than 3 characters only matches the start of the number; no search index (measured at 1M documents in `docs/architecture/data-and-performance-rules.md` section 9). Test: `DocumentSearchTests`. |
| PR-06 | UI | The table layout is the frontend's. `productName`, `uom`, `baseQty` and (with `cost.view`) `unitCost` and `amount` are all in `GET /api/reports/movements` already. |
| PR-07 | needs decision | Cost of stock received by Warehouse staff (cost correction / opening-cost step). Not touched. |
| PR-08 | fixed (PR 3) | The movements Excel has a "Reversal" column after "Status": "Yes" on the reversal row, blank on a posting row. Test: `SignedStockEffectTests`. |
| PR-09 | UI | The tab check could use the list item's `submittedByName`; no backend change needed. |
| PR-10 | fixed (PR 3) | The low-stock list is worst first by the share missing from the safety level (negative rows first, then never-stocked, ...), then by units, SKU and warehouse code, as the design says (`LowStockOneDefinitionTests`). The stock list gets `sortBy=Name`; its default stays `Sku` (the API default is the client's choice: the design wants Name, so the screen should send `sortBy=Name`). Every sort now ends in SKU and warehouse code. |
| PR-11 | fixed (PR 3) | `DocumentDto` has `reversedBy`, `reversedByName`, `reversedAt` for "Reversed by {name} on {date}" (new column `StockDocument.ReversedBy`, filled in for old documents from their reversal ledger entries). Test: `ReversalTests`. |
| PR-12 | not done / UI | A document-number suffix search finds nothing: by design. The audit log is searched by prefix only, so every filter stays an index seek (`data-and-performance-rules.md` section 9: a `LIKE '%x%'` on the audit table is not allowed). The "Show" button names and the entity-type dropdown (`/api/audit/filters` already returns the list) are UI. |
| PR-13, PR-14 | UI | |

## Old findings that the re-test left open

| ID | Status | Notes |
|---|---|---|
| PS-11 | needs decision | Same as PR-02. |
| PS-12 | fixed (PR 3) | `GET /api/audit?hideSignIns=true` leaves out `login.success`, `login.failed`, `login.locked`, `login.lockout` and `logout` and combines with the other filters ("everything except sign-ins"). A settings save writes ONE `settings.updated` event with the old and the new values; the row per changed key is gone (`SystemSetting` is no longer row-audited). Tests in `AuditSearchTests`, `AuditTrailTests`. |
| PS-13 | needs decision | Paging controls (the API offers `afterId` and `pageSize`). |
| PS-14 | fixed (PR 3) | `LedgerRow.balanceAfter` on `GET /api/stock/ledger?productId=...`: what the product held in that warehouse right after the movement, stored on the ledger row when it is written, under the posting lock (column `StockLedgerEntry.BalanceAfter`, migration `AddLedgerBalanceAfter` backfills old rows); the row is never updated, so the ledger stays append-only. Null when the ledger is not read for one product. Not added to the Excel ledger export (up to 50,000 rows of several products; say if you want it for a one-product export). Tests: `LedgerBalanceAfterTests` (running sums, page sizes and cursor, reversals, two warehouses, one document, scope, parallel postings, the whole database) and `MigrationTests`. |
| PS-15 | UI / needs decision | Nothing is left on the backend: `from` after `to` is a `400` (above), signed quantity and the export are done. The date format is a decision; the empty state and the export message are UI. |
| PS-20 | not reproducible | |
| PS-22 | UI | Wrapping of SKU and date in the Reports table (CSS). |
| PS-26 | needs decision | Dialog focus. |

## Money precision (product-owner decision, PR 1 and PR 2)

| Rule | Where |
|---|---|
| Money has exactly 2 decimal places, no sub-cent amount; quantities keep the precision of their unit | `Ims.Domain.Money`; `docs/architecture/data-and-performance-rules.md` section 11 |
| A unit cost (documents, draft edits, the opening-stock import) and an approval amount threshold have at most 2 decimals: `400` "Unit cost can have at most 2 decimal places." / "An amount can have at most 2 decimal places."; a row error in an import file | FluentValidation and a domain rule (`StockDocumentLine.Create`, `ApprovalRule.Update`) |
| Line amount = `round(base quantity x unit cost, 2)`; document total = the sum of the rounded line amounts; stock value = `round(quantity x average, 2)` per row and the total is the sum of the rounded rows; estimates are rounded the same way and the approval rules compare the rounded total | `StockDocumentLine.Amount`, `StockDocument.TotalAmount`, the specs and `EfStockSummaryReader` |
| Rounding mode: half away from zero (`MidpointRounding.AwayFromZero`), in C# and in SQL (`Math.Round(x, 2)` is SQL `ROUND`, also away from zero on a decimal; `MoneyPrecisionTests` pins them together at the half-cent boundary) | `Money.Rounding` |
| Every API money value and Excel money cell is rounded to 2 decimals; Excel cells are numbers formatted `#,##0.00` | specs, `ResponseShaper` (rounds every value tagged `[VisibleWith]`, i.e. every money value), `ReportColumn.IsMoney`, `ClosedXmlExcelWriter` |
| Internal precision of the average and of the cost per base unit: **10 decimals** (`Limits.CostDecimals`, SQL `decimal(18,10)`, was 6) so repeated receipts and unit conversions do not drift by a cent over a large quantity | PR 1, migration `AverageCostSnapshotAndCostScale` (nothing is rounded by it; `Down` restores `decimal(18,6)`) |
| Database: the only column that holds an amount, `ApprovalRule.AmountThreshold`, is already `decimal(18,2)`; no money column needed a migration | |

New observation, not a re-test finding: `DocumentLineDto.unitCost` is the cost per BASE unit. The web editor reloads it as the cost per ENTERED unit (`lineFromDocument`), so a draft receipt line in a non-base unit (1 carton at 120.00, factor 12) comes back as 10.00 and is saved as 10.00 per carton. This predates the round and is the frontend's to fix (convert with the line's factor), or the backend can add an `enteredUnitCost` (cost per entered unit, 2 decimals) to the line: say if you want it.

## Items that wait for a decision (not done)

1. Q3: Cancel on an Approved document (PR-02, PS-11).
2. Cost of stock received by Warehouse staff (PR-07).
3. Date format of the date inputs (PS-15, NV-27).
4. Paging controls (PS-13).
5. Dialog focus (PS-26).
