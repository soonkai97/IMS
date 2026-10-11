# Data access, concurrency and performance rules

These are standing rules for every feature. Most are enforced by tests (architecture tests, integration tests, the smoke test).

## 1. Standard audit columns on every table

Every table carries the same six columns. They are properties of the Domain base class `AuditableEntity` and are stamped by the
persistence layer on save, never by use cases.

| Column | Meaning |
|---|---|
| `REC_DELETED` | Soft-delete flag. Rows are never physically deleted (an archive job may remove them later). |
| `REC_CREATOR` | Id of the user who created the row. Null when the system did (seed, background work). |
| `REC_CREDATE` | Creation time, UTC (database default `SYSUTCDATETIME()`). |
| `REC_REVISOR` | Id of the user who last changed or deleted the row. |
| `REC_REVDATE` | Time of the last change or delete, UTC. |
| `ROW_VERSION` | `rowversion`: optimistic-concurrency token. |

- A delete of a tracked row becomes `REC_DELETED = 1`. A global query filter hides deleted rows from every query.
- Unique indexes are filtered `WHERE [REC_DELETED] = 0`, so a deleted barcode, SKU or link row never blocks its re-use.
- The stock ledger and the audit log carry the columns but are never updated or deleted (triggers enforce it) and have no filter.
- `AuditableEntity` is in `Ims.Domain/Common/Models`; the mapping is `ConfigureAuditColumns` in `AppDbContext`.
- Row-level audit (the `AuditLogs` table) records before/after values for master and security data. Business events (posting, approval,
  login) are recorded explicitly. Both commit in the same transaction as the change.

## 2. SQL injection

- All data access is EF Core LINQ or Ardalis specifications: values are always parameters. Raw SQL and ADO.NET commands are banned
  (`DataAccessSafetyTests`); the one exception is the lock helper, which uses `SqlQuery<int>($"...")` (interpolated = parameterised).
- Sort keys are enums (whitelists); text filters use `Contains`/`StartsWith`, which EF escapes.

## 3. No cartesian explosion

- The DbContext uses split queries by default: each collection is its own query, rows are never multiplied.
- A single query that includes two or more collections throws (`MultipleCollectionIncludeWarning` is an error); `AsSingleQuery()` is banned
  by an architecture test.
- Read models use reference joins (many-to-one) and aggregates (`SUM`, `COUNT`) in SQL, not loaded collections.

## 4. Complexity (aim for O(1) per request, never O(n) in the data)

| Need | How |
|---|---|
| Current stock of a product | one row in `StockBalance` (read model kept by posting), not a sum over the ledger |
| Next document number | one sequence row per type/warehouse/month |
| Lookup by SKU / barcode / document number / id | unique or primary-key index seek |
| Deep paging of ledger, documents, audit | seek (cursor) paging: `afterId`, constant cost at any depth |
| Page numbers (OFFSET) | refused beyond 10,000 rows; use a cursor or a narrower filter |
| Row count | skippable (`includeTotal=false`); the count is the only O(n) part of a list |
| Summaries and totals | aggregated by SQL Server into one row (`IStockSummaryReader`) |
| Exports | capped at 50,000 rows and streamed to a file |
| Settings | read once per request (scoped memoisation) |
| In-code lookups | `Dictionary` / `HashSet`, never `List.Contains` or `FirstOrDefault` inside a loop |
| Unpaged lookup lists | hard cap of 1,000 rows |

No N+1 queries: a use case issues a fixed number of queries regardless of how many rows it touches.

## 5. Concurrency and deadlocks

- Optimistic concurrency (`ROW_VERSION`) everywhere.
- Hot data is serialised with short transaction-owned application locks (`ITransactionLocker`): postings per (warehouse, product), document
  numbers per (type, warehouse, month). Locks are always taken in a fixed order (ascending product id), so they cannot form a cycle.
- `IUnitOfWork.ExecuteAsync` retries the whole transaction on a deadlock victim (1205), lock timeout (1222) or key race, with randomised
  back-off (max 5 attempts). `READ_COMMITTED_SNAPSHOT` is on, so readers never block writers.
- No blocking async calls (`.Result`, `.Wait()`): architecture test.

## 6. Timeouts

- HTTP: 30 s default, 120 s for exports, imports and file transfers (504 ProblemDetails). The request token cancels the running SQL command.
- SQL command timeout 60 s (longer than the default HTTP timeout on purpose); lock wait 10 s.
- Kestrel: headers 15 s, keep-alive 120 s, JSON body 1 MB, uploads 12 MB.
- A client that disconnects is not an error (499, not logged as one). Database unavailable is 503, not 500.

## 7. Files (images and documents)

- Bytes are stored as physical files under `Storage:RootPath`; the database keeps only metadata (`Attachment`).
- The type is decided from the file's bytes (JPEG, PNG, WebP, PDF), never from the client's name or content type; the stored name is generated;
  size is enforced while reading (never more than 10 MB in memory); PDFs with scripts, launch actions or embedded files are refused.
- Images are stored without metadata (EXIF/GPS, XMP, IPTC, comments, bytes after the end marker) and a damaged image is refused
  (`ImageMetadataStripper`); every upload then passes `IUploadScanner` (default no-op; plug a malware scanner in there) before it reaches storage.
  Details: `docs/security/upload-scanning.md`.
- Access is decided per owner by an `IAttachmentOwnerPolicy` (warehouse scope for documents, master-data permissions for products).
- Downloads are sent inert: `nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Cache-Control: private, no-store`.
- Back the storage folder up together with the database (docker-compose mounts it as a volume).

## 8. Paging contract

Every list that can grow returns `{ items, total, page, pageSize, nextCursor }`. Sort keys are enums. Small lookup lists (units, categories,
warehouses, approval rules) return plain arrays with a hard cap.

The order of a list is part of its contract: it is total (it ends in unique keys), so a walk through the pages never skips or repeats a row,
and the export of a list uses the very same order as its pages. The receipt/issue detail (`/api/reports/movements` and its export) is
`PostedAt DESC, DocumentId DESC, line Id` (`MovementOrder`); rows of documents posted at the same instant therefore appear newest document first, and the lines of one document in entry order.
A reversed document adds a *reversal row* per line, dated by `StockDocument.ReversedAt`; the page merges the two kinds by `At DESC, DocumentId DESC, LineId, IsReversal DESC` (`IMovementReader`). Each kind is cut to its first `offset + size` rows in its own order before the merge, so the posting rows still come from a backward scan of `IX_StockDocument_PostedAt` and the (few) reversal rows never make them be sorted in full. The order of the posting rows relative to each other is unchanged and no existing index was touched; the reversal branch has its own narrow index (`IX_StockDocument_ReversedAt`, section 9).

## 9. Indexes

Measured results, rejected proposals and how to re-run them: `docs/dba/README.md`.

- Indexes on soft-deleted tables are filtered `WHERE [REC_DELETED] = 0` (EF adds `REC_DELETED = 0` to every query, so the filtered index answers it
  for free); the append-only tables (ledger, audit log) have no query filter and get unfiltered indexes.
- An index needs a named consumer: it must show reads in `docs/dba/03-index-usage.sql` after a realistic run. Foreign-key indexes EF creates
  by default are kept; extra indexes on the posting path (ledger, document lines) need a measured read win.
- Never combine a column of the main table and `EXISTS` on another table in one `OR` (a text search over SKU, name and barcode): SQL Server probes
  the other table once per row. Resolve the exact-match side first and pass its id (`ProductSearch`).
- A sort that an index should serve ends in unique keys (`Id`, or the primary key), not in a column of another table. A report that lists child
  rows newest-parent-first (movements: document lines by `PostedAt` of the document) sorts by the parent's column, then the parent id, then the
  child id, so a backward scan of an index on the parent's column streams the first rows instead of sorting every row
  (`IX_StockDocument_PostedAt`). A filtered index skips rows its filter excludes only if the query states the same predicate
  (`PostedAt IS NOT NULL`, in `MovementFilters`).
- The child side of such a report keeps its foreign-key index and gets one narrow filtered index (`IX_StockDocumentLine_DocumentId_Live`, so the
  count and page answer EF's `REC_DELETED` predicate without reading the table). A covering index on the lines (36 MB, on the posting path) was
  measured and rejected for lack of need; see `docs/dba/README.md` section 5 for when to revisit it.
- `IX_StockDocument_ReversedAt` (filtered on live rows with a reversal time; includes Status, Type, WarehouseId, PartnerId, No) is read by the reversal branch of the movements report: a backward seek on the reversed documents instead of a lookup per reversed document. It is written only when a document is reversed, never on the posting path. Measured on the 130k-ledger-row seed with 500 reversed documents (2.5 % of posted): first page 79 reads of `StockDocument` before, 18 after (the page query as a whole 438 reads before, 225 after); page at offset 5,000: 19,102 + 1,753 reads before, 18,009 + 1,584 after. Not measured at 1M rows (the posting branch is the same query as before).
- The audit log is searched by index, never by scanning: the object filter resolves a typed fragment to a short list of known entity names (`AuditEntities.Known`, `IN` on `IX_AuditLogs_EntityType_EntityId`), the entity id (document number), user and action filters are prefix matches (`LIKE 'x%'`) served by `IX_AuditLogs_EntityId_Id`, `IX_AuditLogs_Actor_Id` and `IX_AuditLogs_Action_Id`, all ending in `Id DESC` so the newest page is the first rows of a backward seek. A `Contains` (`LIKE '%x%'`) on the audit table is not allowed.
- Low stock has one definition, `Ims.Domain.LowStockRule` (written as expressions the database evaluates), and **low means on-hand at or below the safety level** (`mvp-ux.md` 3.8: a product leaves the list when a receipt brings it ABOVE its safety stock). A balance in an ACTIVE warehouse for an ACTIVE product is low when its quantity is below zero, or when it has a safety level above zero and the quantity is `<=` it. Never stocked is decided PER WAREHOUSE: an active product with a safety level and no balance row in active warehouse W is low in W (quantity 0, no average cost: `avgCost` and `value` are null, not 0) for every caller who can see W; stock in another warehouse, or an old row in an inactive one, says nothing about W, and a scoped caller's answer never depends on a warehouse outside their scope (a product received in A is still low in B for the user of B). The two sets are built once, in `LowStockSets` (low balances; never-stocked pairs), and the readers only add their filters and shape the rows: the low-stock list, its total, the Home count and the export (`ILowStockReader`, `LowStockQuery`) and the stock list with `lowOnly=true` and its export (`ILowOnlyStockReader`, `EfLowOnlyStockReader`); the `isLow` flag of a stock row takes its verdict from the same expression (`LowStockRule.Project`), and `LowStockOneDefinitionTests` / `LowStockPerWarehouseTests` compare them. The never-stocked set is a (product x active warehouse in scope) anti-join on the balance primary key (`NOT EXISTS (ProductId, WarehouseId)`): one seek per pair, so its cost grows with products-with-a-safety-level x warehouses, not with the ledger. Worst first: largest shortfall as a share of the safety level, then in units, then SKU and warehouse code.
- The document search box (`GET /api/documents?search=`) matches the document number, the remark or the partner name, ignoring case. **Measured on a scratch database of 1,001,800 documents** (269,019 with a remark of 45 characters on average, 84,144 containing the dense term "delivery", 20 containing a rare one, 622 partners of which 541 contain "sdn bhd"; `docs/dba/01-seed-perf.sql` plus a set-based top-up of documents; SQL Server 2025 Express on a shared 8-thread laptop, so absolute times varied 2-3x between runs and the figures below are the minimum of 3 back-to-back runs and logical reads, which do not vary):
  - **The cost is the comparison, not the reading.** A contains-match must look at every row (it cannot seek), and under the database's linguistic collation (`SQL_Latin1_General_CP1_CI_AS`) a case-insensitive `LIKE '%x%'` costs about ten times as much CPU as the same scan by code point: a term that matches nothing took 3.1 s of CPU under the collation and **0.41 s** compared as `LOWER(col) COLLATE Latin1_General_100_BIN2 LIKE lowered pattern`; the dense term 5.0 s and 0.42 s (remark alone 0.83 s, number alone 1.4 s under the collation). Both sides are lower-cased so it stays case-insensitive (tested with accented letters). It lives in `EfDocumentTextMatcher` (Infrastructure) behind `IDocumentTextMatcher`; the Application layer only asks for "contains". A count and a page of a term that matches nothing each scan the table once, so a no-match term now costs about 2 x 0.4 s at 1M documents; `includeTotal=false` halves it. It is NOT the default for a text search: the web client has no way to show an unknown total yet, so the default stays on; the web client should send `includeTotal=false` for searches (see HANDOFFS).
  - **The partner side: a short list of ids, or a join, never a cap.** #46 resolved the ids of the partners whose name contains the text, cut the list at 200 and silently dropped the rest (a "sdn bhd" search with 350 matches missed the documents of partner 300). An `IN` list costs more with every id, measured with the case-folded test and a term that matches nothing (1.0 = no partner arm, 1.4 s of CPU): 5 ids 1.4x, 50 ids 2.7x, 200 ids 5.3x, 541 ids 7.1x (22 s under the collation); `IN (SELECT .. FROM Partner)` inside the OR 11 s (a probe per document). A `LEFT JOIN Partner` costs about 1.35x in a count whatever the number of matching partners, but inside the page query (`TOP 25 .. ORDER BY Id DESC`) the optimiser walks the documents newest first and probes `Partner` for each one: a term that matches nothing then read 1,468,993 pages instead of 26,736. So `EfDocumentTextMatcher` uses both and each where it is cheap: up to `Limits.SearchPartnerListMax` (50) matching partners, an id list on the document itself (no join in the scan); with more, the name is tested through the join. More than 50 partners matching means their documents match too, so the page finds its 25 rows after a few hundred reads and the join's weakness (nothing matches) cannot occur. Both forms find exactly the same documents, nothing is capped or refused (`DocumentSearchTests.Documents_of_every_matching_partner_are_found_however_many_partners_match`: 50, 51 and 350 partners; the last partner's documents are found). The partner arm still makes the COUNT dearer (11 partners 2.5 s of CPU, 541 partners 2.7 s, against 0.66 s for number and remark only): `includeTotal=false` skips it and the page then answers in 80-90 ms.
  - **API timings after the change** (Release build, p50 of 4 warm calls, the same 1,001,800 documents, shared machine, admin; scoped to one warehouse is within 0.5-2x): a term that matches nothing 2.2 s with the total, 0.73 s without (the page and the count are each one scan: 0.7 s and 0.66 s of CPU, 26,736 pages); a rare term 1.4 s / 0.66 s; the dense term "delivery" 1.06 s / 38 ms; a name matching 11 partners 2.5 s / 87 ms; a name matching 541 partners (60 % of all documents) 4.5 s / 80 ms. #46 as shipped, the same calls on the same data (this machine was busier then, so take a factor of 2 off): no match 6.4 s / 3.2-7.2 s, rare 6.1-8.6 s / 2.1 s, dense 2.1-3.9 s / 13 ms, a name matching the first 200 of 541 partners 3.8-6.1 s / 22 ms. So with the total the worst terms still cost 2-4 s at 1M documents and without it every search is under 1 s except a term that matches nothing or almost nothing (0.7 s); the web client should send `includeTotal=false` with a text search.  - **A text of fewer than 3 characters only matches the START of the document number** (`Limits.SearchMinTextLength`, `LIKE 'ab%'`, a seek on `IX_StockDocument_No`): a one- or two-letter contains-match reads a million remarks and names for matches nobody can use.
  - **`IX_StockDocument_Search` is removed (it was added in #46).** Candidates measured at 1M documents, case-folded predicate, admin / scoped to one warehouse, a term that matches nothing: no index: count 26,736 reads, 1,469 / 1,047 ms CPU; `(No) INCLUDE (PartnerId, Remark)`: 10,523 reads; `(No) INCLUDE (PartnerId, Remark, WarehouseId)`: 11,025 reads, 1,438 / 390 ms CPU (within the noise of the first line). Adding `WarehouseId` did not change the reads, only helped a scoped count a little. The page of a term that matches nothing or is rare reads the clustered index backwards (26,737 reads) with or without it: the optimiser expects to find 25 matches quickly and never uses the narrow index for the page. So the index cuts the logical reads of the COUNT 2.4x and nothing else, costs 86 MB (41 % of the 209 MB table) and a wider write on every document insert and every edit of a remark, and the CPU, which is what a warm server spends, stays the same. Not worth it: no index. If the table ever does not fit in memory, revisit with a cold-cache measurement.
  - Not solved by any of this: a contains-match over a million remarks is O(documents) (about 0.4 s of CPU per scan). Past a few million documents move the remark search to SQL Server Full-Text (not available on LocalDB/Express) or to prefix search, or require a date range with a text search (the date filter seeks `IX_StockDocument_CreatedAt` and the text is then tested on that window only).
  - Migration `AddDocumentReversedBy` (`ReversedBy` column, its foreign-key index, backfill from the reversal ledger entries): `Up` took 10 s on the 1M-document copy on a quiet machine and 33 s on a loaded one (including 4 s of tool start-up): no maintenance window needed beyond a minute, the table is locked only for the duration.
- The balance after a ledger row (`LedgerRow.BalanceAfter`) is **stored on the row** (`StockLedgerEntry.BalanceAfter`, `decimal(18,4)`), written when the posting or the reversal inserts the entry (`PostingContext.StampBalances`), under the same per-(warehouse, product) lock and in the same transaction as the balance it follows, so it cannot be overtaken by another posting (the derived version of #46 ran three statements outside one snapshot and could be one posting off) and a page of any depth reads it from the row: no aggregate, no extra query, `ILedgerBalanceReader` is gone. The row is still never updated (the trigger stays). It is returned only when the ledger is read for one product (`productId`), as before. Measured on a ledger of 1,499,484 rows with one product of 500,020 rows (the review's hot product), per request, ON TOP OF the page itself (4 reads): the derived version summed everything newer than the page: a page in the middle (250,000 newer rows) 3,492 reads / 230-310 ms CPU, the last page (500,000 newer rows) **20,506 reads / 700-780 ms CPU** (those rows are one contiguous block here; in a real ledger they are interleaved with other products and the scan reads more); that is not "constant cost at any depth". Option (a), an index `(ProductId, WarehouseId, Id) INCLUDE (QtyDelta)`, is 45 MB (28 % of the 163 MB table, written by every posting) and only halves it: 1,926 reads / 250-280 ms CPU in the middle, 375-405 ms at the end, still linear. Option (b), summing from the shorter side, is still linear in the worst case (the middle). Option (c), stored (chosen): the page query is 9 logical reads and 0 ms CPU at every depth. Cost of (c): 9 bytes a row and one value assigned at insert. Migration `AddLedgerBalanceAfter` backfills the old rows with a running sum (`SUM(QtyDelta) OVER (PARTITION BY ProductId, WarehouseId ORDER BY Id)`) with the append-only trigger switched off for that one statement inside the migration's transaction (verified afterwards: 0 differences between the last `BalanceAfter` of every (product, warehouse) and `StockBalance`, trigger enabled, tested in `MigrationTests`). **Maintenance window**: on the 1,499,484-row ledger `Up` took 62 s (loaded machine, including 4 s of tool start-up; about 40 us a row, so roughly 4 minutes at 6 million rows), it holds a schema lock on `StockLedgerEntry` (no posting during it), and it leaves the table 98 % fragmented and twice the size (163 MB to 326 MB) because every row grew: run `docs/dba/07-maintenance.sql` (or `ALTER INDEX ALL ON StockLedgerEntry REBUILD`, 38 s here, back to 154 MB) straight after. Plan for about a minute per million ledger rows plus the rebuild.
- After a migration that adds columns to a big table, rebuild the indexes (`docs/dba/07-maintenance.sql`) before measuring or going live.
- `IX_StockDocument_PostedAt` (filtered on live, posted rows; includes Status, WarehouseId) is read by the Home counts ("posted today", `GET /api/documents/counts`) and by the movements report (page, newest posting first; the report repeats `PostedAt IS NOT NULL` so this filtered index applies, and it needs the lines index above, otherwise the date-filtered report gets slower). The other counts
  (drafts, returned, approvals waiting) are served by `IX_StockDocument_Status_Type_Id`: they select a Status that only a handful of rows have.

## 10. Time

- Every instant is stored and transmitted in UTC (BR-10). EF reads `datetime2` back with `Kind = Utc` (`UtcKindConverter`, applied to every `DateTime` property by convention) and the JSON
  converter writes `yyyy-MM-ddTHH:mm:ss[.fffffff]Z`; nothing on the wire is local or zone-less. Only exports and the document-number month convert to Asia/Kuala_Lumpur (+08:00).
- "Today" for counts and date filters is the Kuala Lumpur calendar day, expressed as a UTC range (`Paging.StartOfDayUtc`).
