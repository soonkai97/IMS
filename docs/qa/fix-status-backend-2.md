# Backend fix status for the supervisor re-test (2026-10-11)

Report: `retest-power-supervisor.md` (PR-01 .. PR-14, and the old PS findings it re-checked). The report itself is not edited. First round: `fix-status-backend.md`.

Delivered as independent pull requests, each based on `main`. The average-cost fix (PR-01, #44) and the money-precision rule (#45) have their own pull requests and their own status in them; this list covers the findings of this branch only.

| PR | Branch | Holds |
|---|---|---|
| - | `feature/qa-backend-fixes-5` (replaces `feature/avg-cost-and-money-precision-3`) | PR-04, PR-05, PR-08, PR-10, PR-11, PS-12, PS-14, this list (migration `AddDocumentReversedBy`; the QA review of the first version is fixed in `feature/qa-backend-fixes-5`) |

Legend: **fixed** = done and covered by a test; **not done** = no change made, reason given; **needs decision** = waits for a product answer; **UI** = the frontend owns it (the contract it relies on is in `docs/team/HANDOFFS.md`, "Contract changes for the frontend").

## New findings (PR)

| ID | Status | Notes |
|---|---|---|
| PR-02 | needs decision | Q3: Cancel (and Reject back to Draft) on an Approved document for Supervisor and Admin, a shortage warning in the Approve dialog. No backend change until decided; `lines[].available` against `baseQty` is already in the DTO. |
| PR-03 | UI | The API already answers `400` "The 'from' date is after the 'to' date." under `errors.to` on every report endpoint (tested in `InputValidationTests`); the screen must map it. |
| PR-04 | fixed (PR 3) | One definition, `Ims.Domain.LowStockRule`, used by the low-stock list and export, the Home count, the badge, the stock list with `lowOnly=true` and its export, and the `isLow` flag of a stock row. `GET /api/stock?lowOnly=true` now includes the active products with a safety level that have no balance row in ANY active warehouse the caller can see: each is listed ONCE, with no warehouse (`warehouseId` null), `qtyOnHand` 0 and `avgCost` and `value` null, not once per warehouse. Only the caller's warehouses count, so a scoped caller learns nothing about stock elsewhere and the answer does not change when a warehouse they cannot see receives the product. A warehouse filter leaves these products out. So it has exactly the rows of `GET /api/stock/low-stock` (documented on `StockFilter` and `StockRow`). `isLow` is false for an inactive product and in an inactive warehouse, as on the low-stock list. **Low means on-hand at or below the safety level** (design 3.8: "on-hand <= safety stock"; the first version said "equal is not low"). The sets are built once in `LowStockSets`; the `isLow` projection asks `LowStockRule` for its verdict. Tests: `LowStockNeverStockedTests`, `LowStockOneDefinitionTests` (set equality per warehouse and in total, the Home count, both exports, row shape, filters and sorts on both kinds of row, paging, warehouse scope, the flag) and `LowStockRuleTests`. |
| PR-05 | fixed (PR 3) | The documents search finds the number (whole or part), the remark and the partner name, case-insensitive; `%`, `_` and `[` are plain text. Every matching partner is found, however many match (up to 50 matching partners as an id list, more through a join, both with the same case-folded comparison as the number and the remark; nothing is capped or refused); the comparison is case-folded and binary (about ten times less CPU than the linguistic collation); a text of fewer than 3 characters is a 400 "Type at least 3 characters."; no search index (measured at 1M documents in `docs/architecture/data-and-performance-rules.md` section 9). Test: `DocumentSearchTests`. |
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
| PS-14 | fixed (own PR) | `LedgerRow.balanceAfter` on `GET /api/stock/ledger?productId=...` is stored on the ledger row when it is written (migration `AddLedgerBalanceAfter`). It is in its own pull request, `feature/ledger-balance-after` (it needed a migration that rewrites the whole ledger, which is better reviewed and scheduled alone). |
| PS-15 | UI / needs decision | Nothing is left on the backend: `from` after `to` is a `400` (above), signed quantity and the export are done. The date format is a decision; the empty state and the export message are UI. |
| PS-20 | not reproducible | |
| PS-22 | UI | Wrapping of SKU and date in the Reports table (CSS). |
| PS-26 | needs decision | Dialog focus. |

## Items that wait for a decision (not done)

1. Q3: Cancel on an Approved document (PR-02, PS-11).
2. Cost of stock received by Warehouse staff (PR-07).
3. Date format of the date inputs (PS-15, NV-27).
4. Paging controls (PS-13).
5. Dialog focus (PS-26).
