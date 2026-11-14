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
`RECOMPILE` (the compile is most of that). Those 50-130 ms were the quietest session; the review of this change (PR 71) measured the real cost on a busier machine:
**94-300 ms of CPU for a cold product and 350-800 ms for a product with 2-5 thousand lines**; a cold product through the API takes 0.12-0.45 s instead of 0.02-0.03 s when
the shared plan happens to be good; 24 concurrent requests gave a median of 0.71 s. The trade is right (the worst case goes from 1-9 s to under 1 s), but the extra CPU per
product-filtered request is real.

Fix: `EfMovementReader` tags the page and both counts with `TagWith(QueryHint.Recompile)` **only when a product filter is present**; `QueryHintInterceptor`
(registered on the context) turns the tag into `OPTION (RECOMPILE)`. No schema change, no migration. The default report and every other filter keep the cached plan. The price
is one compile (0.1-0.8 s of CPU, see above) for the page, and a little for each count, per product-filtered request; that report is a supervisor's drill-down, not a polled
screen. If it is ever called often, replace the hint by a plan chosen from the product's line count (two statement texts). `QueryHintInterceptor` (renamed from `RecompileInterceptor` in 4d, it now knows two hints) leaves a command that already
ends with the hint alone. EF's Command log shows the tag (`-- ims:recompile`) but not the hint (found in the PR 71 review): to see the hint, read the statement in the Query Store or the plan cache. `MovementsByProductPlanTests` sends the report through the real host and asserts the hint on the
statements that reach the server, so removing the registration in `AddInfrastructure` fails it.

Rejected: an index. `IX_StockDocumentLine_ProductId_Live (ProductId) INCLUDE (DocumentId) WHERE REC_DELETED = 0` (candidate measured on the 1M Zipf database, all products,
fresh and shared plans): reads within 2 % of the existing foreign-key index for every product, and the shared-plan problem is the same, because the cost is in the plan the
cache hands out, not in the access path. It would add a write to every document line insert for nothing (not measured: not shipped). The other alternative, one plan for a
typical product (what `OPTIMIZE FOR UNKNOWN` gives; the "cold-first" scenario of `17-movements-by-product.sql`): cold products are cheap, but a product with 5 % of the lines
reads 601,236 reads (666 ms) instead of 4,392 for the page, and the cost grows with the table.

**4c. Public id switch (`feature/public-ids-integration`, PR #78), verified at the 1M tier** (GUID plan PR-10, DBA part; raw numbers in `results-1m-public-ids.txt`).
Throw-away database seeded on the schema **before** `AddPublicIds` (1,000,682 ledger rows, 161,800 documents, 50,002 products), migrated, then loaded. Because the laptop changed speed
2-5x several times during the run, every comparison is **old build against new build on the same database, the same rows and the same minutes**: `origin/main` (integer ids on the wire,
with #71) and the integration branch, both pointed at the migrated database, called alternately (`22-interleaved-ab.ps1`), reads from the Query Store (`19-reads-by-endpoint.ps1`).

| Question | Result |
|---|---|
| Migration `AddPublicIds` at this tier | **7.6 s** for the nine tables (162k documents, 50k products); log 157 MB used (file did not grow); data file did not grow; no row of the ledger, lines or audit log is touched. Clustered keys after: 0.0 % fragmented. The ADR's 1M-document figure (20 s) scales with the document count, not with the ledger |
| Reads of the 72 endpoints, main vs new | **No endpoint reads more than +9 rows more than before; none regresses by 20 % except where the base is tiny:** by-id endpoints +5 or +6 reads (the seek on `UX_<Table>_PublicId`): `documents/{id}` 47 -> 53, `stock?productId` 53 -> 58, ledger by source document 19 -> 25, `users` 19 -> 25, product by id 25 -> 31, attachments by owner 9 -> 14. Lists, search, reports, audit, exports: unchanged to 0.5 %. Fewer reads: product deep page 44,029 -> 29,655, documents deep cursor 1,183 -> 789 |
| By-id reads, same rows by int key and by GUID (`21-by-id-reads.ps1`) | document 191 -> 197, product 25 -> 31, ledger of a hot product 885 -> 890, movements of a hot product 35,562 -> 35,567, movements of a mid product 3,537 -> 3,542; median wall time equal within 10 % |
| Latency, 72 endpoints, interleaved | p50 ratio new/main 0.7-1.25 everywhere (`results-1m-ab.csv`); the only p95 ratios above 1.2 are endpoints under 5 ms (noise) and xlsx exports measured 3 times (re-measured 8 times: equal) |
| Write path: k6 `write-mix` 10 VUs 60 s, main and new alternately on one database | main 2,106 and 2,983 flows, write P95 104 and 38 ms; new 2,738 and 2,697 flows, write P95 64 and 72 ms; 0 failed flows, 0 x 409, 0 x 5xx, 0 deadlocks. Before the migration (pre-switch code): 2,705 flows, P95 72 ms. `mixed` (10 VUs): new 239 flows, read P95 1.5 s, write P95 148 ms; main 214 flows, 2.2 s, 270 ms (saturated closed loop) |
| Cost of `UX_StockDocument_PublicId` on a write (`20-publicid-write-bench.sql`, 3 x 3,000 transactions of document + 5 lines + post) | with the index best 0.646 ms/tx, without 0.733 ms/tx: **not measurable** |
| Reconcile drill (`08-reconcile-drill.sql`) after about 23,000 flows in all | **0 mismatches** in checks 1, 3 and 4 (1,059,012 ledger rows) |
| Movements by product (#71) | still holds: cold product 187-245 reads and 40-60 ms whichever plan came first, hot product 34,787-40,723 reads and 135-235 ms |

**Fragmentation of the nine `UX_*_PublicId` indexes (random v4 GUIDs).** Only `UX_StockDocument_PublicId` receives inserts that matter, and it is small (5 MB at 188k documents; the ADR's figure is 28 MB per million).
Fresh from the migration: 0.0 % fragmented, 90 % full. After +14,000 documents (+8 %): **81.7 % fragmented, 59.5 % full, 1,054 pages instead of 619**. With 100,000 single-row inserts (`20-publicid-write-bench.sql`): 10.8 % after +10k, 95 %
after +20k, 99 % from +30k on; the page count doubles once (733 -> 1,468) and then the pages fill up (79 % full at +100k). The clustered key `PK_StockDocument` stays at 0.4 % in the same run, and the clustered keys of the ledger,
the lines and the audit log are at 0.2-0.5 % (were 0.0-0.2 %): the switch did not make any clustered index worse. The other eight UX indexes are one page (tables with few rows) or have no inserts (`Product`: 0.0 %). The only read on these
indexes is a point seek (+5 or +6 reads per by-id call, measured above on the 82 % fragmented index); nothing scans a range of them, so the fragmentation costs space and some log, not read time. **`07-maintenance.sql` covers it**:
the script ranks by size, ignores indexes under 1,000 pages (every UX index except `StockDocument`'s at this tier), and for a bigger one rebuilds it at `FILLFACTOR = 90` (its `UX_%_PublicId` rule); the post-load plan lists
`UX_StockDocument_PublicId` (REBUILD, 1,054 pages), and a rebuild of an index that size takes well under a second. No change needed; keep the weekly run.

**Found on the way, present on `main` before the switch (reported, not fixed here; they need a `be-framework` change):**

1. **`GET /api/documents` plan poisoning (Major).** The page statement is `TOP(@p)` over a correlated sum of the lines. Compiled for a page size of 150 or more (the API accepts 200) the optimizer picks a hash plan that scans
   `StockDocumentLine` twice (44,624 reads, 3.3 s CPU); the plan is cached and **every** later call, page size 25 included, takes 3.3-5.2 s until it leaves the cache (restart, statistics update). Compiled for 25-100 it takes 590-4,812 reads
   and 20-120 ms at any size. It happened by itself in this run (a statistics update after the first load; the next k6 setup called with 200) and the web app, which sends 25, would then show 4 s lists. Evidence:
   `results-1m-public-ids.txt` section 8. **Minimal fix:** one more tag for `RecompileInterceptor` that appends `OPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))` to the documents page query (tested on the real statement: fresh compile for
   200 then 25 = 137 ms then 2 ms, for 500 = 181 ms then 3 ms); `OPTION (RECOMPILE)` alone does not help, the large page's own plan is the bad one.
2. **`GET /api/products/lookup?code=` (document line picker) grows with the product's position (Major).** `Sku == code || Barcodes.Any(...)` with `TOP(1)` is answered by walking the `Product` clustered index: 673 reads and 5-9 ms
   for `SKU-00042` (the only code `02` asks), **average 963 ms and up to 2.0 s over 40 random SKUs**, 79,000 reads per statement and three statements per call. P95 over the catalog is above 1 s. **Minimal fix:**
   resolve the SKU first (unique index seek), the barcode only if nothing matched (the same two-seek shape R1 gave the stock search); no schema change.
3. `GET /api/audit?actor=supervisor`: 497k reads, 0.3 s quiet and 0.9-1.0 s P95 in a noisy session when the actor's rows are old (164k of them here); equal on `main`. Watch item (`IX_AuditLogs_Actor_Id` exists; the plan walks the
   key backwards through the newest rows until it has a page of that actor).
4. `GET /api/stock` (no filter) scans and sorts all balance rows for every page: 195-200 ms in a quiet session, 1.2-1.4 s when the CPU slowed down 5x. Equal on `main`; the most sensitive endpoint to a busy machine.

**P95 verdict at 1M ledger rows.** The public id switch does not move any interactive endpoint: reads +0 to +9 per call, latency equal to `main` within the noise of an interleaved comparison, write path unchanged, reconcile 0, the migration takes seconds.
In the interleaved pass (`results-1m-ab.csv`, both builds in the same minutes) every endpoint except the exports (downloads) is under 1 s P95 on both builds, with one exception: `audit: actor=supervisor` (0.89 s on `main`, 1.02 s new, item 3).
The single sequential runs of `02` (`results-1m-main.csv`, `results-1m-public-ids.csv`) show P95 above 1 s for both builds during slow stretches of the laptop, always on endpoints that read 50-175k rows per call
(`main`: stock sorted by warehouse 2.1 s; new: product search 3.0 s): the machine, not the code (the same endpoints are 0.2-0.5 s in the interleaved pass). Over 1 s and **not caused by the switch**: item 1 after a poisoned plan, item 2 for most SKUs.
Items 1 and 2 should be fixed before the 1M tier is called green.

Scripts added for this section: `19-reads-by-endpoint.ps1` (reads per call from a `02` result file), `20-publicid-write-bench.sql`, `21-by-id-reads.ps1`, `22-interleaved-ab.ps1`.
Caveat: the k6 A/B rounds (`write-mix`, `mixed`) used `PRODUCT_DRAWS=60 DOC_PAGES=30`: the default setup walks 100 document pages and about 250 product look-ups, which hit items 1 and 2 and exceeded k6's 300 s `setupTimeout` on a slow day
(the first attempts timed out). `write-hot` and `read-mix` ran only on the pre-switch code, before the migration, and the ramp, spike and soak scenarios were not run (`results-1m-public-ids-k6.csv`).

**4d. Documents list and product lookup at the 1M tier** (two P95 > 1 s defects found by the verification of the public id switch, PR 81; present on main; raw numbers in
`results-1m-documents-lookup.txt`, scripts `23-measure-documents-list.ps1` and `24-measure-product-lookup.ps1`). No schema change, no migration: the indexes the lookup needs
(`IX_Product_Sku`, `IX_ProductBarcode_Barcode`, both unique and filtered on `REC_DELETED = 0`, matching the filter the global query filter adds) already exist.

*Documents list.* Root cause: parameter sniffing on `TOP(@p)`. The page statement has two correlated `SUM` subqueries over `StockDocumentLine`. For a small page the plan is a nested loop
(about 26 reads per document: 1,318 reads for 25 rows, 9,590 for 200); once `@p` is large enough the optimizer prefers a hash plan that scans the 43,070-page line table (703 ms of CPU, 43,070
reads). The plan is cached per statement text, so the page size that happens to be compiled first decides the plan of every later page size: after a hash compile, size 25 takes 0.6-0.8 s
here (3.3-5.2 s in the PR 81 run) until the plan leaves the cache. On a fresh 1M database the flip is between 1,000 and 1,500 rows, above the 200-row limit of the API, so it needs a
poisoned cache to show (the "poisoned" order of `23-measure-documents-list.ps1`: the cached statements are executed once from sqlcmd with `TOP(@p) = 2000`); in the PR 81 run it flipped at about 150 rows. The
threshold moves with the statistics and the machine, which is why the fix does not depend on where it is. Hints compared on the page statement (cache cleared, poison at 2,000, then sizes 25 and 200):

| Hint | page 25 after the poison | page 200 after the poison | the 2,000-row compile itself | CPU of the compile |
|---|---|---|---|---|
| none (before) | 547-610 ms, 43,070 reads | 578 ms | 625 ms (hash plan) | 27 ms, once |
| `DISABLE_PARAMETER_SNIFFING` | 0 ms, 1,318 reads | 0 ms, 9,590 reads | 94 ms (nested loop) | 25 ms, once |
| `OPTIMIZE FOR UNKNOWN` | same | same | 62 ms | 15 ms, once |
| `RECOMPILE` | 31 ms, 1,318 reads | 31 ms, 9,590 reads | 656 ms (hash plan: it does not help the large page) | **15-26 ms per request** |

Fix: `DocumentsPageSpec` tags the page statement with `TagWith(QueryHint.NoSniffing)`; `QueryHintInterceptor` (the renamed `RecompileInterceptor`, one interceptor, two hints, tags
in `Ims.Application/Common/QueryHint.cs`) adds `OPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))`: one plan, compiled from the average statistics, for every value. It is the same plan as
`OPTIMIZE FOR UNKNOWN` but names no parameter, so it serves every shape of the statement (first page, filters, cursor, offset). **Compile cost: one compile of 15-34 ms of CPU per cached
statement shape (after a restart or an eviction), and nothing per request**; `RECOMPILE` would have cost 15-26 ms of CPU on every request and still compiled the hash plan for a large page. The count is
not tagged: it has no `TOP` and did not flip. Rare values stay fine (status Draft, about 300 rows: 1,352 reads and 0-16 ms with and without the hint; a status with no rows: 0 reads). Through the
API, poisoned cache, ms for sizes 25 / 50 / 150 / 500 (= 200): no filter 655 681 703 640 -> 66 18 32 47; warehouse 736 712 684 684 -> 68 36 58 38; status posted 659 633 630 684 -> 64 18 40 53;
cursor 628 741 715 775 -> 61 21 50 55; warehouse + cursor 764 795 712 799 -> 69 35 44 50 (all other variants and both clean orders, small-first and large-first, were 10-220 ms before and after).
`DocumentsListPlanTests` sends every shape (first page, filters, text search, cursor, offset page, no total) through the real host and asserts the hint on the page and its absence on the count.

*Product lookup.* Root cause: `Sku = @code OR EXISTS (barcode = @code)` with `TOP(1)` makes the optimizer walk the `Product` clustered index and probe the barcode table per product until the
first match (79,000 reads per statement, three statements per call: the product and its two collections), so the cost grows with the position of the product: SKU-00042 5 ms, SKU-49999 380 ms,
an unknown code 130-170 ms (the whole table). Fix: `FindProductByCodeHandler` asks for the SKU first (`ProductBySkuSpec`, one seek on the unique filtered index: 6 reads, 0.1 ms of CPU),
and only when no product has it for the barcode (`ProductIdByBarcodeSpec`, one seek: 4 reads, then `ProductDtoSpec` by key). Measured on 40 products spread over the catalog, ms avg / P95 / max:
SKU 204 / 372 / 433 -> 3 / 4 / 12; barcode 291 / 549 / 616 -> 4 / 4 / 27; lower-case SKU 190 / 398 / 411 -> 2 / 3 / 3; unknown code 130 / 156 / 168 -> 3 / 5 / 20. The 160 answers were identical to
those of the old statement (0 differ). The comparison is the same column comparison, so it stays case-insensitive (`SQL_Latin1_General_CP1_CI_AS`) and the code is still trimmed. One behaviour is now
defined: a code that is one product's SKU and another's barcode (nothing forbids it) belongs to the SKU's product; the old statement returned whichever of the two the scan met first (the lower id).
`ProductLookupTests`: case and spaces, several barcodes, inactive products, a barcode taken off a product, the SKU-wins rule with the barcode owner created first, and a guard that no statement compares
the SKU and the barcode in one predicate (a shape check, not a plan check).

*Watch items (not changed, numbers in `results-1m-documents-lookup.txt` section 5).* `audit?actor=supervisor`: the default plan (actor index range seek, key lookups, sort) reads 462,438 pages (300-400 ms
of CPU) for an actor with half of the table; a backward clustered scan reads 16. The same scan reads the whole table (9,063 pages) for a rare or absent actor, where the seek reads 3-156, and
`RECOMPILE` and `DISABLE_PARAMETER_SNIFFING` each fix one end: it needs a product decision (an exact user name could use the ordered `(Actor, Id)` seek), not a hint. The unfiltered stock list costs 272 ms of CPU
and 3,576 reads per call (median 300 ms through the API on a quiet machine); P95 above 1 s only when the machine is slow.

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
| `19-reads-by-endpoint.ps1`, `20-publicid-write-bench.sql`, `21-by-id-reads.ps1`, `22-interleaved-ab.ps1`, `results-1m-public-ids*`, `results-1m-main*`, `results-1m-ab*`, `results-1m-by-id.csv` | section 4c: logical reads per HTTP call, the write cost and fragmentation of `UX_StockDocument_PublicId`, by-id reads of two builds on the same rows, an interleaved latency comparison of two builds on one database; `results-1m-public-ids.txt` has the raw numbers, `results-1m-public-ids-k6.csv` the load runs |
| `25-seed-reorder.sql`, `25-measure-reorder.ps1`, `results-1m-reorder.csv` | section 13: open orders on a restored copy, and the reorder suggestions list timed through the API |
| `23-measure-documents-list.ps1`, `24-measure-product-lookup.ps1`, `results-1m-documents-list-*`, `results-1m-lookup-*`, `results-1m-documents-lookup.txt` | section 4d: the documents list in three call orders (small-first, large-first, poisoned plan cache) and the product lookup by SKU, barcode and unknown code, before and after; the txt has the hint comparison and the watch items |
| `10-filtered-vs-unfiltered.sql` | why the indexes are filtered on `REC_DELETED = 0` |
| `11-snapshot.sql`, `11b-snapshot-diff.sql` | deadlock and lock-wait counters around a load test (used by `tests/load/run-scenario.ps1`) |
| `results-1m-before.csv`, `results-1m-after.csv` | the comparison in section 3 (`-before-run1` = first, colder baseline; `-full-proposal` = all 13 proposed index changes applied) |
| `results-100k-after.csv` | the 130k tier on the final code |
| `results-1m-movements-before.csv`, `results-1m-movements-after.csv` | R3 (section 4), re-measured on a fresh 998,184-ledger-row database of the same seed (161,800 documents, 1,051,964 lines): `mov-base*` and `fin-after*`: a first, quiet session (two baseline runs; the two quietest of five interleaved after runs; exports best of three rounds; there the PostedAt index was R3's own, same key and INCLUDE minus `Type`). `q-main*`, `q-newcode-noLive*`, `q-r3*`: the final index set on a fresh database, main as merged against the new code without and with the lines index, three interleaved rounds, in a session where unchanged statements ran 3-5x slower. The machine was shared with other test runs and antivirus scans for most of the session, so P95 of the other rounds is 2-10x higher on both sides; the logical reads in section 4 are exact |

## 11. Accounting export (PL1.6, `GET /api/accounting/export`)

> Removed 2026-10-22 (RA1 part A1): the endpoint, its handler, reader and `AccountingExportVolumeTests` are deleted. This section is kept as the record of what was measured; nothing in it runs any more.

The file for a period: one row per posted document line, plus a reversal row for every line of a document reversed in the period (dated by the reversal, as in the movements report), oldest first, at most `Limits.MaxExportRows` (50,000) rows, refused above that (never cut). Written by `EfAccountingReader`: two branches (postings, reversals) that reuse the movements report's predicates (`MovementFilters`, so the `IX_StockDocument_PostedAt` and `IX_StockDocument_ReversedAt` filtered indexes and the live-lines index are the ones available), each read in file order and cut to 50,001 rows, then merged and cut again, in ONE statement that projects every column of the file (product, partner, unit and warehouse are joins, not follow-up queries; the statement count is 1 plus the audit insert). One row more than the cap is asked for so the handler can refuse a period that is too big without a separate count.

**Measured on a 50,000-row period** (`AccountingExportVolumeTests`: a database of its own that is dropped at the end; 24,500 documents, 245,000 lines: 4,000 posted and 500 reversed documents in March 2025 = exactly 50,000 export rows, and 20,000 posted documents in 2023 as noise, so the period is 18% of the documents; set-based `INSERT`, no API calls). LocalDB `ImsDev` on a shared workstation:

| What | Result |
|---|---|
| The export statement (plan cache, one execution) | 11,280 logical reads; 444 to 1,147 ms CPU; 1.1 to 1.9 s elapsed. Plan: parallel (DOP 4), clustered index scans and hash joins, no key lookups; at 18% of the documents the optimizer scans, as it should. The filtered date indexes matter at the 1M tier (a month is about 5%): see below. |
| End to end, first request of a fresh host (request, statement, workbook of 50,000 x 17 cells, 1.9 MB response) | 8.1 s on a quiet machine; 26 to 34 s on the same machine while other test runs were going (the same run seeded in 23 s instead of 6 s). |
| Where the time goes | The statement is 1 to 2 s. The rest is ClosedXML building the workbook in memory (about 20 s before the writer change below, on a busy machine) and saving it (4 s). |

**A change to the shared writer came with it** (`ClosedXmlExcelWriter`, used by every export): the number format is set once per column instead of once per cell (10 s of 27 at 50,000 rows), and column widths are fitted to the first 2,000 rows instead of every row (9 s of 27); a sheet of up to 2,000 rows is laid out exactly as before. A streaming writer (`OpenXmlWriter`) would bring the workbook to about 1 s and its memory to a constant; it is a bigger change and not needed while an export is a deliberate, rare action under the long request timeout (`Limits.LongRequestTimeoutSeconds`).

**Still to do (DBA, exclusive use of `Ims_Perf`):** the one-month export at the cap on the 1M tier. Procedure: seed the 1M tier (section 9), enable the module (`PUT /api/modules/accounting`), pick a month whose line count is just under 50,000 (`SELECT CAST(PostedAt AS date)...` over `StockDocument`), call `GET /api/accounting/export?from=...&to=...` three times with the Query Store on, and read the statement with `04-query-store-top.sql` (filter on `HsCode`). Record the reads, the CPU and whether the plan seeks `IX_StockDocument_PostedAt`. Acceptance 9 of the pilot plan is met when that row is added here.

## 12. Receipt link to a purchase order (PL2.4, migration `AddReceiptPurchaseOrderLinks`, MG13)

Two nullable `int` columns with NO default on the two big document tables, one filtered index and one foreign key: `StockDocument.PurchaseOrderId` (FK to `PurchaseOrder`, restrict, index `IX_StockDocument_PurchaseOrderId` filtered `WHERE [PurchaseOrderId] IS NOT NULL`) and `StockDocumentLine.PurchaseOrderLineNo` (the order line's number; no foreign key and no index: the lines of a receipt are reached through their document).

**Timing on the 1M tier** (`Ims_PerfPid1M`: 161,800 documents, 1,053,056 lines, 1,001,225 ledger rows). The database was backed up (`COPY_ONLY`), restored as a throw-away copy on D:, brought to the previous migration with `dotnet ef database update`, and the generated script (`dotnet ef migrations script <previous> <this>`) was run on the copy with a timer between statements; the copy was dropped afterwards, `Ims_PerfPid1M` itself was not touched.

| Statement | Time |
|---|---|
| `ALTER TABLE StockDocument ADD PurchaseOrderId int NULL` | 2 ms (metadata only) |
| `ALTER TABLE StockDocumentLine ADD PurchaseOrderLineNo int NULL` | under 1 ms (metadata only) |
| `CREATE INDEX ... WHERE [PurchaseOrderId] IS NOT NULL` | 23 ms (one pass over the table to find no row; the index is empty, 0 KB) |
| `ADD CONSTRAINT ... FOREIGN KEY ... WITH CHECK` | 31 ms (validates an all-NULL column) |
| **Whole script in one transaction** | **58 ms** |

Well under the 1 s limit of plan risk 6, so the foreign key stays. No row is written: the migration test compares the row versions of all documents and lines before and after.

**Why the one index** (a linked receipt is a handful of rows among 161,800): with 3 posted receipts linked to one order, "the receipts of this order" (the PO detail, the cancel check) reads **22 pages** through the filtered index against **4,236** for a scan of the clustered index, and the posted-quantity sum (documents of the order, then their lines through `IX_StockDocumentLine_DocumentId`) reads 14 + 114 pages in 3 ms. The index is empty on every database that does not use purchasing and costs a write only on a linked receipt. The line column has no index: nothing reads lines by order line number alone.

**Running the script by hand** (a DBA deployment). Use `sqlcmd -I -b` (or SSMS). `-I` is QUOTED_IDENTIFIER ON, which the filtered index needs; the script also sets it itself inside the `EXEC` that creates the index, so a client with it OFF (sqlcmd's default) still gets the index (tested with the session set to OFF). `-b` stops at the first error. Without `-b`, sqlcmd and SSMS go on after an error, and EF's script has no `XACT_ABORT`: a failed statement does not undo the others, so the foreign key, the history row and the `COMMIT` still run and the migration is recorded as applied even though one statement failed. Check `sys.indexes` for `IX_StockDocument_PurchaseOrderId` after a hand run. The index is created by dynamic SQL (`EXEC(N'...CREATE INDEX ...')`) on purpose: in one batch with the `ADD COLUMN` above it, a plain `CREATE INDEX` is compiled before the column exists ("Invalid column name").

**`Down` and its refusal.** `Down` refuses (error 50006) while any document or line, live or soft-deleted, carries a link. The generated Down script is ONE batch (EF puts no `GO` inside a migration's transaction) and the refusal is a `THROW` with `XACT_ABORT ON`, which ends the batch and rolls the transaction back: nothing is dropped, the history row stays and the `COMMIT` never runs, whether the client stops at an error (`sqlcmd -b`) or goes on (SSMS, `sqlcmd` without `-b`); both are tested. The runner behaves the same. The `Up` script is one batch too but has NO `XACT_ABORT`: a statement-level error in it (for example the index with QUOTED_IDENTIFIER OFF, now prevented) lets the rest, the history `INSERT` included, run; that is why `-b` is advised and `sys.indexes` is worth a look after a hand run.

## 13. Reorder suggestions (PL2.6, `GET /api/purchase-orders/suggestions`, no migration, no index)

The list is computed live from the shared low-stock sets plus ONE grouped sum of what is still to come on the open orders of the warehouse; the count and the page are the same query, with the filter `2 x safety - on hand - on order > 0` in SQL so a page is never short and the total is true. Statements per call: the module switch, the warehouse id lookup, the count, the page.

**Data** (`25-seed-reorder.sql` on a restored copy `Ims_PerfReorderCopy` of `Ims_PerfPid1M`, brought to the current migration by starting the API on it): 50,002 products, 172,887 balances, 5 warehouses; **each warehouse has 12,800 to 14,800 rows to order** (the seed puts about a third of the balances at or below safety, far more than a real warehouse) plus the products never stocked; 200 open orders with **1,000 order lines** (65 Draft, 70 Submitted, 65 Approved), 100 Posted receipts linked to Approved orders (633 lines). The original `Ims_PerfPid1M` was only read by a `COPY_ONLY` backup; the copy and the backup were dropped afterwards.

**API timing** (`25-measure-reorder.ps1`, API on the copy, 5 warehouses x 10 timed calls after one untimed, pageSize 50, the whole run twice on a quiet machine; the figures are the two runs; a run while another process used about 35 % of the CPU was 3 to 5 times slower with identical reads, so only logical reads and quiet runs are comparable):

| Call | P50 | P95 | max | Query Store (per call, plan cached) |
|---|---|---|---|---|
| page 1 | 152 / 180 ms | 199 / 240 ms | 207 / 287 ms | count 8,125 reads, 109 ms CPU; page 126,601 reads, 67 ms CPU |
| page 20 (offset 950) | 194 / 205 ms | 268 / 339 ms | 325 / 484 ms | same statements |
| search (SKU prefix, 2 products match) | 345 / 363 ms | 459 / 575 ms | 646 / 677 ms | count 16,118 reads, 227 ms CPU; page 1,938 reads, 164 ms CPU |

P95 stays under the 1 s target on the largest list the 1M tier can produce (about 14,800 rows). The first call of each statement shape compiles for 485 ms of CPU (a plan of five derived tables and a union); the plan is cached after that. The search is the slowest path because `LIKE '%text%'` reads every product row, as in every other search of the API (`Limits.SearchMinTextLength`); it is the one to watch (see below).

**What the measurement changed.** The first form summed the posted receipts with one correlated subquery per order line (twice per line, once per union branch): 1,548 seeks on `StockDocument` and 720 on `StockDocumentLine`, 20,000 reads, page 1 at P50 341 ms / P95 960 ms and search at P95 2,814 ms (a first run, probably not quiet). It now sums the receipts of the open orders of the warehouse ONCE, grouped by (order, line number), joined to the order lines (the filtered index `IX_StockDocument_PurchaseOrderId`, then the lines of those few documents), and the search is applied to the products of each branch before the join, not to the union. Result: the table above. The ledger is never read; no table is scanned per row of the result.

**Where the reads are.** The page statement shows about 126,000 reads (count: 8,125) because the planner, with the row goal of `OFFSET ... FETCH`, joins the 9,400 candidate rows one seek at a time: `Product` 55,000 reads, `StockBalance` 34,000, `Warehouse` 18,700, the rest the order lines and receipts (about 3,000). It costs 67 ms of CPU because every page is in memory; cold, the distinct pages are a few thousand (about 50 MB). It is the shared low-stock sets (`LowStockSets`, plan D8) plus the never-stocked products (30,000 have a safety level): the list is the only reorder surface that still lists a product with a safety level and no balance row anywhere. An attempt to read the warehouse once instead of joining it per row did not change the plan and was dropped. No hint can be put in LINQ; a computed flag on the product would be a migration for the DBA to decide if this ever matters.

**Decision.** No index and no migration: the existing `IX_PurchaseOrder_WarehouseId`, `IX_PurchaseOrderLine_PurchaseOrderId` and the filtered `IX_StockDocument_PurchaseOrderId` carry the on-order sum (the count reads 34 pages of order lines, 28 of orders, 916 of receipt lines and 340 of receipt headers for 200 open orders, 1,000 lines and 100 linked receipts). A search that matches many products in a very large catalog is the first thing to re-measure; a search resolved once to a short id list (like the barcode of `ProductSearch`) would replace the four product scans of count and page by one.

## 14. Sales: the sales-order tables, the delivery link and the DBA deferrals (PL3.2 to PL3.5, migrations `AddSalesOrders`, `AddSalesOrderApprovalRules`, `AddDeliverySalesOrderLinks`)

**Tables (PL3.2).** `SalesOrder` and `SalesOrderLine` are new and empty; nothing in the first pilot go-live writes to them (sales is off). Indexes as the purchase-order twins: unique live number, status and id for the list and the open-order count, filtered foreign keys for the nullable user columns, the line index on the product including the order and the base quantity. `Down` refuses while any order exists.

**Delivery link (PL3.5, `AddDeliverySalesOrderLinks`).** Same technique as section 12: `StockDocument.SalesOrderId int NULL` (FK to `SalesOrder`, restrict, filtered index `IX_StockDocument_SalesOrderId WHERE [SalesOrderId] IS NOT NULL`) and `StockDocumentLine.SalesOrderLineNo int NULL`, no default, no index on the line (the delivered sum reaches the lines through the filtered document index). The filtered indexes are created by dynamic SQL with `SET QUOTED_IDENTIFIER ON` inside it, so a script run with `sqlcmd` defaults still creates them (tested with the session setting OFF).

**Timing on a 1M-document database** (throw-away database on the local server, migrated to the previous migration, then 1,000,000 documents and 1,000,000 lines inserted set-based, then the migration run through the EF migrator): **Up 403 ms** (both columns, the filtered index over an all-NULL column, the foreign key `WITH CHECK`, and the order-line changes below), **Down 262 ms**. Under the 1 s limit of plan risk 6, so the foreign key stays. The ledger is not touched. This is the whole migration measured end to end, not each statement, and not the 1M Zipf tier of section 3.

**`Down` and its refusal.** Error 50008 while any document or line, live or soft-deleted, carries a link; one batch with `XACT_ABORT ON`, as section 12. The order-line changes below are undone without a refusal (they hold no data).

**Deferrals of the PL3.2 review, done while the sales tables are empty.**
- `IX_SalesOrderLine_SalesOrderId_LineNo`: UNIQUE on `(SalesOrderId, LineNo) WHERE [REC_DELETED] = 0`. A delivery names an order line by its number, so two live lines with one number would be a silent mis-link. Soft-deleted lines are outside the index, which is what a save needs: it replaces the lines, soft-deleting the old rows and inserting the new ones in one batch. EF sends the deletes before the inserts, and the update, parallel-save and replace tests pass with the index in place.
- `IX_SalesOrderLine_SalesOrderId` (the unfiltered foreign-key index, which a physical delete needs) now `INCLUDE (BaseQty, SellingAmount)`, the two columns the detail and the list add up per order. Reasoned, not measured: an order has a handful of lines and the table is empty until sales is live, so the cost is two columns in a small index and the benefit is that the sums read the index only. Re-check with the plan of the list at the first real volume.
- CHECK constraints `CK_SalesOrderLine_Qty` (`> 0`), `CK_SalesOrderLine_BaseQty` (`> 0`) and `CK_SalesOrderLine_SellingPrice` (`>= 0`): decided YES. The domain already refuses these values; the constraint costs nothing on an empty table and would take a full validation pass later. A price of 0.00 stays legal on a draft (submit refuses it, D15). No CHECK on the amount: it is derived and capped by the domain limit.

**Review fixes of PL3.5 (PR #214).**
- **`CK_StockDocument_OneLink`** (`[PurchaseOrderId] IS NULL OR [SalesOrderId] IS NULL`): decided YES and added to `AddDeliverySalesOrderLinks`. `ALTER TABLE ... WITH CHECK ADD CONSTRAINT` validated at **173 ms** on 1,000,000 documents (about 200 of them linked), `DROP` 1 ms; under the 1 s limit, so it stays. `Down` drops it inside its single refusing batch, before the columns. A document carries at most one link in the domain (`StockDocument.Link` throws if both keys are set) and now in the database.
- **Query plan of `EfDeliveredQuantityReader.PostedByLineAsync`** at 1,000,000 documents and 1,000,000 lines with about 200 linked Posted deliveries (the exact statement shape, `SET SHOWPLAN_TEXT`): seek on the filtered `IX_StockDocument_SalesOrderId` for the order, key lookup on `PK_StockDocument` for status and deleted flag, seek on `IX_StockDocumentLine_DocumentId_Live` for the lines of those few documents, key lookup on `PK_StockDocumentLine` for quantities, sort and stream aggregate over the few rows. No scan of either big table. The result is one row per order line (3 rows for the 600 lines of the test).
- **What `Up` assumes.** `SalesOrderLine` holds no row that breaks the unique live `(SalesOrderId, LineNo)` or the three checks. It checks and stops in words (error 50009, its own batch: run a script by hand with `sqlcmd -b`). Sales is off in every real database and the domain has always refused those values, so a failure means rows written by hand.
- **Errors from the new constraints.** `2601` (unique index) and `2627` map to 409 in `SqlExceptionMapper` (the unit of work retries them first); `547` (a CHECK or foreign key) is NOT mapped and would be a 500, which is why the domain refuses every value a CHECK refuses (`SalesOrderLine.Create` now refuses a base quantity that rounds to 0 at four decimals: `Messages.QtyTooSmall`) and why `CK_StockDocument_OneLink` cannot be reached through the API.

## 15. Price lists (PL3.11, migration `AddPriceLists`, PL3-M5)

**What it does.** Two new tables, empty, `PriceList` and `PriceListItem`, and the grant of `pricelist.manage` to the Administrator, Owner and Supervisor roles in the same migration (a data-only `INSERT ... SELECT` over `Role`/`RolePermission`, a frozen statement, idempotent, only for a system role that still holds `masterdata.read`). Nothing in any existing table is altered: the generated `Up` script has `CREATE TABLE`, `CREATE INDEX` and one `INSERT INTO [RolePermission]`, and a test fails if it contains an `UPDATE`, a `DELETE`, an `ALTER TABLE` or an insert anywhere else. The tables are written only while `sales` is on and a person edits a price list, so they hold a handful to a few thousand rows; they are not on a hot path.

**Constraints, all over LIVE rows (`REC_DELETED = 0`), so a deleted list frees what it held.**
- `IX_PriceList_IsDefault` UNIQUE on `IsDefault WHERE IsDefault = 1`: at most one default list.
- `IX_PriceList_CustomerId` UNIQUE on `CustomerId WHERE CustomerId IS NOT NULL`: at most one list per customer. It also serves as the foreign-key index of `CustomerId` (no second one).
- `IX_PriceList_Name` UNIQUE on `Name`: not in the plan, added because two lists with one name cannot be told apart in a list of lists. The collation is case-insensitive, so `Retail` and `RETAIL` collide, as the application check says.
- `IX_PriceListItem_PriceListId_ProductId_UomId` UNIQUE, `INCLUDE (ListPrice)`: one price per list, product and unit. It is also the index of the lookup PL3.12 does (a list, a product, a unit), which then reads the price from the index and never touches the table. A price is for exactly the unit it names, so the unit is part of the key and nothing is converted.
- `CK_PriceList_DefaultHasNoCustomer` (`IsDefault = 0 OR CustomerId IS NULL`) and `CK_PriceListItem_ListPrice` (`ListPrice > 0`): decided YES, the tables are empty so they cost nothing to add. The domain refuses both values first (`PriceList.SetCustomer`, `PriceListItem.Create`), because `547` is not mapped by `SqlExceptionMapper` and would be a 500; `2601` and `2627` map to 409, and the unit of work retries them, so the request that lost a race re-runs and gets the application's sentence (`PriceListUniqueness`).
- The default unfiltered foreign-key indexes on `PriceListItem(ProductId)` and `PriceListItem(UomId)` are left as EF made them. Reasoned, not measured: the table is small and nothing reads by product alone yet; if "which lists price this product" ever becomes a screen, the product index is already there.

**`Down` and its refusal.** Error 50010 while any price list exists, live or soft-deleted. The check, the removal of every `pricelist.manage` grant (whichever role holds it: the previous release does not know the code) and both `DROP TABLE` statements are ONE batch with `XACT_ABORT ON`, so a refusal changes nothing, whether the client stops at the error (`sqlcmd -b`) or goes on (SSMS); both are tested, with every batch of the script run on its own.

**Run by hand with `sqlcmd`.** A filtered unique index makes every `INSERT` and `DELETE` on its table require `QUOTED_IDENTIFIER ON`, and `sqlcmd` starts with it OFF. `RolePermission` has one, so the grant statement and the `Down` batch each begin with `SET QUOTED_IDENTIFIER ON;`. EF's own script already sets it for the filtered indexes it creates. A test runs the whole `Up` script with the session setting OFF in front of every batch and finds all four filtered indexes and the history row.

**Not measured at 1,000,000 documents** when this section was written (measured since: section 18). The migration touches no row and no index of `StockDocument` or `StockDocumentLine`, so no step of it depends on their size, and the tests above prove that from the script and from the row-version checksum of 5,000 products and 2,000 partners that does not move. A timing on a database with the 1M-document tier was NOT taken in this change: the only drive on the build machine is C:, and a throw-away database of that size (about half a gigabyte) is not allowed there. To take it on a machine that has the room: migrate a throw-away `Ims_Perf` database to `20261108061359_AddDeliverySalesOrderLinks` (section 9, steps 1 to 3 seed the 1M tier), then `dotnet ef database update 20261108132822_AddPriceLists` with the stopwatch on, then `dotnet ef database update 20261108061359_AddDeliverySalesOrderLinks` for `Down`. The expectation, from the nature of the statements (two `CREATE TABLE`, seven `CREATE INDEX` on empty tables, one three-row insert), is a few hundred milliseconds in total, dominated by the migrator's own history table.

## 16. Returns: the return link on the big tables (PL3.16, migration `AddReturnLinks`, PL3-M7)

**What it does.** Two nullable columns with NO default, so the add is metadata-only on SQL Server (the technique of sections 12 and 14): `StockDocument.ReturnOfDocumentId int NULL` (a foreign key to `StockDocument` itself, restrict) and `StockDocumentLine.ReturnOfLineNo int NULL` (the position, 1 to n, of the original line over the original's lines in key order; not a foreign key, and not a row id, because a Posted document's lines never change and the id rule keeps row ids out of contracts). One index: `IX_StockDocument_ReturnOfDocumentId`, filtered on `IS NOT NULL`, so it holds only returns and costs nothing on any other document; it serves "the returns of this document" and the returned-quantity sum. No index on the line (the sum reaches the lines through the document index, as for deliveries). The filtered index is created by dynamic SQL with `SET QUOTED_IDENTIFIER ON` inside it, so a script run with `sqlcmd` defaults still creates it (tested with the session setting OFF).

**The rule changes.** `CK_StockDocument_OneLink` becomes "at most one of `PurchaseOrderId`, `SalesOrderId`, `ReturnOfDocumentId`" (a sum of three `CASE` terms, `<= 1`). It is dropped and added again `WITH CHECK`, which reads `StockDocument` once to validate. The previous version of this rule validated at 173 ms on 1,000,000 documents (section 14); this one reads the same rows and adds one more null test per row, so the expectation is the same order of magnitude, not a measurement.

**`Down` and its refusal.** Error 50011 while any document or line, live or soft-deleted, carries a return link; one batch with `XACT_ABORT ON`, as sections 12 and 14, so a refusal changes nothing (the new rule stays, the columns stay). With no link left it drops the rule, the foreign key, the index and both columns, and puts the two-link rule back. Tested: refusal for a live document, a soft-deleted document and a line; Down after clearing; Up, Down, Up on 5,000 documents with the row-version checksum unchanged; the script with every batch run on its own.

**Query shapes.** `EfReturnedQuantityReader`: one grouped sum over the lines of the Posted documents whose `ReturnOfDocumentId` is the original: a seek on the filtered index, then those few documents' lines through `IX_StockDocumentLine_DocumentId_Live`, as the delivered-quantity sum. Reasoned from the plan shape of section 14, not re-measured at volume.

**Not measured at 1,000,000 documents** when this section was written (measured since: section 18). The same reason as section 15: the only drive on the build machine is C:, and a throw-away database with a million documents and lines (about half a gigabyte) is not allowed there. To take the timing on a machine with room: migrate a throw-away `Ims_Perf` database to `20261109035255_GrantReturnsPermissions` (section 9, steps 1 to 3 seed the 1M tier), then `dotnet ef database update 20261109044255_AddReturnLinks` with a stopwatch, then `dotnet ef database update 20261109035255_GrantReturnsPermissions` for `Down`. The expectation is a few hundred milliseconds in all, dominated by the `WITH CHECK` validation of the rule; a foreign key over an all-NULL column and the empty filtered index cost almost nothing.

**PL3.17 additions to section 16 (review findings of PL3.16).**
- **`Down` cost.** `AddReturnLinks.Down` first looks for a link (`EXISTS` on `StockDocument.ReturnOfDocumentId`, then on `StockDocumentLine.ReturnOfLineNo`). The document test is an index seek on the filtered index (no rows when nobody returned anything), but the LINE column has no index, so on a database that holds no return at all the second `EXISTS` is a full scan of `StockDocumentLine` (about 1,000,000 lines at the 1M tier; about 3,000,000 lines at the 2M ledger tier), and after it the `DROP CONSTRAINT`/`DROP COLUMN` statements. Plan for a maintenance window of the order of seconds, not milliseconds, and for a `Sch-M` lock on both tables while it runs. Not measured here (section 16, last paragraph).
- **No `CHECK (ReturnOfLineNo >= 1)`.** Adding it would validate every line of the big table (a full scan of about 3,000,000 rows at the 2M tier) for a value the domain cannot produce (a return line gets its number from the position of an original line, 1 or more); a number that matches no original line is refused by the line builder and the posting check. Decided: not added. `CK_StockDocument_ReturnNotSelf` (`[ReturnOfDocumentId] IS NULL OR [ReturnOfDocumentId] <> [Id]`) IS added, by its own migration `AddReturnSelfLinkCheck` (PL3-M8, the merged PL3-M7 is not rewritten): `WITH CHECK` reads `StockDocument` once (the same order of cost as the previous rule, 173 ms at 1,000,000 documents, not re-measured), `Down` is metadata-only.
- **Runbook for PL3-M7 and PL3-M8 on a live installation.** (1) Stop the API (or put it in maintenance): the migration needs `Sch-M` on `StockDocument` and `StockDocumentLine`, and a request holding a document row or an application lock makes it wait; (2) run with `sqlcmd -b` (or the EF migrator) with a lock timeout you can live with, e.g. `SET LOCK_TIMEOUT 60000;` in front of the script, so a blocked run fails in a minute instead of queueing every reader behind its schema lock; (3) after it, `sys.indexes` shows `IX_StockDocument_ReturnOfDocumentId` with a filter and `sys.check_constraints` shows `CK_StockDocument_OneLink` and `CK_StockDocument_ReturnNotSelf` trusted (`is_not_trusted = 0`); (4) start the API. If the run is stopped half way, run it again: the migration history decides, and `Down` of an unfinished run is the same single batch.
- **Reversal of an original.** Reversing a Posted receipt or sale issue now takes `ret:{id}` and asks `EXISTS (Posted return of it)` (a seek on the filtered index of `ReturnOfDocumentId`): +2 commands for those two kinds of document only, 0 writes; every other reversal and every posting runs the statements it ran. `PostingStatementCountTests` pins the two reversals at 10 and 12 commands instead of 8 and 10.
- **Movements.** The movements report rows carry `IsReturn` (`ReturnOfDocumentId IS NOT NULL` on the document row already read; no join was added).

## 17. Release runbook (migrations, rollback, the STOP before switch-on)

Kept from the go-live checklist, which was removed on 2026-11-13 (it is in git history). Only what exists nowhere else in `docs/` is here; the hand-run script technique is in sections 12, 15 and 16 (`sqlcmd -I -b`, QUOTED_IDENTIFIER, the lock timeout and stopping the API before PL3-M7).

1. **Migrations are a release step, never automatic outside Development.** `Database__MigrateOnStartup` is off in production. Either run the DBA script by hand in the same step as the new build, or turn `MigrateOnStartup` on for ONE instance for that start only (instances starting together race). A script-only deploy creates no administrator: start the API once with `MigrateOnStartup=true` and `Seed__AdminPassword` set (it writes only what is missing), then turn it off. Back up the database and the file store (`Storage__RootPath`) first and keep the backup until the smoke checks pass.
2. **`RemoveAccountingModule` must run with the build that removed the module.** It deletes the `Module.accounting` switch row and every `accounting.export` grant (hard delete, no audit row). Until it has run, the Owner role and any custom role still hold `accounting.export` in the database, and an administrator who edits such a role gets 403 `RoleEditBeyondYourAccess`.
3. **Rollback order.** (a) The `Down` of `RemoveAccountingModule` does nothing: the deleted grants and the switch row stay deleted, which an older build treats as "off". (b) To go back to a build older than part B of RA1 (the tax fields), run `Down` of `RemoveTaxReadyFields` first (`dotnet ef database update 20261022122230_RemoveAccountingModule`, or the script), then redeploy the older binary; the older binary against a database without `Product.HsCode` and `Partner.Tin` fails every product and partner read with a 500, and `Down` re-adds the four columns empty. (c) Do not roll the schema back after real use: the `Down` of `AddPurchaseOrders`, `AddReceiptPurchaseOrderLinks` and the later link migrations refuse while any order or link exists; return to an older build by restoring the backup taken in step 1.
4. **STOP: the placeholder approval amounts are not facts.** The migrations `AddPurchaseOrderApprovalRules` and `AddSalesOrderApprovalRules` write RM 5,000 (Supervisor) and RM 20,000 (Owner) where no rule of that type exists, so that the purchasing and sales switches work. They are placeholders. Before purchasing goes live, and again before sales does, the owner gives the real amounts. The amounts are editable in Settings > Approval rules only once the module is on, and the module is live for Supervisor and Clerk the moment it is on: switch it on at a quiet time, open Approval rules at once, set the real amounts, and only then tell the users. Orders submitted before that are routed by the placeholders.

## 18. PL3 on the 1M tier: migration timings and a load run (2026-11-14, test only, no change in `src/`)

First real numbers for PL3-M4 to PL3-M8 (sections 14 to 16 had to say "not measured") and the k6 scenarios on the PL3 build, including four new ones for the PL3 endpoints. Everything was run on a throw-away database `Ims_Perf_20261114` that was dropped afterwards; nothing here changes code, schema or contract.

**Machine and method.** AMD Ryzen 7 7730U (8 cores, 16 threads), 13.8 GB RAM, NVMe SSD, Windows 11; SQL Server 2022 **Developer** (16.0.1000.6, the default instance, `max degree of parallelism` 8, no memory cap) on the same machine as the API (Release build, Kestrel) and k6 v2.4.0. Unlike every earlier section this is a full SQL Server, not Express or LocalDB, and **the machine was shared** with other test runs against the same instance: the server-wide wait statistics at the end of the run show `THREADPOOL` (21,694 s over 46,860 waits), `RESOURCE_SEMAPHORE` (28,644 s, 11,669 waits) and `LCK_M_X` (14,816 s) accumulated since the instance started, and the buffer pool held 550 MB of the 1.8 GB database. Read the absolute numbers as pessimistic and noisy; the logical reads and the plans are exact.
Data (`01-seed-perf.sql`, 1M tier, seeded in 171 s at the state before PL3-M4): 161,800 documents (153,810 posted), 1,052,706 document lines, 1,000,729 ledger rows, 173,229 balance rows, 315,628 audit rows, 50,002 products, 5 warehouses; 1.8 GB of data and log. The seed script ran unchanged in two respects and with one workaround: two earlier attempts hung in a parallel plan (`CXSYNC_PORT`, `e_waitPortOpen`, minutes without progress) on this instance, so the third ran a scratch copy of the script with `OPTION (MAXDOP 1)` on its statements (the file in the repository is untouched; `ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP` was set to 1 on the throw-away database and was not honoured by those statements; the server-wide setting was not touched). `sqlcmd` could not connect here (its ODBC driver 17 is missing), so the scripts were run through `System.Data.SqlClient` and `dotnet ef`.

### 18.1 Migration timings (PL3-M4 to PL3-M8)

How: `dotnet ef migrations script <from> <to>` for each migration (the DBA script of the runbook, section 17), run statement by statement on one connection with a stopwatch, in order Up M4..M8, Down M8..M4. A second connection read a primary key of `StockDocument` and a `DocumentId` of `StockDocumentLine` every 40 ms to show the blocking. Two database sizes in the same database: **A** = the 1M tier as seeded (161,800 documents, 1,052,706 lines); **B** = the same plus 838,200 Cancelled documents with one line each, **1,000,000 documents and 1,890,906 lines** (they were deleted again afterwards, the migrations ran with them present). Wall-clock milliseconds of the whole migration script (including `BEGIN TRANSACTION` ... `COMMIT`) and, in brackets, the longest read of the concurrent probe that overlapped it (normal: 0.3-0.7 ms).

| Migration | Up A | Up B, first run | Up B, second run | Down A | Down B, first | Down B, second |
|---|---|---|---|---|---|---|
| PL3-M4 `AddDeliverySalesOrderLinks` | 324 (243) | **1,527 (1,486)** | 608 (569) | 369 (44) | 430 (74) | 441 (54) |
| PL3-M5 `AddPriceLists` | 140 (0.4) | 144 (4) | 130 (0.3) | 42 (0.6) | 46 (0.5) | 27 (0.4) |
| PL3-M6 `GrantReturnsPermissions` | 179 (0.7) | 147 (0.4) | 153 (0.4) | 46 (0.5) | 37 (0.5) | 21 (0.3) |
| PL3-M7 `AddReturnLinks` | 197 (178) | 594 (566) | 545 (519) | 336 (102) | 725 (226) | 700 (290) |
| PL3-M8 `AddReturnSelfLinkCheck` | 73 (65) | 306 (255) | 305 (289) | 43 (0.5) | 55 (30) | 59 (8) |
| All five | 913 | 2,718 | 1,741 | 836 | 1,293 | 1,248 |

- **Up** of all five migrations takes 0.9 s at 162k documents and **1.7 to 2.7 s at 1,000,000 documents**; the longest single one is PL3-M4. Inside PL3-M4 at B: the filtered index 127-238 ms, the foreign key 38-635 ms, `CK_StockDocument_OneLink` validated in 296-460 ms (section 14 measured 173 ms for the same check on another machine); inside PL3-M7 at B: index 100 ms, foreign key 37 ms, the rewritten `CK_StockDocument_OneLink` (`WITH CHECK`, drop and add) 346 ms; PL3-M8's single `ADD CONSTRAINT` (`CK_StockDocument_ReturnNotSelf`) about 280 ms. The first run at B is the slowest (cold pages after the top-up); the second is the figure to plan with. PL3-M5 and PL3-M6 do not depend on the size of the big tables (130-180 ms in every run, none blocked a reader), as the sections said.
- **Blocking.** A read of `StockDocument` or `StockDocumentLine` that arrived while PL3-M4, -M7 or -M8 ran waited until the schema lock was released: at most 1.49 s at B (PL3-M4, first run), 0.52-0.57 s in the second run. The wait is about as long as the migration's schema-changing statements, so stopping the API first (runbook, section 17) is right, but a request that arrives is delayed, not failed (no timeout was hit). PL3-M5 and PL3-M6 never blocked a read for more than 4 ms.
- **Down** of PL3-M7 takes 0.34 s at A and 0.70-0.73 s at B: the first batch looks for a return link, and the line column has no index, so it scans `StockDocumentLine` (section 16 expected "seconds"; at 1.89M lines it is 0.7 s) and then drops. PL3-M4 `Down` takes 0.37-0.44 s. A reader waited at most 0.29 s during a `Down`.
- **Through the EF migrator** (`dotnet ef database update`, database A): one command that applies the five Ups took 4.3 to 5.0 s, against 3.8 s for the same command when nothing is pending (process start and model load), so the five migrations add about 0.5 to 1.2 s; the five Downs in one command took 4.8 s and, on the first (cold) run, 9.2 s.
- What to plan: a maintenance window of **well under 10 seconds** for the five migrations at 1,000,000 documents, plus the time to stop and start the API; this was one machine, one run per cell, repeat before relying on a difference below about 30 %.

### 18.2 Load run (k6, API in Release on the migrated 1M database, closed loop, no think time beyond the scripts' 0-0.3 s)

The API ran with `RateLimit__PerMinute=1000000` and `Jwt__AccessMinutes=30` (`tests/load/README.md`), one scenario after the other with 15 s between them, in the order of the table; the soak was 5 minutes instead of 10. `Run` is what k6 reports, including the ramp-down. P50/P95/P99 are k6's `http_req_duration` per kind (read: `GET`; write: every `POST`), in ms. "Thresholds" are the ones of `scenarios.js` (read P95 under 1 s, write P95 under 2 s, failed under 1 %, no 5xx, 504 or 409): the last column says which k6 reported as crossed, nothing else was assessed by hand. CPU of the SQL Server process was not recorded (the service process is not readable from this session; the API used at most 23 % of the machine in any run). Deadlocks: the performance counter `Number of Deadlocks/sec` (server-wide, `_Total`) and the `system_health` deadlock reports that name `Ims_Perf_20261114` were read before and after every scenario: **0 in every run**; the API log holds no deadlock victim (SQL error 1205) and no 409. The lock-wait counters of the database (`sys.dm_db_index_operational_stats`) were also 0 before and after every scenario, which is odd next to the waits in 18.3, so they are not evidence of "no lock waits".

| Scenario | VUs | Run (s) | Requests (per s) | Reads P50 / P95 / P99 (ms) | Writes P50 / P95 / P99 (ms) | Failed | 409 / 5xx / 504 / other 4xx | Flows ok / failed | Thresholds |
|---|---|---|---|---|---|---|---|---|---|
| post-burst | 50 | 75 | 1,101 (14.8) | - | 143 / 30,014 / 30,036 | 3.72 % | 0 / 2 / 0 / 39 | 109 / 41 | crossed: write P95, failed rate, 5xx |
| read-mix | 50 | 127 | 6,247 (49.0) | 99 / 3,997 / 6,684 | - | 0.00 % | 0 / 0 / 0 / 0 | - | crossed: read P95 |
| write-mix | 20 | 130 | 25,674 (198.2) | - | 17 / 92 / 368 | 0.00 % | 0 / 0 / 1 / 0 | 8287 / 1 | crossed: 504 |
| write-hot | 30 | 134 | 4,969 (37.0) | - | 20 / 9,985 / 10,356 | 0.06 % | 0 / 1 / 1 / 1 | 1386 / 3 | crossed: 504, write P95, 5xx |
| mixed | 50 | 192 | 12,774 (66.6) | 143 / 3,952 / 6,130 | 106 / 525 / 1,516 | 0.00 % | 0 / 0 / 0 / 0 | 1713 / 0 | crossed: read P95 |
| ramp | 200 | 378 | 46,997 (124.3) | 3 / 1,174 / 25,776 | 2 / 78 / 426 | 86.24 % | 0 / 2 / 40331 / 193 | 665 / 8398 | crossed: 5xx, 504, failed rate, read P95 |
| spike | 300 | 176 | 18,500 (105.0) | 33 / 12,939 / 30,026 | 26 / 702 / 30,022 | 83.95 % | 0 / 110 / 15086 / 331 | 327 / 3054 | crossed: 504, 5xx, failed rate, read P95 |
| soak | 50 | 309 | 19,106 (61.8) | 619 / 3,638 / 5,065 | 93 / 454 / 1,606 | 0.00 % | 0 / 0 / 0 / 0 | 2601 / 0 | crossed: read P95 |
| sales-read | 30 | 125 | 22,506 (179.4) | 5 / 52 / 77 | - | 0.00 % | 0 / 0 / 0 / 0 | - | all held |
| sales-write | 15 | 126 | 12,406 (98.8) | - | 40 / 426 / 498 | 0.00 % | 0 / 0 / 0 / 0 | 2271 / 0 | all held |
| returns-reversal | 10 | 125 | 12,538 (100.5) | - | 22 / 229 / 366 | 0.00 % | 0 / 0 / 0 / 0 | 2131 / 4 | all held |
| pl3-mixed | 40 | 186 | 14,461 (77.7) | 54 / 1,832 / 3,031 | 395 / 1,078 / 2,160 | 0.00 % | 0 / 0 / 0 / 0 | 1960 / 2 | crossed: read P95 |

The first eight rows are the existing scenarios on the PL3 build, the last four the new ones (`sales-read`, `sales-write`, `returns-reversal`, `pl3-mixed`). Comparison with section 3: the 1M tier there is measured one request at a time (P95 under 0.5 s for every interactive endpoint after the indexes) and by `read-mix` at 5, 10 and 20 VUs (P95 306 ms, 596 ms, 1.24 s); here `read-mix` runs at 50 VUs and reaches the same capacity (49 requests per second against "about 45") with P95 4.0 s. That is the capacity figure again, not a regression of one endpoint: the cost of a read did not change, the queue in front of SQL Server did. Write path: `write-mix` at 20 VUs completed 8,287 flows in 130 s with write P95 92 ms (section 3: 9-10 ms at 10 VUs on a quiet machine with Express); its one 504 crossed the "no 504" threshold.

**Per endpoint** (P50 / P95 / P99 ms and number of calls, from `by_name` of the summary JSON):

| Scenario | Endpoint | P50 / P95 / P99 | n |
|---|---|---|---|
| read-mix, 50 VUs | `/api/products?search=<sku>` | 4,878 / 8,432 / 12,601 | 227 |
| read-mix | `/api/stock` | 2,580 / 5,477 / 7,380 | 156 |
| read-mix | `/api/stock/low-stock` | 2,792 / 4,939 / 7,784 | 364 |
| read-mix | `/api/stock?search=bolt` | 2,519 / 4,773 / 9,519 | 126 |
| read-mix | `/api/stock/summary` | 1,653 / 3,412 / 6,392 | 316 |
| sales-read, 30 VUs | `GET /api/sales-orders/{id}` | 39 / 70 / 133 | 5,513 |
| sales-read | `GET /api/sales-orders` (six filter variants) | 4-6 / 8-13 / 15-25 | 7,686 |
| sales-read | `GET /api/price-lists/lookup` | 4 / 12 / 27 | 8,566 |
| sales-write, 15 VUs | create sales order | 15 / 93 / 139 | 2,271 |
| sales-write | submit sales order | 56 / 464 / 519 | 2,271 |
| sales-write | approve sales order (about 10 %) | 67 / 490 / 564 | 233 |
| sales-write | create delivery from the order | 71 / 431 / 499 | 2,271 |
| sales-write | submit delivery | 45 / 423 / 482 | 2,271 |
| sales-write | post delivery | 89 / 436 / 494 | 2,271 |
| sales-write | whole flow (7 requests) | 421 / 1,537 / 1,866 | 2,271 |
| returns-reversal, 10 VUs | `POST /api/returns` | 195 / 358 / 409 | 1,094 |
| returns-reversal | submit return | 191 / 341 / 412 | 1,094 |
| returns-reversal | post return | 193 / 355 / 398 | 1,094 |
| returns-reversal | whole return flow (7 requests) | 649 / 1,030 / 1,456 | 1,094 |
| returns-reversal | whole reversal flow (create, submit, post, reverse) | 94 / 253 / 355 | 1,037 |
| returns-reversal | the ordinary issue and receipt steps of the same flows | 10-14 / 44-71 / 56-87 | - |
| pl3-mixed, 40 VUs | whole flows: sales; return; reversal | 2,988 / 5,523 / 10,952; 2,453 / 5,139 / 11,086; 468 / 1,305 / 3,357 | 985; 607; 368 |

### 18.3 Slow spots, with the numbers and a hypothesis (nothing was changed)

1. **The delivered-quantity and returned-quantity readers do not seek: 25,000 to 30,000 page reads and 0.3 to 0.4 s per call at this volume.** From the plan cache after the run: the delivered-quantity statement (`SUM(Qty)` grouped by `SalesOrderLineNo`, `EfDeliveredQuantityReader`) ran 8,864 times, **382 ms on average, 30,446 logical reads per execution, 7.2 s at worst**; the returned-quantity statement (`ReturnOfLineNo`) ran 5,107 times, **304 ms, 25,388 reads, 7.5 s at worst**. The plan of the first (`dm_exec_text_query_plan`) is a **Clustered Index Scan of `PK_StockDocumentLine`** as the outer input of a nested loop that probes `IX_StockDocument_SalesOrderId` and `PK_StockDocument` once per line, then sort and stream aggregate. SQL Server 2022 compiled it as a parameter-sensitive plan (`PLAN PER VALUE ... predicate_range(SalesOrderId = @orderKey, 100.0, 1000.0)`, compiled for `@orderKey = 1390`) with an estimate of about 100 lines for the scan. Sections 14 and 16 describe the plan as a seek on the filtered document index first and the lines of those few documents next, taken as an estimated plan on a database with about 200 linked deliveries; with thousands of linked documents the optimizer chose the other order. This is the likely reason why every step that asks what was already delivered or returned is slow while the same kind of step without it is not: `delivery.create`, `delivery.submit` and `delivery.post` have P95 of 423-436 ms against 10-15 ms at P50 for plain document steps (`so.submit` and `so.approve` also show P95 464-490 ms in `sales-write`; their cause was not determined); `POST /api/returns`, submit and post of a return have **P50 of about 190 ms** each (the issue and receipt steps of the same flow: 10-14 ms); in `pl3-mixed` (40 VUs) `delivery.create` has P99 9.5 s and `return.create` P95 1.6 s. Hypothesis, not tested: the join order flips with the number of linked documents. Candidates for the DBA to measure: `OPTION (RECOMPILE)` through the existing `QueryHint` tag (as section 4b), a covering index on the line table that starts with the link column, or forcing the document side first. The cost grows with the size of `StockDocumentLine`, not with the order, so it is a scaling risk of the sales and returns modules.
2. **Document create and post queue behind one lock per warehouse and month when many VUs write at once.** `post-burst` (50 VUs, one flow each at the same moment on 5 hot products): create P50 19.9 s and P95 30 s (the 30 s request timeout), 41 of 150 flows failed, 2 responses were 500 (a cancelled command), 85 `POST`s were abandoned by k6 at its client timeout (the API logs them as 499). `write-hot` (30 VUs, 5 hot products, one warehouse): create P95 10.1 s, post P95 10.1 s, one 504. The API log shows where the time goes: `SqlTransactionLocker.AcquireAsync`, called from `DocumentNumbering.NextAsync`, is the frame at which a request gives up, that is the per-prefix, warehouse and month number lock (an application lock held until commit). With the same code spread over the five warehouses (`write-mix`, 20 VUs) create P95 is 119 ms and post 93 ms. No deadlock and no 409, so the lock does what it should, but 50 simultaneous creators in one warehouse wait for each other for up to 30 seconds on this machine. Hypothesis, not measured separately: the hold time is the commit of a transaction that writes the document, its lines and the audit rows, bound by the log flush of a disk and a server that other test runs share.
3. **Overload at 200 and 300 VUs (`ramp`, `spike`) ends in 504s, as designed, but widely: 86 % and 84 % of the requests failed.** In `ramp` 40,331 responses were 504 with a median of 3 ms (the request had already waited out the 30 s timeout, so it is answered at once), the API process used at most 16 % of the machine and no deadlock was counted: the limit is the queue in front of SQL Server, not API CPU. The 10-VU tail after the `spike` ran at P50 26-33 ms, so the API recovered once the load went down. The saturation point (about 100-125 requests per second on this machine with these reads) is the number to remember.
4. **Read endpoints at 50 VUs** (table above): `/api/products?search=<sku>` is the worst (P50 4.9 s), then `/api/stock`, `low-stock`, `stock?search=bolt` and `summary` (P50 1.7-2.8 s). Section 3 shows all of them under 0.45 s one request at a time, so the delay is queueing for CPU and memory grants on a saturated, shared instance (`read-mix` completed 49 requests per second whatever the endpoint). Hypothesis: the `products?search` and `stock?search` shapes scan the 50,002 products (`LIKE` over name and SKU) and each needs a memory grant, so they wait on `RESOURCE_SEMAPHORE` first; confirm with `04-query-store-top.sql` on a quiet machine before touching an index.
5. **What was fine.** The PL3 reads (`sales-read`, 30 VUs, 179 requests per second): sales-order list P95 8-13 ms, detail P95 70 ms, price lookup P95 12 ms, every threshold held. `sales-write` and `returns-reversal`: every threshold held, flow P95 1.5 s for the 7-request sales flow. The 4 failed flows in `returns-reversal` and the 2 in `pl3-mixed` are single rejected requests (4xx), not 5xx.

**To repeat it:** `tests/load/README.md` (the four new scripts are in its table; they switch the three modules on through the API, so only on a throw-away `Ims_Perf` database). The migration timing recipe is in 18.1: a database at `20261023110329_AddSalesOrderApprovalRules` seeded by `01-seed-perf.sql`, then `dotnet ef migrations script <from> <to>` for each migration gives the ten scripts.
