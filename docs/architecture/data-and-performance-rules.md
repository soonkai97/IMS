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
  size is enforced while streaming; PDFs with scripts, launch actions or embedded files are refused.
- Access is decided per owner by an `IAttachmentOwnerPolicy` (warehouse scope for documents, master-data permissions for products).
- Downloads are sent inert: `nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Cache-Control: private, no-store`.
- Back the storage folder up together with the database (docker-compose mounts it as a volume).

## 8. Paging contract

Every list that can grow returns `{ items, total, page, pageSize, nextCursor }`. Sort keys are enums. Small lookup lists (units, categories,
warehouses, approval rules) return plain arrays with a hard cap.

## 9. Indexes

Measured results, rejected proposals and how to re-run them: `docs/dba/README.md`.

- Indexes on soft-deleted tables are filtered `WHERE [REC_DELETED] = 0` (EF adds `REC_DELETED = 0` to every query, so the filtered index answers it
  for free); the append-only tables (ledger, audit log) have no query filter and get unfiltered indexes.
- An index needs a named consumer: it must show reads in `docs/dba/03-index-usage.sql` after a realistic run. Foreign-key indexes EF creates
  by default are kept; extra indexes on the posting path (ledger, document lines) need a measured read win.
- Never combine a column of the main table and `EXISTS` on another table in one `OR` (a text search over SKU, name and barcode): SQL Server probes
  the other table once per row. Resolve the exact-match side first and pass its id (`ProductSearch`).
- A sort that an index should serve ends in a unique key (`Id`, or the primary key), not in a column of another table.
- After a migration that adds columns to a big table, rebuild the indexes (`docs/dba/07-maintenance.sql`) before measuring or going live.
