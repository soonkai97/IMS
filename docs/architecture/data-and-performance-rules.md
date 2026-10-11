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
- Low stock has one definition (`ILowStockReader`, `LowStockQuery`): low balances plus active products with a safety level that have no balance row in any warehouse. The list, its total, the Home count and the export all read it; the never-stocked branch is an anti-join on the balance primary key (`NOT EXISTS`), so its cost grows with the products that have a safety level, not with the ledger.
- After a migration that adds columns to a big table, rebuild the indexes (`docs/dba/07-maintenance.sql`) before measuring or going live.
- `IX_StockDocument_PostedAt` (filtered on live, posted rows; includes Status, WarehouseId) is read by the Home counts ("posted today", `GET /api/documents/counts`) and by the movements report (page, newest posting first; the report repeats `PostedAt IS NOT NULL` so this filtered index applies, and it needs the lines index above, otherwise the date-filtered report gets slower). The other counts
  (drafts, returned, approvals waiting) are served by `IX_StockDocument_Status_Type_Id`: they select a Status that only a handful of rows have.

## 10. Time

- Every instant is stored and transmitted in UTC (BR-10). EF reads `datetime2` back with `Kind = Utc` (`UtcKindConverter`, applied to every `DateTime` property by convention) and the JSON
  converter writes `yyyy-MM-ddTHH:mm:ss[.fffffff]Z`; nothing on the wire is local or zone-less. Only exports and the document-number month convert to Asia/Kuala_Lumpur (+08:00).
- "Today" for counts and date filters is the Kuala Lumpur calendar day, expressed as a UTC range (`Paging.StartOfDayUtc`).

## 11. Money

Product-owner decision: **money has exactly 2 decimal places and never a fraction of a cent.** Money is a unit cost, an average cost, a line amount, a document total, an estimated amount, a stock value and an approval threshold. Quantities are not money and keep the precision of their unit (4 decimals). The rule is written once, in `Ims.Domain.Money`.

| Step | Rule |
|---|---|
| Input | A unit cost (documents, draft edits, the opening-stock import) and an approval amount threshold have at most 2 decimals; `12`, `12.5` and `12.50` are fine, `12.505` is `400` "Unit cost can have at most 2 decimal places." (`Messages.CostTooPrecise`; "An amount can have at most 2 decimal places." for a threshold). Enforced twice: FluentValidation on the request and a domain rule (`StockDocumentLine.Create`, `ApprovalRule.Update`). In an import file it is a row error. |
| Line amount | `round(base quantity x unit cost per base unit, 2)` (`StockDocumentLine.Amount`, `Money.Amount`). |
| Document total | The **sum of the rounded line amounts**, not the rounded sum: three lines of 0.015 are 0.02 + 0.02 + 0.02 = 0.06. Approval rules compare this total (a rule fires at `total >= threshold`), so an estimate of 4,999.9996 is 5,000.00 and reaches a 5,000.00 rule. |
| Stock value | `round(quantity x average cost, 2)` per stock row; the stock total is the sum of the rounded rows, so the list and the Home tile always add up. |
| Rounding mode | **Half away from zero** (`MidpointRounding.AwayFromZero`): 0.005 is 0.01, -0.005 is -0.01. One mode everywhere. In queries the rounding is `Math.Round(x, Money.Decimals)`, which EF translates to SQL `ROUND` (also half away from zero on a decimal); `MoneyPrecisionTests` (an average of 10.005 gives a value of 10.01) pins SQL and C# together at the half-cent boundary. |
| Inside the system | A cost per base unit (document line, ledger) and the moving average keep more places than 2 (they are not amounts: a carton price spread over 12 pieces is 0.8333...). They are stored with more places than 2 (their precision is stated with the costing rule in `docs/architecture/backend.md`); the average is not rounded before it is used, so repeated receipts do not drift. |
| Outside the system | Every API money value (`unitCost`, `avgCost`, `value`, `totalValue`, `totalAmount`, `amount`, `amountThreshold`, trigger `actual` and `threshold` of an amount rule) is rounded to 2 decimals in the query that produces it, and once more by `ResponseShaper` (the pipeline step that already withholds cost: a value tagged `[VisibleWith]` is money, so what stays visible is rounded to a cent whatever produced it). A displayed `avgCost` times a quantity can differ by a cent from `value`, because `value` uses the unrounded average. |
| Excel | A column declared `IsMoney` (`ReportColumn`) is rounded again by the writer and given the number format `#,##0.00`; `CostVisibilityCoverageTests` fails when an export column that looks like money is not declared. |
| Database | Money columns that hold amounts are `decimal(18,2)` (approval thresholds); nothing else stores an amount, so no migration was needed. |
