# Database performance: what was measured and what changed

Owner: DBA. Target (PRD, `docs/architecture/data-and-performance-rules.md`): **P95 < 1 s** for interactive endpoints. Read this in 5 minutes;
every number below can be reproduced with the scripts in this folder (section 8).

## 1. Result in one paragraph

At 1 million ledger rows every interactive endpoint is under 1 s P95 except the **unfiltered movements report** (1.15 s) and, before this
change, **stock/product search by SKU or barcode** (up to 1.8 s). The change set is small on purpose: **four indexes** (one migration,
`AddHotPathIndexes`) and **one behaviour-identical query rewrite** (the barcode is resolved by one index seek before the list query is built).
The previous investigation proposed 13 index changes; nine were rejected or deferred because the evidence did not support them (section 5), and
its baseline was taken on fragmented tables (section 6). Writes (posting) are unchanged.

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
| `/api/reports/movements` 7d / 30d / hot product | 148-267 / 196-308 | unchanged (151-329 / 206-431) | see R3 |
| `/api/reports/movements` **no filter** | 1,031 / **1,153** | unchanged, **still over 1 s** | needs R3 (a decision) |
| Excel exports (50,000 rows) | 1.3-3.6 s | unchanged | downloads, 120 s timeout; not interactive |

At the 100k tier **everything** is under 130 ms P95 (exports up to 2.9 s).

**Write path.** k6 `write-mix` (create, submit, post), 10 VUs, 60 s, alternating the old and new index set on the same database:
3,580 and 3,606 flows before, 3,561 and 3,557 after; write P95 10 and 9 ms in both; no errors, no 409/5xx, no deadlocks. A T-SQL
micro-benchmark (5,000 transactions of 1 document + 5 lines + 5 ledger rows + posting update + audit row) shows the new indexes cost less than
the run-to-run noise (0.62-0.83 ms per transaction either way). After about 18,000 posted flows `08-reconcile-drill.sql` still finds
**0** differences between `StockBalance` and the ledger.

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

## 5. Rejected proposals (the previous investigation proposed 13 index changes; evidence decides)

| Proposal | Verdict | Reason |
|---|---|---|
| `IX_StockLedgerEntry_ProductId_Id (ProductId, Id DESC) INCLUDE (WarehouseId)` | reject | the hottest product has 4,841 ledger rows and sorts in 2.8 ms; the mid-range product page read more (30,352 to 43,983 reads, 11 to 17 ms); +21.7 MB on the hottest write path. Revisit when one product exceeds about 300,000 ledger rows |
| `IX_StockDocument_Open` (filtered `Status IN (0,1,2)`) | reject | the API sends `Status = @p`; a filtered index with an IN list cannot be matched by a parameter, so it is never chosen (tested). `Status_Type_Id` already serves drafts in 3 ms |
| `IX_StockDocument_PostedAt` + `IX_StockDocumentLine_DocumentId_Cover` (36 MB) | defer with R3 | alone they do nothing for the movements report (unfiltered 15,272 to 10,374 reads, same time) because of the sort tie-break; with R3 the report drops from 1.65 s to 0 ms. The cover index also replaces the unfiltered FK index with a filtered one (a hard delete of a document would scan the table) |
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

## 7. Recommendations that need a decision (not applied: they change the order of rows that tie)

Measured with `09-rewrite-candidates.sql` (the exact EF statement versus the rewrite; 1M ledger rows):

| | Today | Rewrite (plus the deferred indexes) | What changes |
|---|---|---|---|
| **R2** stock list `ORDER BY Sku, Warehouse.Code` | 3,056 reads, 254 ms | `ORDER BY Sku, Id, WarehouseId` + unique covering `IX_Product_Sku`: 1,275 reads, **24 ms** | rows of one SKU appear by warehouse id instead of warehouse code |
| **R2b** stock sort `QtyOnHand DESC, Sku` | 3,153 reads, 380 ms | `QtyOnHand DESC, ProductId, WarehouseId` + `IX_StockBalance_QtyOnHand`: 460 reads, **0 ms** | equal quantities appear by product id instead of SKU |
| **R3** movements `ORDER BY PostedAt DESC, line.Id` | unfiltered 11,119 reads, **1.65 s**; 30 days 336 ms; hot product 188 ms | `PostedAt DESC, DocumentId, Id` + `IX_StockDocument_PostedAt` + lines cover: 426 reads, **0 ms** each | lines of two documents posted in the same instant swap places. This is the one that closes the last P95 violation |
| **R4** document search `No LIKE '%x%'` | 961 reads, 88 ms | `LIKE 'x%'`: 23 reads, 4 ms | contains becomes starts-with (UI wording) |

Owners: be-framework to apply R2/R3 in `StockRowsSpec` / `MovementsPageSpec` / `MovementsSpec` and the DBA to ship the matching migration in
the same change; the product owner to accept the tie order. Also noted: a `Contains` search is a scan by nature (50,000 products, about 150-400 ms);
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
7. To compare with the old index set: `dotnet ef database update 20261009151059_AuditColumns` (the migration's `Down`), measure, then update again.
8. Load tests: `tests/load/README.md`.

## 10. Files

| File | Purpose |
|---|---|
| `01-seed-perf.sql` | set-based volume seed (130k, 1M or 2M ledger rows) |
| `02-measure-api.ps1`, `compare-results.ps1` | endpoint latency probe and before/after table |
| `03-index-usage.sql`, `04-query-store-top.sql` | which indexes are read and what they cost; the real EF statements, worst first |
| `05-backup-restore.sql`, `06-sizes.sql`, `07-maintenance.sql`, `08-reconcile-drill.sql` | recovery and upkeep (section 8) |
| `09-rewrite-candidates.sql`, `summarize-stats.awk` | EF statement versus rewrite (R1-R4) |
| `10-filtered-vs-unfiltered.sql` | why the indexes are filtered on `REC_DELETED = 0` |
| `11-snapshot.sql`, `11b-snapshot-diff.sql` | deadlock and lock-wait counters around a load test (used by `tests/load/run-scenario.ps1`) |
| `results-1m-before.csv`, `results-1m-after.csv` | the comparison in section 3 (`-before-run1` = first, colder baseline; `-full-proposal` = all 13 proposed index changes applied) |
| `results-100k-after.csv` | the 130k tier on the final code |
