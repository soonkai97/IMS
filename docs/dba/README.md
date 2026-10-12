# Database performance: what was measured and what changed

Owner: DBA. Target (PRD, `docs/architecture/data-and-performance-rules.md`): **P95 < 1 s** for interactive endpoints. Read this in 5 minutes;
every number below can be reproduced with the scripts in this folder (section 8).

## 1. Result in one paragraph

At 1 million ledger rows every interactive endpoint is now under 1 s P95. Two endpoints were over: **stock/product search by SKU or barcode**
(up to 1.8 s) and the **unfiltered movements report** (1.05-1.15 s). The change set is small on purpose: **six indexes** (`AddHotPathIndexes`: four; `DocumentFlagsAndReturnInfo`: `IX_StockDocument_PostedAt`, added for the Home counts and reused by R3; `AddMovementsReportIndexes`: one), **one behaviour-identical query rewrite** (the barcode is resolved by one index seek before the list query is built, R1) and **one change of tie order** (R3, section 4: documents posted at the same instant in the movements report now appear newest document first). The previous investigation proposed 13 index changes; most were rejected or deferred because the evidence did not support them (section 5), and its baseline was taken on fragmented tables (section 6). Writes (posting) are unchanged within noise.

## 2. What was measured

| | |
|---|---|
| Data (1M tier, `results-1m-*.csv`) | 1,016,854 ledger rows, 167,313 documents, 1,068,636 document lines, 174,731 stock balances, 50,002 products and barcodes, 331,790 audit rows, 5 warehouses (`01-seed-perf.sql`, Zipf-skewed: the hottest product has 4,841 ledger rows, the median product 15) |
| Data (100k tier, `results-100k-after.csv`) | 129,711 ledger rows, 21,300 documents, 5,002 products, 3 warehouses |
| Hardware | one laptop: Intel Core i7-1355U (10 cores, 12 threads), 64 GB RAM, SSD, Windows 11; API and SQL Server on the same machine |
| SQL Server | SQL Server 2025 **Express** (LocalDB instance `ImsDev`): at most 4 cores and a small buffer pool, so absolute numbers are pessimistic for a Standard/Enterprise server |
| API | Development build of `Ims.Api`, Kestrel, rate limit raised (`RateLimit__PerMinute=1000000`), logged in as the admin user |
| Method | `02-measure-api.ps1`: per endpoint 1 untimed warm-up call, then 30 sequential timed calls on one connection (exports: 3); P95 = 29th of 30. The database was cycled offline/online and the API restarted before each run, so the buffer pool starts cold. Plans and logical reads come from the Query Store (`04-query-store-top.sql`), index use from `03-index-usage.sql` |
| Noise | Two baseline runs differ by up to 50 % on P50 and 4x on P95 for endpoints nothing touched (the first run was colder). Trust factors of 2 or more and the deterministic **logical reads**, not 10 % wiggles. `results-1m-before.csv` is the second, warm baseline; `results-1m-before-run1.csv` is the first |

## 3. Before / after at 1M ledger rows (P50 / P95 in ms; full tables in the CSV files)

| Endpoint | Before | After | Why |
|---|---|---|---|
| `/api/stock?search=SKU-00042` | 756 / 922 | **147 / 165** | barcode resolved up front (R1) |
| `/api/stock?search=9557000000042` (barcode) | 1,072 / **1,812** | **141 / 165** | R1: 1.08 M page reads become 953 |
| `/api/stock?search=bolt` | 286 / 384 | 185 / 222 | R1 |
| `/api/products?search=<barcode>` | 717 / 807 | 401 / 424 | R1 |
| `/api/products?sortBy=Name` | 97 / 140 | **4.9 / 5.3** | `IX_Product_Name` |
| `/api/audit?actor=clerk` (rare user) | 67 / 99 | **1.0 / 1.3** | `IX_AuditLogs_Actor_Id` (9,106 reads become 370) |
| `/api/audit?actor=supervisor` | 39 / 42 | 9.3 / 9.9 | same |
| `/api/documents?from=&to=` (30 days) | 14 / 24 | **3.8 / 4.9** | `IX_StockDocument_CreatedAt` (count: 3,514 reads become 25) |
| `/api/documents?type=2&status=3` | 20 / 36 | 6.8 / 12 | `IX_StockDocument_Status_Type_Id` (count: 3,514 become 261) |
| `/api/documents?status=0` | 9.8 / 12.5 | 2.7 / 4.1 | same |
| `/api/stock` (list), warehouse filter, low stock, summary, categories | 11-170 / 16-215 | unchanged | already fine |
| `/api/stock/ledger` (all variants, `afterId`, product, dates) | 1.4-62 / 2-90 | unchanged | seek paging, existing indexes |
| `/api/reports/movements` 7 days | 43-44 / 55-56 | **6-9 / 10-17** (exact final index set, slow session: 24-29 / 31-55) | R3: `IX_StockDocumentLine_DocumentId_Live` next to main's `IX_StockDocument_PostedAt` (count 7 days: 22,506 reads become 2,577). Main as merged, without the lines index, is slower than before: 880-1,010 ms in the slow session, see section 4 |
| `/api/reports/movements` 30 days | 97-98 / 107-108 | **10-18 / 13-22** (final set, slow session: 41-48 / 53-65) | same (count 30 days: 17,130 reads become 1,841) |
| `/api/reports/movements` hot product | 141-143 / 150 | unchanged (178-198 / 217-870; same plan, same reads, the 870 is a noisy run) | 17,192 reads before and after; 4,841 rows are sorted either way |
| `/api/reports/movements` **no filter** | 1,040-1,049 / **1,056-1,179** | **155-160 / 227-267** (final set, slow session: 720-1,050 / 750-1,480; main in the same session 3,540-4,850 / 4,400-7,200) | R3: the page costs 666 reads instead of 17,586 (seconds of CPU to 0); what is left is the `COUNT(*)` over 1M lines (5,751 reads) |
| Excel exports (50,000 rows) | 1.3-3.6 s | unchanged | downloads, 120 s timeout; not interactive |

At the 100k tier **everything** is under 130 ms P95 (exports up to 2.9 s).

**Write path.** k6 `write-mix` (create, submit, post), 10 VUs, 60 s, alternating the old and new index set on the same database:
3,580 and 3,606 flows before, 3,561 and 3,557 after; write P95 10 and 9 ms in both; no errors, no 409/5xx, no deadlocks. A T-SQL
micro-benchmark (5,000 transactions of 1 document + 5 lines + 5 ledger rows + posting update + audit row) shows the new indexes cost less than
the run-to-run noise (0.62-0.83 ms per transaction either way). After about 18,000 posted flows `08-reconcile-drill.sql` still finds
**0** differences between `StockBalance` and the ledger.

**Write path after R3** (the lines index sits on the table that every document write touches; the PostedAt index is main's and was measured with it). k6 `write-mix`, 10 VUs, 60 s, the lines index dropped and created in turn on the same database: with the first, quiet-machine build of R3 (own PostedAt index plus the lines index) 3,251 flows without, 3,284 with, 3,252 without (write P95 23-24 ms; no errors, no 409/5xx, no deadlocks). On the final set the machine was 3-5x slower for unchanged statements and the four runs are 929 and 1,582 flows without, 1,440 and 1,239 with, write P95 187-546 ms, still no 409/5xx and no errors: a spread of 40 % that hides any difference of a few percent. `14-write-path-bench.sql` (single-threaded, 3,000 transactions of document + 5 lines + posting update, 4 rounds alternating) gives best 1.31 ms per transaction with the lines index and 1.25 ms without, averages 1.39 and 1.80: the cost is below the noise. After about 5,000 further posted flows `08-reconcile-drill.sql` finds 0 differences between `StockBalance` and the ledger. (`tests/load/scenarios.js` read `id` from `/api/products/lookup`, which now answers `{ found, product }`; fixed, otherwise every scenario stopped in `setup()`.)

**Concurrency.** `read-mix` is closed-loop (no think time) and saturates at about 45 requests/s on this Express instance: P95 306 ms at
5 VUs, 596 ms at 10, 1.24 s at 20. That is a capacity figure, not a user count (see `tests/load/README.md`).

## 4. What is recommended and applied

Migration `20261010025752_AddHotPathIndexes` (model change in `AppDbContext`; `Up` took 3.8 s at 1M rows; `Down` restores the old index):

| Index | Serves | Evidence |
|---|---|---|
| `IX_AuditLogs_Actor_Id (Actor, Id DESC)` | audit page and count filtered by user | rare user 4,558 reads to 102; 67 ms to 1 ms; 124 seeks in `03-index-usage.sql` |
| `IX_StockDocument_Status_Type_Id (Status, Type, Id DESC) INCLUDE (WarehouseId) WHERE REC_DELETED = 0` | document list/count filtered by status and/or type, newest first. **Replaces** `IX_StockDocument_Type_Status_WarehouseId` (dropped, no net size change) | count 3,514 to 261 reads; 155 seeks/scans; old index unused afterwards |
| `IX_StockDocument_CreatedAt (CreatedAt) WHERE REC_DELETED = 0` | document list/count by date range | count 3,514 to 25 reads; 14 to 3.8 ms |
| `IX_Product_Name (Name) INCLUDE (Sku, IsActive) WHERE REC_DELETED = 0` | product list sorted by name; narrower scan for the name/SKU search | page 1,116 to 364 reads; 97 to 4.9 ms; 310 scans |

Rules the four follow (and a new index should too): indexes on soft-deleted tables are **filtered `WHERE [REC_DELETED] = 0`**, because EF adds
`REC_DELETED = CAST(0 AS bit)` to every query and the filtered index answers it for free (`10-filtered-vs-unfiltered.sql`: 289,143 reads
without the filter, 261 with it); the append-only tables (ledger, audit) have no query filter and get unfiltered indexes; every new index
must show up with reads in `03-index-usage.sql` after a realistic run, otherwise it is write cost for nothing. All four showed seeks or scans.

**R1, implemented: resolve the barcode before the list query** (`ProductSearch`, `ProductIdByBarcodeSpec`, `StockFilters`, `ProductSearchFilter`).
`sku LIKE OR name LIKE OR EXISTS(barcode)` in one predicate made SQL Server probe the barcode table once per product: 527,412 page reads and
1.1-1.7 s for a stock search. One `Barcode = @x` seek (barcodes are unique among live rows) turns the third arm into `Id = @productId`:
953 reads, 126 ms CPU. The rows returned are identical: 102 stock/product requests (14 search texts, warehouse, low-only, sort, paging,
inactive) were compared old API against new API on the same database with **0 differences**. It applies to `/api/stock`, the stock summary
export and `/api/products`.

**R3, implemented: a total order for the movements report, and one more index** (product owner approved the tie order). Migration
`AddMovementsReportIndexes` (`Up` creates the index in 1-2 s at 1M ledger rows, `Down` drops it; tested up, down, up). The report relies on two
indexes; the first already exists on main:

| Index | Serves | Evidence (`12-movements-order.sql`, 1M ledger rows) |
|---|---|---|
| `IX_StockDocument_PostedAt (PostedAt) INCLUDE (Status, WarehouseId) WHERE REC_DELETED = 0 AND PostedAt IS NOT NULL` (migration `DocumentFlagsAndReturnInfo`, 4 MB, ascending) | the report page: a backward scan yields `PostedAt DESC, DocumentId DESC` and stops after the first rows | unfiltered page 4,588 reads and 750 ms CPU (session on a slow machine) become 666 reads and 0 ms; warehouse-scoped user 4,475 reads to 550; the sort no longer grows with the number of documents. It serves the report only because the order ends in `DocumentId DESC` and the query repeats `PostedAt IS NOT NULL` |
| `IX_StockDocumentLine_DocumentId_Live (DocumentId) WHERE REC_DELETED = 0` (migration `AddMovementsReportIndexes`, 14 MB; the unfiltered foreign-key index stays, declared explicitly in `AppDbContext` or EF would drop it as redundant) | the report **count**, and the plan of the page: EF puts `REC_DELETED = 0` on the lines, which the foreign-key index cannot answer, so every count read the whole lines table | count unfiltered 17,130 reads to 5,751; 30 days 17,130 to 1,841; 7 days 22,506 to 2,577. Without it the PostedAt index **makes the 7-day report slower than before** (count 13,210 reads and a hash join over all 1M lines: 43 ms before, 130-230 ms with the index alone, and 880-1,010 ms in the slow session where the final set takes 24-29 ms) |

What changed in the order. Before: `ORDER BY PostedAt DESC, line.Id ASC`. Now: `ORDER BY PostedAt DESC, DocumentId DESC, line.Id ASC`
(`MovementOrder`, used by the page, the total and the export alike, so pages and export always agree). Only rows whose documents share
**exactly the same PostedAt** can move: two such documents now appear newest document id first (before: whichever had the lower line ids,
which is mostly the older document first), and the lines of each document stay together in entry order, as before. Documents with different
PostedAt, the lines of one document, the filters, the totals and the cursor/seek paging of the ledger (`/api/stock/ledger`, `afterId`) are
unchanged. Real postings take the clock's instant, so ties are rare in production (bulk imports, seed data); `13-movements-order-diff.sql`
counts them on any database: in the perf seed 38 PostedAt values are shared by 76 documents and 197 of the 1,070,375 report rows (0.02 %) change position. The report also states `PostedAt IS NOT NULL` (true for every posted document), because a filtered index is only
used when the query repeats its predicate.

The movements report has no cursor of its own: it pages by OFFSET (capped at 10,000 rows) and always counts. With the total order above a walk
through the pages never skips or repeats a row (tested at page sizes 1, 2 and 3 across a tie). The remaining cost at 1M ledger rows is the
count, which is O(lines); an `includeTotal=false` switch like the ledger's would remove it if the unfiltered report ever has to be faster.

**4b. Movements report for one product: a plan of its own per call** (found in the review of PR 70, item M3: `?productId=<cold product>` cost 2.4M reads and
1.3-1.5 s at 450,000 lines; present on main since R3). **Root cause: parameter sniffing, not a missing index.** The statement has one cached plan for every
`ProductId = @p`. For a product with thousands of lines the cheapest plan walks the documents by `PostedAt` (newest first) and stops after 50 matches (4,352 reads
for a product with 5 % of the lines). SQL Server compiles it for whichever product arrives first after a restart, a plan-cache clear or a statistics update; every
cold product then walks every document looking for matches. With a plan of its own each product is cheap: the optimizer seeks `IX_StockDocumentLine_ProductId`
for a product with few lines and walks `PostedAt` for a hot one. Measured with `17-movements-by-product.sql` (the page statement through `sp_executesql`) and
`18-measure-movements-by-product.ps1` (the API, page plus both counts, reads from the Query Store), 1M tier (1,051,488 lines, uniform over 20,000 products, plus products 3, 4 and 5
with 50,045, 10,050 and 3,044 lines to get the plan flip; the Zipf seed alone, hottest product 0.5 %, only reaches 4,000 reads because its shared plan is a hash join):

| Call (API, reads of page + counts) | Before, plan compiled by the hot product | After (`OPTION (RECOMPILE)`) |
|---|---|---|
| cold product, 0 lines | **4,212,875** reads, 990-2,200 ms | 18 reads, 40-80 ms |
| cold product, 49 lines | **4,274,995-4,301,095** reads, 1,650-4,030 ms | 1,049 reads, 60-220 ms |
| cold product, 81 lines | **2,377,568-2,459,166** reads, 870-3,800 ms | 1,469 reads, 75-390 ms |
| 3,044 lines, plan shared with a hot product | 113,333 reads, 124-270 ms | 25,076 reads |
| 10,050 lines | 45,350 reads, 100-245 ms | 47,144 reads |
| 50,045 lines (5 % of the table) | 27,905 reads, 500-600 ms | 27,887 reads (page 4,392 + the counts) |
| hot product when a cold product compiled the plan | 639,356 reads, 250-660 ms | 26,917 reads |
| default page, no filter (untouched) | 8,837-9,076 reads, 140-310 ms | 8,837-9,076 reads (same code path) |

The wall times of one call are noisy on this shared machine (the same call differs 2-4x between sessions, and a first call includes JIT); the logical reads are exact.
Server-side, `17-movements-by-product.sql` (`results-1m-movements-by-product-plan.txt`): 0 lines 4.03M reads and 2,550 ms with the shared plan, 12 reads and 53-64 ms with
`RECOMPILE` (the compile is most of that: 50-130 ms of CPU per statement; a normal plan costs 0-2 ms to run for a cold product).

Fix: `EfMovementReader` tags the page and both counts with `TagWith(RecompileInterceptor.Tag)` **only when a product filter is present**; `RecompileInterceptor`
(registered on the context) turns the tag into `OPTION (RECOMPILE)`. No schema change, no migration. The default report and every other filter keep the cached plan. The price
is one compile (about 50-130 ms of CPU) for the page, and a little for each count, per product-filtered request; that report is a supervisor's drill-down, not a polled
screen. If it is ever called hundreds of times per second, replace the hint by a plan chosen from the product's line count (two statement texts).

Rejected: an index. `IX_StockDocumentLine_ProductId_Live (ProductId) INCLUDE (DocumentId) WHERE REC_DELETED = 0` (candidate measured on the 1M Zipf database, all products,
fresh and shared plans): reads within 2 % of the existing foreign-key index for every product, and the shared-plan problem is the same, because the cost is in the plan the
cache hands out, not in the access path. It would add a write to every document line insert for nothing (not measured: not shipped). The other alternative, one plan for a
typical product (what `OPTIMIZE FOR UNKNOWN` gives; the "cold-first" scenario of `17-movements-by-product.sql`): cold products are cheap, but a product with 5 % of the lines
reads 601,236 reads (666 ms) instead of 4,392 for the page, and the cost grows with the table.

## 5. Rejected proposals (the previous investigation proposed 13 index changes; evidence decides)

| Proposal | Verdict | Reason |
|---|---|---|
| `IX_StockLedgerEntry_ProductId_Id (ProductId, Id DESC) INCLUDE (WarehouseId)` | reject | the hottest product has 4,841 ledger rows and sorts in 2.8 ms; the mid-range product page read more (30,352 to 43,983 reads, 11 to 17 ms); +21.7 MB on the hottest write path. Revisit when one product exceeds about 300,000 ledger rows |
| `IX_StockDocument_Open` (filtered `Status IN (0,1,2)`) | reject | the API sends `Status = @p`; a filtered index with an IN list cannot be matched by a parameter, so it is never chosen (tested). `Status_Type_Id` already serves drafts in 3 ms |
| `IX_StockDocument_PostedAt` (as proposed) | exists on main, used by R3 | PR 18 added it (ascending, `INCLUDE (Status, WarehouseId)`, filtered on live rows and `PostedAt IS NOT NULL`) for the Home counts. Alone it does nothing for the movements report under the old order (unfiltered 15,272 to 10,374 reads, same time); with the new order it removes the sort (section 4), and without the lines index it slows the date-filtered reports |
| `IX_StockDocumentLine_DocumentId_Cover (DocumentId, Id) INCLUDE (ProductId, Qty, UomId, BaseQty, UnitCost)` (36 MB) | reject (revisit below) | with it the hot-product page drops from 17,192 to 11,076 reads and the export from 202,000 to 49,000 reads; but the report is already 6-200 ms P95 without it, the index adds 36 MB and upkeep on the hottest write path (line costs change at posting), and the count it improves is served just as well by the 14 MB `_Live` index (count unfiltered 6,892 vs 5,751 reads). Revisit if one product exceeds about 30,000 posted lines (its report is linear in that product's lines: 150 ms at 4,841) or the 50,000-row export has to be faster. It would also have replaced the foreign-key index, which a physical delete of a document needs (scan of the lines table otherwise); `_Live` is therefore added beside it |
| `IX_StockBalance_QtyOnHand` | defer with R2 | the sort statements read the same 2,984 pages with and without it; the gain only appears with the tie-break change |
| `IX_Product_Sku` rebuilt with 4 INCLUDE columns | defer with R2 | the stock-list statements read the same 3,081 pages with and without it |
| `IX_Product_LowStock` (filtered partial) | reject | the low-stock statements read the same 2,881 pages and ran slower (92 to 170 ms); 2.6 MB of upkeep |
| `IX_AuditLogs_EntityType_Id` | reject | the entity-type filter is already served by `IX_AuditLogs_EntityType_EntityId` (rare type 1.8 vs 2.4 ms, common type 22 vs 21 ms); +14 MB |
| `IX_RefreshToken_ExpiresAt` | reject | no query or job seeks on `ExpiresAt` (there is no purge job); it would only slow every login and refresh. Add it together with the purge job |
| Drop 7 FK-only indexes (ledger/document `CreatedBy`, `SubmittedBy`, `ApprovedBy`, `PostedBy`, line `UomId`, product `BaseUomId`) through a custom EF convention | reject | they have zero reads, but dropping them saved about 10 % of a micro-benchmark (0.60 vs 0.66 ms per posting transaction, inside the noise), nothing end to end, and 43 MB; the price is a replaced EF convention that must follow every EF upgrade and table scans on any physical delete of a user or unit. Revisit under space pressure |

## 6. Findings that are not indexes

- **Rebuild after column-adding migrations.** The `AuditColumns` migration left every big clustered index 85-100 % fragmented at 50-75 %
  page density (maintenance log of the previous run at 1M rows). A baseline taken in that state is not comparable: the previous
  investigation measured the stock list at 601 ms, while the same schema and data give 166 and 251 ms in my two baseline runs after
  `07-maintenance.sql` (about 10 seconds for all tables at 1M rows). Run the script right after any migration that adds columns to a big
  table, and always rebuild before taking a "before" measurement.
- **The COUNT runs before the paging check.** `GET /api/stock/ledger?page=1000&pageSize=500` is refused with 400 (more than 10,000 rows
  deep) but only after a `COUNT(*)` over the ledger: 54 ms P50 at 1M rows for nothing. Validate with `Paging.Normalize` first.
- **Unused today** (kept, they are foreign-key or planned-feature indexes): `IX_AuditLogs_At`, `IX_AuditLogs_EntityType_EntityId` (31.7 MB; nothing
  filters by `EntityId` until an entity-history view exists), `IX_StockDocument_PartnerId` and `_WarehouseId`, `IX_StockBalance_WarehouseId`,
  `IX_StockDocumentLine_ProductId` and `_UomId`, `IX_Product_CategoryId`. Decide when the audit viewer and warehouse reports are final.
- **Concurrency invariants held.** 10 VUs of write-mix (about 3,500 posted flows a minute, write P95 9-10 ms) and a 50-VU burst of 150 postings
  on the same 5 hot products (write P95 698 ms, flow P95 1.4 s, 150 of 150 ok): no deadlocks (system_health and ring buffer), no 409, lock waits
  only where the hot rows are serialised on purpose (287 waits, 53 s in total across the burst), and stock equals the ledger afterwards.

## 7. Recommendations that need a decision (not applied: they change the order of rows that tie or the matching rule)

R3 (movements report) was decided and shipped, see section 4. These remain:

Measured with `09-rewrite-candidates.sql` (the exact EF statement versus the rewrite; 1M ledger rows):

| | Today | Rewrite (plus the deferred indexes) | What changes |
|---|---|---|---|
| **R2** stock list `ORDER BY Sku, Warehouse.Code` | 3,056 reads, 254 ms | `ORDER BY Sku, Id, WarehouseId` + unique covering `IX_Product_Sku`: 1,275 reads, **24 ms** | rows of one SKU appear by warehouse id instead of warehouse code |
| **R2b** stock sort `QtyOnHand DESC, Sku` | 3,153 reads, 380 ms | `QtyOnHand DESC, ProductId, WarehouseId` + `IX_StockBalance_QtyOnHand`: 460 reads, **0 ms** | equal quantities appear by product id instead of SKU |
| **R4** document search `No LIKE '%x%'` | 961 reads, 88 ms | `LIKE 'x%'`: 23 reads, 4 ms | contains becomes starts-with (UI wording) |

Owners: be-framework to apply R2 in `StockRowsSpec` and the DBA to ship the matching migration in the same change (as was done for R3);
the product owner to accept the tie order. Also noted: a `Contains` search is a scan by nature (50,000 products, about 150-400 ms);
past a few hundred thousand products move to prefix search or full-text.

## 8. Maintenance plan and recovery drills

| What | When | How | Measured (1M tier) |
|---|---|---|---|
| Index and statistics maintenance | weekly, and right after any migration that adds columns | `07-maintenance.sql` (`Execute=0` prints the plan): fragmentation 5-30 % reorganize, more than 30 % or density under 70 % rebuild; `FILLFACTOR` 100 on append-only tables, 90 on random keys; then refresh statistics with more than 5 % changes. Rebuilds are offline (schema lock) on Express/LocalDB/Standard; the script adds `ONLINE = ON` on Enterprise, Developer and Azure SQL | 4.4 s for the 7 indexes that needed it |
| Full backup, verify, restore, CHECKDB | nightly full + log backups every 15 minutes in production (FULL recovery); drill each release | `05-backup-restore.sql` (backs up, `RESTORE VERIFYONLY`, restores under a new name, `DBCC CHECKDB`, compares row counts, trigger, RCSI) | backup 5.8 s (786 MB, no compression on Express), verify 1.0 s, restore 3.0 s, CHECKDB 8.4 s, **all clean**, 1,069,217 ledger rows equal, append-only trigger and `READ_COMMITTED_SNAPSHOT` preserved |
| Balance vs ledger reconciliation | after any restore or incident | `08-reconcile-drill.sql`: verify `SUM(ledger)` = `StockBalance` (0.54 s, 0 mismatches) and dry-run rebuild (0.66 s for 180,062 rows) | the real rebuild must replay the ledger in `Id` order for the moving-average cost |
| Files | with every database backup | the attachment folder (`Storage:RootPath`) | not part of the SQL backup |
| Sizes | monthly | `06-sizes.sql` | database 1.1 GB data at 1M ledger rows |

## 9. Re-run the measurements

Use a throw-away database whose name starts with `Ims_Perf` (the scripts refuse any other name). From the repository root, PowerShell:

1. `$env:ConnectionStrings__Default = 'Server=(localdb)\ImsDev;Database=Ims_Perf;Trusted_Connection=True;TrustServerCertificate=True'; $env:ASPNETCORE_ENVIRONMENT = 'Development'`
   then `dotnet ef database update --project src/Ims.Infrastructure --startup-project src/Ims.Api` (creates the schema).
2. Start the API once (`$env:ASPNETCORE_URLS = 'http://localhost:5187'; $env:RateLimit__PerMinute = '1000000'; dotnet run --project src/Ims.Api -c Release --no-launch-profile`) so the demo users are seeded, then stop it.
3. Seed: 130k tier `sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -b -W -v Products=5000 Docs=21000 Days=360 Warehouses=3 -i docs/dba/01-seed-perf.sql` (6 s);
   1M tier `-v Products=50000 Docs=161500 Days=1080 Warehouses=5` (a few minutes).
4. Optional: turn the Query Store on (command in `04-query-store-top.sql`), start the API again, then
   `powershell -File docs/dba/02-measure-api.ps1 -Label mine -BaseUrl http://localhost:5187` (about 15 minutes at 1M; `-Only 'stock|audit'` runs a subset).
5. `powershell -File docs/dba/compare-results.ps1 -Before docs/dba/results-1m-before.csv -After docs/dba/results-mine.csv` (adds `**>1s**` where P95 exceeds 1 s).
6. Evidence: `sqlcmd ... -i docs/dba/03-index-usage.sql`, `04-query-store-top.sql`, `09-rewrite-candidates.sql` (+ `summarize-stats.awk`).
7. To compare with an older index set: `dotnet ef database update 20261010025752_AddHotPathIndexes` (before R3) or `20261009151059_AuditColumns` (before everything), measure, then update again. R3 evidence: `12-movements-order.sql` (it creates and drops its own candidates) and `13-movements-order-diff.sql`.
8. Load tests: `tests/load/README.md`.

## 10. Files

| File | Purpose |
|---|---|
| `01-seed-perf.sql` | set-based volume seed (130k, 1M or 2M ledger rows) |
| `02-measure-api.ps1`, `compare-results.ps1` | endpoint latency probe and before/after table |
| `03-index-usage.sql`, `04-query-store-top.sql` | which indexes are read and what they cost; the real EF statements, worst first |
| `05-backup-restore.sql`, `06-sizes.sql`, `07-maintenance.sql`, `08-reconcile-drill.sql` | recovery and upkeep (section 8) |
| `09-rewrite-candidates.sql`, `summarize-stats.awk` | EF statement versus rewrite (R1, R2, R4) |
| `12-movements-order.sql`, `13-movements-order-diff.sql`, `14-write-path-bench.sql` | R3: old versus new order against five index sets, page, count and export (reads, CPU); which report rows change position; what the two indexes cost per posting transaction |
| `17-movements-by-product.sql`, `18-measure-movements-by-product.ps1`, `results-1m-movements-by-product-*` | section 4b: the report for one product with a shared plan versus a plan per call (statement-level reads and times; the API with Query Store reads) |
| `10-filtered-vs-unfiltered.sql` | why the indexes are filtered on `REC_DELETED = 0` |
| `11-snapshot.sql`, `11b-snapshot-diff.sql` | deadlock and lock-wait counters around a load test (used by `tests/load/run-scenario.ps1`) |
| `results-1m-before.csv`, `results-1m-after.csv` | the comparison in section 3 (`-before-run1` = first, colder baseline; `-full-proposal` = all 13 proposed index changes applied) |
| `results-100k-after.csv` | the 130k tier on the final code |
| `results-1m-movements-before.csv`, `results-1m-movements-after.csv` | R3 (section 4), re-measured on a fresh 998,184-ledger-row database of the same seed (161,800 documents, 1,051,964 lines): `mov-base*` and `fin-after*`: a first, quiet session (two baseline runs; the two quietest of five interleaved after runs; exports best of three rounds; there the PostedAt index was R3's own, same key and INCLUDE minus `Type`). `q-main*`, `q-newcode-noLive*`, `q-r3*`: the final index set on a fresh database, main as merged against the new code without and with the lines index, three interleaved rounds, in a session where unchanged statements ran 3-5x slower. The machine was shared with other test runs and antivirus scans for most of the session, so P95 of the other rounds is 2-10x higher on both sides; the logical reads in section 4 are exact |
