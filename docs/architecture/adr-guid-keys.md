# ADR: GUID keys for every table?

Status: Accepted 2026-10-12: option 1, the hybrid (integer clustered keys kept, a unique `PublicId` GUID on the tables users address, typed codes beside it). Owner decisions: D1 = no (no offline creation), D2 = yes (ids must not be guessable or countable in URLs and responses). The owner's reason for GUIDs: URLs with sequential numbers are easy to enumerate. Implementation notes from the owner conversation: `PublicId` is a RANDOM GUID (version 4, not sequential) so that two ids reveal nothing about how many records exist between them; being a non-clustered unique index it does not fragment a clustered key; authorization and warehouse-scope checks (404 for out-of-scope ids) remain the real protection, the GUID only removes enumeration. Author: be-architect.
Inputs: the code at `docs/2026-refresh-specs` (HEAD 7e8fb4c), `docs/architecture/*`, `docs/dba/README.md`, `docs/design/2026-refresh-plan.md`.
Evidence: the DBA benchmark in `docs/dba/guid-pk/` had produced the baseline variant C (measured) and the GUID generator check, but no A or B variant when this was revised; section 7 lists what is measured, what I estimated and the predictions to check.

## 0. Decision in short

Read the section "Owner preference: GUID primary keys plus user-defined unique code" first; it holds the owner's latest answers, the costed options and the recommendation. Summary of the whole note:

1. Give the owner what a GUID preference is for: every addressed row has an immutable GUID id in the API, URLs and logs, with the typed code (Sku, Code, Username, document No) beside it. That is **option 1, the hybrid**: keep the int (bigint on ledger and audit) clustered keys, add a unique `PublicId` GUID on the 9 tables a client addresses (User, Warehouse, Partner, Category, Uom, Product, ApprovalRule, StockDocument, Attachment).
2. Do **not** put a GUID primary key on the ledger, audit log, lines or balances in any form. Measured baseline at 1M ledger rows: ledger plus indexes 357 MB; GUID foreign keys make it about 520 MB (+46 %), a literal GUID key with order kept about 650 MB (+81 %).
3. A sequential GUID is not leak-free: EF's counter makes consecutive ids differ by one (the DBA's sample: `...0390`, `...0391`, `...0392`), so two ids reveal how many were created between them. A random GUID hides that but fragments a clustered index. Only the hybrid is index-friendly and leak-free at once. That settles D2 in favour of option 1.
4. Cursors become opaque (encrypted) strings; `id` leaves the ledger and audit responses.
5. D1 = no: client-minted ids and idempotent create (section 2.3) become optional (PR-7b).
6. Cost: option 1 about 85 agent-hours (65 to 105), 12 PRs; option 2 (GUID everywhere except ledger and audit) 95 to 105; option 3 (every table) about 125 and a baseline reset.
7. Option 1 is additive (`AddPublicIds` migration, `Down` drops it): dev, QA and CI databases are migrated, not rebuilt.
8. The one question left for the owner is in section 8.

## Owner preference: GUID primary keys plus user-defined unique code

Revision of 2026-10-11 (after the owner's answers). D1 (offline creation or data exchange between deployments within a year): **no**. The owner restates the original wish: every table has a GUID primary key, and every table users address also has one extra unique column holding a value users type or read (Sku, document number, warehouse code, partner code, username). D2 (scoped users must not infer other warehouses' activity from ids) is still open; I assume "ids should not leak" and show what each option gives.

What D1 = no changes: the reasons for client-minted ids, cross-database identity and offline queues fall away as requirements. Idempotent create (section 2.3) stays useful against double taps and retries but becomes optional (PR-7b, 4 hours, can be dropped). What remains of the case for GUIDs is the owner's preference, non-guessable ids and D2.

### Evidence used (and what is still missing)

`docs/dba/guid-pk/results/` now holds the **baseline variant C only** (integer keys, 1M tier: 1,003,384 ledger rows, 176,144 documents, 1,057,321 lines, 352,112 audit rows, 250,000 balances, 50,000 products; SQL Server 2025 Express, 8 CPUs). Variants A1 to A3, B1 and B2 had not produced results when this was written. So: C sizes and reads below are **measured**; every A and B figure is **my estimate** (C measured size scaled by the change in key width, +-15 %), with the prediction the DBA data should confirm or refute. Two A/B facts are measured: `results/ef-guid-generators.txt` (200,000 values each, share of adjacent pairs ascending in SQL Server order: EF `SequentialGuidValueGenerator` 100.00 %, `Guid.CreateVersion7` 49.95 %, `Guid.NewGuid` 50.02 %), which confirms section 2.1. Measured C baseline of the three tables that matter: ledger 357 MB (clustered 110 MB, five indexes 247 MB, 45,663 pages), lines 275 MB, documents about 131 MB with 12 indexes. This SQL Server instance reports a memory target of about 40 GB, so my earlier remark about the Express buffer-pool cap does not apply to this machine and I withdraw it; memory cost below means working-set growth, cold reads and backup time.

### The user-defined unique code

Most tables already have it. A code is a business value users change (a SKU is editable), so it is never a foreign key and never the identity in a URL; the id is immutable. Uniqueness follows the existing rule (unique among live rows, filtered on `REC_DELETED = 0`).

| Table | Unique user-defined value today | Action |
|---|---|---|
| User | `Username` (and `Email`) | none |
| Warehouse | `Code` | none |
| Uom | `Code` | none |
| Product | `Sku` (barcodes are in `ProductBarcode.Barcode`, unique) | none |
| StockDocument | `No` (BR-11, immutable) | none |
| Category | `Name` | none |
| Partner | `Name`, unique per type | add an optional unique `Code` (null allowed, filtered unique index), 3 hours, backend, API, form and import template |
| ApprovalRule, Attachment | none | no code, as the owner said |
| SystemSetting | `Key` (natural primary key) | none |
| StockLedgerEntry, AuditLog, StockDocumentLine, StockBalance, RefreshToken, ProductBarcode, UomConversion, UserWarehouse, DocumentSequence | none (children, history or internal) | none |

So "GUID plus a typed code" costs almost nothing extra: one new column. It does not decide the key strategy.

### Why a GUID primary key cannot be both index-friendly and leak-free

A primary key that is also the public id is visible to every user, so its generator matters twice.

| Generator | Index-friendly in SQL Server (measured) | Reveals volume or time to a user holding two ids |
|---|---|---|
| EF sequential (client side) | yes, 100 % ascending | **yes, volume.** It writes a counter that grows by one per generated id into the sortable bytes. The DBA's sample ends `...-0390-08df274032d0`, `...-0391-...`, `...-0392-...`: three consecutive ids differ by exactly one. A warehouse-scoped user who holds two of their own document ids learns how many documents were created in that table by that process between them, across all warehouses. That is the same leak as integer ids |
| `NEWSEQUENTIALID()` | yes | similar (a server counter) |
| `Guid.NewGuid()` (v4) | no, 50 % | nothing |
| `Guid.CreateVersion7()` (v7) | no, 49.95 % (stamp in the least significant groups) | creation time only (already visible as `createdAt`) |

So an owner who wants GUID primary keys **and** non-leaking ids must choose: index-friendly with a volume leak, or leak-free with a randomly-ordered clustered index. Only the hybrid (random or v7 `PublicId` in a narrow secondary index, sequential internal clustered key) has both. For D2 this is the deciding fact.

### Split by table class: what a GUID clustered primary key costs

Method: C measured size per row, plus the added key bytes in the row and in every index (each index row carries the clustered key and its own key columns). "GUID FKs" = foreign keys to a GUID parent are 16 bytes. Megabytes added at the 1M tier (1,003,384 ledger rows) and at a 3M tier (all tables scaled by three, products, warehouses and balances unchanged). Estimates.

| Class | Tables | Rows at 1M / 3M | Added with GUID PK and GUID FKs, 1M / 3M | Insert cost | Verdict |
|---|---|---|---|---|---|
| M: master and configuration | User, Warehouse, Partner, Category, Uom, Product (+ barcodes), ApprovalRule, UomConversion, UserWarehouse, SystemSetting, RefreshToken | at most 50 k each | +11 MB / +11 MB (product and barcode indexes carry 16-byte keys) | negligible (sequential generator) | Acceptable. Harmless to the database |
| T: modest transactional | StockDocument, Attachment | 176 k / 530 k documents | +63 MB / +190 MB. 11 id columns per document row become 16 bytes (+132 bytes) and its 12 indexes carry a 16-byte key (+25 MB at 1M) | one page per insert if sequential; random if the client mints the id | Acceptable but not free. Needs keyset paging (below) |
| L: lines and balances | StockDocumentLine, StockBalance | 1.06 M / 3.2 M lines, 250 k balances | lines +220 MB / +660 MB (275 MB becomes about 495 MB: row 99 to 171 bytes, four indexes each about twice as wide); balances +15 MB. Balance key 8 bytes becomes 32 bytes in every index row | lines: 5 rows per document, each now 1.7 times the bytes on the posting path | **Not worth it.** The largest relative growth for the least benefit; no endpoint addresses a line |
| V: high-volume append-only | StockLedgerEntry, AuditLog | 1.0 M / 3.0 M ledger, 352 k / 1.06 M audit | ledger +163 MB / +490 MB with a bigint key and GUID FKs (357 MB becomes about 520 MB, +46 %); +289 MB / +870 MB with a GUID clustered key and `Seq` kept for order (+81 %); audit +9 MB / +27 MB (its two user columns) or +45 MB / +135 MB with a GUID key | ledger insert writes about 1.5 to 1.8 times the bytes (284 bytes of row plus indexes today, 416, or 515) | **No.** Append order is the data model; section 2.6 |

Totals: **GUID PKs on all but ledger and audit (option 2, GUID FKs): about +480 MB (+45 % of the 1,061 MB allocated) at 1M, +1.4 GB at 3M. On every table including ledger and audit (option 3): about +640 MB (+60 %) at 1M, +1.9 GB at 3M.** Backup and restore time scale with allocated size (C measured: backup 1,061 MB in 8.3 s, restore 5.3 s). The hybrid adds about +8 MB (+0.8 %).

### The ledger in detail (the owner's biggest cost)

A ledger row today carries six id columns: `ProductId`, `WarehouseId`, `SourceDocId`, `CreatedBy`, `REC_CREATOR`, `REC_REVISOR`. Each index repeats some of them. Per row (model at full pages; the measured C total is 357 MB, which includes the fragmentation of the load):

| Variant | Row bytes | Index bytes (5 indexes) | Total per row | Ledger at 1M | at 3M | Order kept? |
|---|---|---|---|---|---|---|
| C: bigint key, int FKs (today) | 113 | 171 | 284 (1.00) | 357 MB (measured) | about 1.07 GB | yes |
| bigint key, FKs to **GUID** parents | 185 | 231 | 416 (1.46) | about 520 MB | about 1.57 GB | yes |
| bigint key, FKs to the parents' **int alternate keys** | 113 | 171 | 284 (1.00) | 357 MB (+1 MB of parent keys) | about 1.07 GB | yes |
| GUID clustered key sequential, GUID FKs, no `Seq` | 193 | 271 | 464 (1.63) | about 580 MB | about 1.75 GB | **no** |
| GUID clustered key, GUID FKs, plus `Seq bigint identity` and its index | 201 | 314 | 515 (1.81) | about 650 MB | about 1.94 GB | yes |
| **GUID primary key as a non-clustered unique index, clustered on `Seq`, FKs to int alternate keys** | 129 | 214 | 343 (1.21) | about 430 MB | about 1.29 GB | yes |
| the same with GUID FKs | 201 | 274 | 475 (1.67) | about 600 MB | about 1.79 GB | yes |

Page reads. Single-row seeks are 3 reads in every variant (B-tree depth stays 3 at 1M and 3M; C measured `ledger_by_id` 3). Reads that scan an index scale with its width: the hot-product history (`ledger_sum_by_product`, 14,156 cold logical reads in C) and the movements counts (7,626) should grow by 1.4 to 1.6 times for GUID FKs and 1.6 to 1.9 times for a GUID clustered key, and by three times from 1M to 3M rows in every variant (the DBA's existing finding that the count is the O(n) part). Working set: C cached 381 MB after the DBA workload; expect 540 MB (GUID FKs) to 690 MB (literal GUID key).

Insert cost: not measured yet. Bytes written per ledger row (row plus five indexes, which is also the log volume) rise 1.46 times for GUID FKs and 1.81 times for the literal GUID key. The earlier DBA measurement of one extra index (0.6 to 0.8 ms per posting transaction, inside the noise) suggests the penalty is single digit to low double digit percent in throughput. Prediction until A1 is measured: option 2 costs 10 to 20 % of posting throughput (C measured 252 to 398 transactions/s with 16 writers), option 3 20 to 30 %. A random generator would be far worse.

**If the owner insists on a GUID primary key on the ledger**, the least harmful form is the last-but-one row: the GUID is a non-clustered unique key, the table stays clustered on a `Seq bigint identity`, and its foreign keys reference the parents' integer alternate keys. It costs +21 % (about +75 MB per million rows), keeps strict append order, `Id DESC` seeks and the DBA index set, and still leaves a user-visible GUID. It is the hybrid with the roles swapped on this one table. The ledger row's own id is not addressed by anyone, so the GUID buys nothing there.

### Paging design for GUID-keyed lists

GUIDs have no useful order, so `Id DESC` and `Id < afterId` stop meaning "newest".

- **Ledger and audit (strict append order required: BR-01, moving-average replay):** a `Seq bigint identity` column is mandatory; cursor `Seq < @after`; indexes end in `Seq DESC` exactly as today. With an `OccurredAt` keyset instead, a late-committed row can appear in the middle of an already-read page and the replay order is lost (the DBA script `50-cursor-walk.sql` measures the `(OccurredAt, Id)` walk against the `Id` walk, including late rows, and an extra index of roughly 40 to 60 MB; results pending).
- **Documents (GUID key):** either (a) keyset `(CreatedAt DESC, Id DESC)`: predicate `CreatedAt < @c OR (CreatedAt = @c AND Id < @i)`, index `(Status, Type, CreatedAt DESC, Id DESC) INCLUDE (WarehouseId)` replaces `IX_StockDocument_Status_Type_Id`, cursor = base64 of both values; the tie-break must be evaluated by SQL Server (its GUID order differs from .NET `Guid.CompareTo`), so it must be verified that EF translates the comparison and never re-sorts in memory; or (b) a `Seq bigint identity` column on documents with unchanged indexes and `Seq < @after`. I prefer (b): it keeps every measured plan, adds 8 bytes per document, and (a) needs a new index design.
- **Movements report:** `PostedAt DESC, DocumentId DESC, line Id` still gives a total order with a GUID document id, but the tie order of documents posted at the same instant is no longer "newest document first" (a documented contract note); a GUID line key makes the last term arbitrary.
- **Master lists** (products, partners, users by name or username) already end in a tie-break on `Id`; a GUID works there unchanged.
- **Cursor on the wire:** opaque string in all cases (section 2.4).

### Three costed options for the owner

| | **Option 1: hybrid (recommended)** | **Option 2: GUID PK on all tables except ledger and audit** | **Option 3: GUID PK on every table** |
|---|---|---|---|
| What it is | int/bigint clustered keys stay; `PublicId` GUID (random or v7, unique, unfiltered) on 9 addressed tables; typed code beside it (one new column: `Partner.Code`); opaque, encrypted cursors; no row id on ledger and audit responses | 2a: lines, balances and all tables except ledger and audit get GUID clustered keys; ledger FKs reference GUID parents. 2b: GUID clustered PK only on classes M and T; each of those parents also has an `int Seq` unique alternate key and ledger, lines and balances keep int keys and reference the alternate key | GUID clustered key everywhere, ledger and audit included, `Seq` kept on ledger, audit and documents for order; all FKs 16 bytes |
| Database size, 1M / 3M | +8 MB (+0.8 %) / +25 MB | 2a +480 MB (+45 %) / +1.4 GB. 2b +42 MB (+4 %) / +100 MB | +640 MB (+60 %) / +1.9 GB |
| Ledger at 1M / 3M | 357 MB / 1.07 GB (unchanged) | 2a 520 MB / 1.57 GB. 2b unchanged | about 650 MB / 1.94 GB |
| Insert (posting) | unchanged within noise (one more index row per document) | 2a predicted -10 to -20 %. 2b about unchanged | predicted -20 to -30 % |
| Single-row read by id | public id: one more seek, 5 to 6 reads instead of 3 | 3 reads (clustered GUID seek) | 3 reads |
| Scans, counts, hot-product history | unchanged | 2a x1.4 to x1.9. 2b unchanged | x1.6 to x1.9 |
| Paging | unchanged (`Id`), cursor opaque | documents keyset or `Seq`; ledger/audit unchanged | `Seq` on three tables plus new indexes |
| Does the id leak volume? (D2) | **No**: the public id is random or v7; ledger and audit rows carry no id; cursors are encrypted | 2a: **yes**, if the GUIDs are sequential (counter), or fragmentation if random. 2b: same for the 12 parent tables. Ledger and audit ids and cursors still need hiding | same as 2a for every table |
| Index-friendly and leak-free at once | yes | no | no |
| Agent-hours | about 85 (idempotent create is optional, -4; D2 hardening +4) | 2a about 105; 2b about 95 | about 125 |
| PRs | 12 | 13 to 14 (the DBA re-derives every index of README sections 4 to 7) | 14 to 16 |
| Migration | additive, migrate in place | 2b additive per table but needs the alternate keys first; 2a re-keys 17 tables and changes 26 FK columns: baseline reset | baseline reset, re-seed, re-run the 1M tier |
| Main risks | two ids per entity (mitigated by types: `int` vs `Guid` do not mix, architecture test on the OpenAPI document) | 2a: wide FKs on the posting path; unconventional foreign keys to an alternate key (2b: EF `HasPrincipalKey`, ER diagrams, DBA surprise) | all of 2a, plus loss of ordering semantics on the two tables where they matter most, plus a baseline reset |
| UI refresh slices | wire change only: ids become strings (PR-8); R11, R14, R22 type-only; R17 to R20 wait for PR-8; R01 to R10, R12, R13, R15, R16, R21, R23, R24 unaffected | identical to option 1 | identical, and `LedgerRow.id` and `AuditLogDto.id` also become GUID strings |

All three options make the same breaking change for the web client (section 3.2) and place identically against the refresh waves (section 6.3). The options differ only in the database and in D2.

### Recommendation

**Option 1, with the D2 hardening included** (drop `id` from `LedgerRow` and `AuditLogDto`, encrypt cursors with Data Protection, +4 hours), and the typed code as listed (only `Partner.Code` is new). It gives the owner what the GUID preference is for: every addressed row has an immutable GUID id in the API and URLs, nothing sequential or countable is visible to a user, and a typed code sits beside it. It is the only option that is index-friendly and leak-free together, keeps the DBA's measured index set valid, adds under 1 % to the database and costs about 20 hours less than option 2a, 10 less than 2b and 40 less than option 3.

If the owner needs the GUID to be the literal SQL primary key of the master and document tables, option 2b is the way (about 10 more hours, +4 % size), never 2a or 3. Do not put a GUID key on the ledger or audit log in any form.

**The one question left for the owner:** Is the purpose of the GUID that clients and users see non-guessable, non-countable ids (then option 1), or must the SQL primary key column itself be a `uniqueidentifier` on the master and document tables for a standard or tooling reason (then option 2b, for about 10 more hours)?

(Still true, from the earlier analysis: if D2 were "no" and the owner did not care for GUIDs, the cheapest choice would be to keep integers, fix SEC-11 and add an `Idempotency-Key`, about 12 hours. The owner's preference for GUIDs rules that out.)

## 1. Context: what the code does today

Facts I checked in the code (not in the owner's note):

- **The keys are mostly `int`, not `bigint`.** 14 tables have an `int` identity key (`Entity.Id`, `src/Ims.Domain/Common/Entity.cs:7`). Only `StockLedgerEntry` (`StockLedgerEntry.cs:8`) and `AuditLog` (`AuditLog.cs:8`) are `bigint`. `StockBalance` (ProductId, WarehouseId), `DocumentSequence` (Prefix, YearMonth, WarehouseId) and `SystemSetting` (Key) have composite or natural keys. 19 tables, 26 foreign keys, and 2 audit user columns (`REC_CREATOR`, `REC_REVISOR`) on every table. Replacing `int` by a 16-byte GUID is a 4x growth per key, not 2x.
- **Order is meaning.** "Newest first" is `ORDER BY Id DESC` on documents (`DocumentsPageSpec.cs:11`), the ledger (`LedgerRowsSpec.cs:11`) and the audit log (`AuditLogsPageSpec.cs:12`), and the seek cursor is `Id < afterId` (`Paging.NextCursor`, `PagedResult.NextCursor: long?`). The hot-path indexes of the DBA work end in `Id DESC` (`IX_StockDocument_Status_Type_Id`, `IX_AuditLogs_Actor_Id`, `IX_AuditLogs_EntityId_Id`, `IX_AuditLogs_Action_Id`) and `data-and-performance-rules.md` section 9 states the rule "a sort an index serves ends in unique keys (`Id`)". The movements report orders by `PostedAt DESC, DocumentId DESC, line Id` (`MovementOrder.cs:18-25`). The moving-average rebuild must replay the ledger **in `Id` order** (`docs/dba/README.md` section 8). Under a GUID none of this order exists.
- **Lock order uses ids.** Posting takes application locks per (warehouse, product) in ascending product id (`SqlTransactionLocker.LockStockAsync`, `PostingEngine` orders by `ProductId`). Any total order works as long as it is one order; .NET `Guid.CompareTo` and SQL Server's `uniqueidentifier` order differ, so the order must be fixed in one place and tested.
- **Audit entity ids are strings already** (`AuditLog.EntityId`, max 100, `AppDbContext.KeyOf`); documents are audited by document number. Files on disk are already named by a random GUID (`LocalFileStorage`), independent of the row id. Excel imports and exports use SKU, warehouse code and barcode, never row ids. Document numbers (BR-11) are a separate counter (`DocumentSequence`) and stay.
- **The web never uses cursors** (no `afterId` or `nextCursor` in `web/libs`, outside the generated client). It treats ids as numbers in about 49 declarations and parses them with `Number(...)` at 4 places (`product-edit`, `document-editor` x2, `product-stock`). The OpenAPI document has 60 operations, 42 id parameters (path and query) and 49 id-like properties in 30 of 62 schemas.
- **There is no production data.** `Ims_Dev` has 106 ledger rows, `Ims_QA` 1,124 (9 migrations applied), and CI creates a fresh database per run (`TestDatabase`). The PRD fixes one deployment per customer (single tenant), offline buffering of counts in Phase 2 (CNT-03), and "evaluate multi-tenancy at scale [to be confirmed]".
- **Existing security finding that depends on this:** SEC-11 (`docs/security/security-review-initial.md:88`): an out-of-scope id gives 403 and a missing id 404, an existence oracle on sequential ids. The fix (404 for both) is independent of the key type and should be done anyway.
- **A real leak that sequential global ids cause:** `DocumentDto.id`, `AttachmentDto.id` and `LedgerRow.id` are counters shared by all warehouses. A warehouse-scoped user who sees ids 31,204 and 31,209 knows another warehouse created four documents in between. (Document **numbers** are per warehouse and month, so they do not leak that; `total` in page results is scoped by the caller's warehouses.) This is the only information leak I can show that a public GUID closes.

## 2. Options

- **A. GUID clustered primary keys everywhere.** All 19 tables, all foreign keys 16 bytes.
- **B. Hybrid.** Keep the int/bigint clustered key internally. Add `PublicId` (GUID, unique, non-clustered) to the tables the API exposes. Foreign keys stay int. The API speaks only GUIDs.
- **C. Keep integer keys.** Fix SEC-11, add an `Idempotency-Key` header for creates when needed.

### 2.1 SQL Server GUID ordering and the generator (precise)

SQL Server compares a `uniqueidentifier` by byte groups, not left to right. Writing the text form as `AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE`, the **most significant group is `EEEEEEEEEEEE`** (bytes 10 to 15, left to right), then `DDDD` (bytes 8 and 9), `CCCC`, `BBBB`, and **`AAAAAAAA` last**. Verified on this machine (SQL Server 2025 RTM-CU3, `ORDER BY` on five values with one group set to `FFFF`): they sorted ascending as A, B, C, D, E.

What that means for each generator, as stored in `uniqueidentifier`:

| Generator | Where the time part sits | Order in SQL Server | Index-friendly? |
|---|---|---|---|
| `Guid.NewGuid()` (UUIDv4), browser `crypto.randomUUID()` | nowhere | random | No: inserts land anywhere, page splits, about 70 % page fullness in steady state, higher log volume |
| `Guid.CreateVersion7()` (UUIDv7, RFC 9562) and a JS UUIDv7 | 48-bit millisecond stamp in group A and the first half of B, i.e. the **two least significant groups**; group E is random | **random** (I sorted six v7-shaped values created in time order and got 2, 4, 1, 6, 3, 5) | **No.** The usual advice "use UUIDv7 for time-ordered keys" is true for PostgreSQL and MySQL, not for a SQL Server `uniqueidentifier` |
| EF Core `SequentialGuidValueGenerator` (the SQL Server default for a Guid key, client side) | an increasing counter (starts at `DateTime.UtcNow.Ticks`, +1 per id) written into bytes 8 to 15, high bytes in 10 to 15 | ascending within one process | **Yes** (one hot insert page per API process; after a restart the counter starts at the current time, so it does not fall behind) |
| `NEWSEQUENTIALID()` (column default) | server side | ascending since the last Windows restart; can restart in a lower range | Yes, but it cannot be generated by a client or an API, so no client-minted ids |
| UUIDv7 stored as `binary(16)` big-endian (EF `ValueConverter`), or v7 bytes re-ordered into SQL order | time stamp in the most significant bytes | ascending | Yes, but the type is no longer `uniqueidentifier`: raw SQL, SSMS and `CONVERT` get awkward, and a re-ordered value differs from the one on the wire (debugging trap). I would not do this |

So "which generator keeps inserts index-friendly" = **the EF sequential generator (or `NEWSEQUENTIALID()` for server-only inserts)**, never UUIDv7 or v4 in a SQL Server `uniqueidentifier`. A client-minted id (v4 from the browser) is always random; that is why it must never be a clustered key of a big table, which decides A against B for documents and attachments.

### 2.2 Comparison by criterion

| Criterion | A: GUID clustered everywhere | B: internal int + `PublicId` | C: integer keys |
|---|---|---|---|
| Id enumeration, IDOR | Blind enumeration impractical. IDOR is an authorization question (RBAC and scope are tested); the key type is defence in depth | Same as A on the wire (the internal key is never exposed) | Enumerable; with SEC-11 fixed (404) it gives no signal, but ids stay guessable |
| Volume leak to scoped users | Closed | Closed for the 9 tables. Ledger and audit `id` remain unless D2 (section 8) says to drop them | Open (global counters) |
| Client-minted ids, offline queue, idempotent create | Yes, but a random v4 becomes the clustered key of documents and attachments (random inserts into wide rows) | Yes, and the random value lands in a 29-byte-per-row index, not in the wide table | No; needs `Idempotency-Key` plus a table, and temp-id mapping for dependent queued requests |
| Merge across databases, future shard or tenant | Yes | Yes (`PublicId` is global; internal ints are per database and re-mapped on import) | No (SKU and code natural keys exist for master data only) |
| Ordering, "newest first", cursors | Broken: needs a sequence column on ledger, audit, documents, plus new composite cursors and a redesign of every index that ends in `Id` | Unchanged | Unchanged |
| Strict append order of ledger and audit | Needs an extra `Seq bigint identity`, and then the GUID is a second key anyway | Unchanged | Unchanged |
| Row and index size at 1M ledger rows | Ledger +72 to +80 bytes per row (6 id columns): 357 MB measured becomes about 520 to 650 MB | Ledger unchanged. +1 % overall (nine small unique indexes) | Baseline (357 MB measured) |
| Join and FK cost | 16-byte keys in every FK, every non-clustered index and every join | Unchanged. Reads that return a related id join a table they already join (name, code, username) | Baseline |
| Fragmentation, page splits | None with sequential GUIDs server-made; severe for client-minted ids | None on the clustered keys; the unique index on `PublicId` splits, on small narrow tables | None |
| Memory, backup, restore | About +45 to +60 % of the 1,061 MB allocated at the 1M tier (owner-preference section) | About +1 % | Baseline |
| Debugging, support | Humans quote document numbers either way. SQL by id is copy-paste of 36 characters | DB staff keep short int keys; URLs and logs carry `PublicId`; two ids per row is a new thing to explain | Easiest |
| EF Core | Fine (`Guid` keys, `SequentialGuidValueGenerator`) | Fine (a property plus a unique index; an EF alternate key is not needed) | Current |
| Tests and fixtures | Every builder, client, k6 and seed script changes | Same changes at the boundary; seed SQL scripts keep working (the column has a default) | None |
| Wire contract change | Breaking (ids are strings) | Breaking (the same) | None |
| Effort | about 115 agent-hours | about 85 | 0, or about 12 with `Idempotency-Key` |
| Reversibility | Only by restoring a backup | `Down` drops the column and the indexes | n/a |

The wire change is identical in A and B. **What differs is the database side, and there B is smaller, keeps the ordering, keeps every DBA index and measurement valid, and is additive.**

### 2.3 Idempotent create (NV-02): exactly how (optional since D1 = no, PR-7b)

QA finding NV-02 (a second tap on "Submit & post" after Back created a second document) was fixed in the UI by making the editor follow the saved document (`docs/architecture/frontend.md` rule 9). That fix is still needed. A GUID helps in the cases the UI cannot control (a lost response and a retry by the service worker or the user, two tabs, a queued offline request). The mechanism has five parts; remove any one and it breaks:

1. **The client mints the id once per form instance.** When "New receipt" opens, it creates `id = crypto.randomUUID()` and keeps it with the draft (memory now, IndexedDB when offline arrives). Every retry of the same create sends the same id.
2. **The create endpoint accepts it.** `POST /api/documents/receipts` and `/issues` take an optional body field `id` (and `POST /api/attachments` an `id` form field). No id means the server mints one (compatible with scripts).
3. **Check before allocating the number.** The handler looks the id up first, inside `IUnitOfWork.ExecuteAsync`, before `IDocumentNumbering.NextAsync`. Found and created by the **same user, same type**: return the current document (200, with its ETag) and apply nothing from the payload. Found with another creator, another type, or soft-deleted: `409 id_in_use`, same body in every case, no data from the other row. Not found: create as today. A replay therefore never burns a document number.
4. **A unique index is the backstop.** `UX_StockDocument_PublicId` is unique and **not filtered by `REC_DELETED`** (an id must never be reused after a soft delete). Two parallel creates with the same id: one insert wins; the other raises 2601/2627, which `UnitOfWork.IsRetriable` already treats as a key race and re-runs the whole body after back-off. On the second attempt step 3 finds the row and returns it. No new retry code.
5. **State changes are already safe to retry.** Submit, approve, post and reverse carry `If-Match` and the state machine refuses a second post, so a lost response to `post` yields 409 and a GET shows `Posted`; stock is never posted twice. A client that wants a silent success on replay can compare the fetched status.

Offline fit: a queued sequence "create receipt, upload photo, submit" is possible because every later request names the document by an id the client already holds. With server ints the client needs a temp-id table and request rewriting. The server must treat the client id as untrusted: non-empty (`Guid.Empty` is a 400), no meaning in version or embedded time, and `createdAt` stays the server's time (a Phase 2 `clientCreatedAt` is a separate decision).

### 2.4 Ordering and paging

- **B and C:** unchanged. `Id DESC` on the internal key stays the order of documents, ledger and audit; all existing indexes and plans stay valid. Only the cursor's *type* changes (below).
- **Cursor in B:** `afterId` (number) becomes `cursor` (string) and `nextCursor` becomes `string?`. The value is base64url of the 8-byte internal position. It is opaque by contract (clients must not parse it) and trivially constructible in tests. It is not signed: a forged cursor only moves the start of a page inside the filter and scope the server still applies, so it grants nothing. If D2 is "yes" it is encrypted with ASP.NET Data Protection instead (then k6 collects cursors by walking pages in `setup()`).
- **What would replace `afterId` under A:** a keyset on a monotonic column, because GUIDs have no usable order. Documents: `(CreatedAt DESC, Id DESC)`, cursor = both values, predicate `CreatedAt < @c OR (CreatedAt = @c AND Id < @i)` (EF has no row-value comparison; SQL Server seeks on `CreatedAt <= @c` and filters the tie, which is cheap), and `IX_StockDocument_Status_Type_Id` becomes `(Status, Type, CreatedAt DESC, Id DESC)`. Ledger and audit: **a `Seq bigint identity` column** (clustered or at least the sort key), because `OccurredAt` and `At` are not unique and not strictly commit-ordered, and the moving-average replay and `Id DESC` seek require strict append order. That column is the B design for those tables, so A contains B's exception on the largest tables and adds GUIDs on top of it.
- Identity values are allocated at insert, not at commit, so a consumer that tails the ledger by "last seen Id" can miss a row committed late. Posting serialises per (warehouse, product) with application locks, so the per-product order the replay needs is exact. GUIDs do not change any of this; a future outbox or integration feed should use its own watermark.

### 2.5 Size arithmetic at the 1M tier (first estimate; superseded by the measured baseline in the owner-preference section)

This is my first, schema-only estimate. The measured C baseline is larger (ledger 357 MB, not 220 MB, because the load left the indexes 56 to 98 % full) and the owner-preference section scales the measured numbers; the conclusions are the same. Row sizes from the column lists in `AppDbContextModelSnapshot.cs`, 11 bytes of row overhead, 8,096 usable bytes per page, DBA tier counts (`docs/dba/README.md` section 2).

| Table (rows) | Id columns per row now | Row bytes now | Row bytes under A | Heap of the clustered table, now | A, sequential | A, random keys (about 70 % full) |
|---|---|---|---|---|---|---|
| StockLedgerEntry (1.02 M) | Id 8, 4 FKs 16, 2 audit users 8 | about 98 | about 178 | 99 MB | 181 MB | 258 MB |
| StockDocumentLine (1.07 M) | Id 4, 3 FKs 12, 2 audit users 8 | about 87 | about 159 | 92 MB | 171 MB | 245 MB |
| StockDocument (167 k) | Id 4, 7 FKs 28, ReversesDocumentId 4, audit users 8 | about 280 | about 412 | 47 MB | 70 MB | 100 MB |

Ledger with its five non-clustered indexes (`ProductId, WarehouseId, OccurredAt`; `SourceDocId`; `OccurredAt`; FK indexes on `WarehouseId` and `CreatedBy`), each of which carries the clustered key: about **220 MB now, 400 MB under A with sequential GUIDs, 480 MB with random keys**. Lines (with their four indexes) and documents (eight indexes) add about 180 MB more, so the database grows by about 350 MB. Over the 1,061 MB allocated at the 1M tier that is about +30 % by this estimate (+45 % with the measured, more fragmented indexes). This instance reports a memory target of about 40 GB, so there is no buffer-pool cap here; the cost is working-set growth and cold reads. Under B only nine unique indexes of about 29 bytes per row are added (documents 167 k rows, about 5 MB), about +1 %.

CPU and write cost: B adds one 16-byte index row per insert on nine tables, none on the posting path except the document header (once per document). A makes the ledger, line, balance and index rows 1.8x wider on the posting path itself.

### 2.6 Table by table: where the owner's suggestion is a bad idea

| Table | Rows at 1M tier | Addressed by a client? | Verdict |
|---|---|---|---|
| StockLedgerEntry | 1.02 M | No (never fetched by id; only listed and exported) | **Keep bigint identity.** Append order is the data model (BR-01, moving-average replay, `Id DESC` seek); six id columns per row; the widest and hottest table. No GUID, no public id |
| AuditLog | 332 k | No | **Keep bigint.** Append order; ids are never referenced; `EntityId` is already a string |
| StockDocumentLine | 1.07 M | No (replaced with its document; the line `id` in `DocumentLineDto` is only a row key) | **Keep int.** Drop `id` from `DocumentLineDto` (also removes a global-counter leak; the one use is `track line.id` in `document-editor.component.html:221`, which becomes `track $index`) |
| StockBalance | 175 k | No | **Keep the composite key.** Updated on every posting; a 32-byte clustered key repeated in every index |
| StockDocument | 167 k | Yes (URLs, links) | Internal int stays clustered; `PublicId` added; client-minted ids supported |
| Attachment | small | Yes (download, delete) | Internal int; `PublicId`; client-minted ids supported (offline photo). The disk name is already a separate GUID |
| User, Warehouse, Partner, Category, Uom, Product, ApprovalRule | small | Yes | Internal int; `PublicId` |
| ProductBarcode, UomConversion, UserWarehouse | small | No (children of Product and User; addressed by barcode text, unit pair, warehouse) | Keep int, no public id |
| RefreshToken | small, high churn | No (the token hash is the handle) | Keep int |
| DocumentSequence, SystemSetting | tiny | No | Keep natural keys |

For the small master tables A would be harmless; the objection is that the model then has two patterns (GUID keys on some tables, a sequence column on the big ones). B has one pattern with a short list of exceptions.

### 2.7 Considered and rejected

- **Translate ids on the wire in a filter** (DTOs keep `int`, a JSON converter swaps them for GUIDs): little code change, but DTO types lie about the contract, the API tests could not use the Application types, and it needs async lookups inside serialization. Rejected for honesty and debuggability.
- **Expand/contract of the contract** (DTOs carry both `id` and `publicId` for a while): avoids a merge freeze on the web, costs about 12 more hours and doubles the tests for a transition with one client we own and no production. Not chosen; the integration-branch plan in section 6 gives the same safety.
- **UUIDv7 as `binary(16)` clustered keys everywhere:** index-friendly, but loses the `uniqueidentifier` type and is the same size as A.

## 3. Inventory of tables, keys and foreign keys

Foreign keys (26). "Internal" means the column stays int under B; under A each becomes `uniqueidentifier`.

| Table | Key now | Public id (B) | Foreign keys (column -> table) |
|---|---|---|---|
| User | int | yes | none |
| UserWarehouse | int | no | UserId -> User (cascade); WarehouseId -> Warehouse |
| RefreshToken | int | no | UserId -> User (cascade) |
| Warehouse | int | yes | none |
| Partner | int | yes | none |
| Category | int | yes | none |
| Uom | int | yes | none |
| Product | int | yes | BaseUomId -> Uom; CategoryId -> Category |
| ProductBarcode | int | no | ProductId -> Product (cascade) |
| UomConversion | int | no | ProductId -> Product (cascade); FromUomId, ToUomId -> Uom |
| ApprovalRule | int | yes | none |
| StockDocument | int | yes | WarehouseId -> Warehouse; PartnerId -> Partner; CreatedBy, SubmittedBy, ApprovedBy, PostedBy, RejectedBy -> User. Not an FK: `ReversesDocumentId` |
| StockDocumentLine | int | no | DocumentId -> StockDocument (cascade); ProductId -> Product; UomId -> Uom |
| StockLedgerEntry | bigint | no | ProductId -> Product; WarehouseId -> Warehouse; SourceDocId -> StockDocument; CreatedBy -> User |
| StockBalance | (ProductId, WarehouseId) | no | ProductId -> Product; WarehouseId -> Warehouse |
| Attachment | int | yes | UploadedBy -> User. Not an FK: `OwnerType` + `OwnerId` (polymorphic: document or product) |
| AuditLog | bigint | no | none (`EntityId` is a string) |
| DocumentSequence | (Prefix, YearMonth, WarehouseId) | no | none declared |
| SystemSetting | Key (string) | no | none |

Columns that hold a user id without an FK: `REC_CREATOR` and `REC_REVISOR` on all 19 tables. Ids in other places: JWT claims (`NameIdentifier`, `warehouse`; `CurrentUser.cs`), `ICurrentUser.WarehouseIds`, `IWarehouseScope`, the lock resource names (`stock:{warehouse}:{product}`, `docseq:...`), the `productId` extension of the 409 problem details and `ReversalShortage.ProductId`.

### 3.1 Code areas that change, with size and hours

Hand-written lines changed, tests included, excluding generated code (migration designers, snapshot, `api-client.ts`). Hours are agent-hours including building, running the suites and fixing. Ranges are about plus or minus 25 %.

| Area (today) | Option B (recommended): what changes | Lines | Hours | Option A: what changes | Lines | Hours |
|---|---|---|---|---|---|---|
| Domain (74 files, 2,258 lines; 22 files carry ids) | `PublicEntity : Entity` (one type per file), `IHasPublicId`, 9 entities inherit, factories of `StockDocument` and `Attachment` take an optional id, `Guid.CreateVersion7()` for server-made ones | 150 | 2 | Every `int` id and FK in 22 files, `Attachment` `ownerId <= 0` guards, lock-order comparer, `PostingEngine` grouping, exceptions | 350 | 5 |
| Infrastructure mapping and migration (`AppDbContext` 374 lines, 12 migrations) | `PublicId` property, unique **unfiltered** index and default `NEWSEQUENTIALID()` in one convention (with an exemption from the "unique indexes are filtered on `REC_DELETED`" loop at `AppDbContext.cs:225`), audit `KeyOf` uses `PublicId`, `AddPublicIds` migration (add, backfill, index; `Down` drops), `SqlExceptionMapper` for the unique index, Seeder | 250 (+700 generated) | 6 | All 19 key and 26 FK mappings, generators, `Seq` columns (ledger, audit, documents), re-derive about 15 indexes, baseline migration | 600 (+3,000 generated) | 14 |
| Application (5,142 lines, 419 files; 118 files carry ids) | `IdMap` (scoped, batched), `ByPublicIdSpec`, `PublicIdsSpec`, `CursorToken`; DTOs, commands, filters, requests, validators, handlers, specs of all features; `ShortageNamingBehavior` adds the product's public id; idempotent create | 1,900 | 22 | Type swap in 118 files (about 1,000), new keyset paging and ordering for documents, ledger, audit, movements (about 500), cursors (about 250) | 1,750 | 24 |
| Api (1,885 lines) | route `{id:int}` becomes `{id:guid}` (`ApiRoutes.Id`, 12 uses), 18 action parameters, `cursor`, 409 problem extension, OpenAPI `format: uuid` | 190 | 2 | Same | 190 | 2 |
| Backend tests (`Ims.Api.Tests` 10,786 lines, `Ims.Domain.Tests` 4,830, `Ims.Architecture.Tests` 1,054) | builders, `ReferenceData`, 16 clients, `EndpointCatalog`, 69 id lines, hard-coded 404 ids become random GUIDs; about 25 new tests (section 6.2) | 1,250 | 15 | Same plus ordering and cursor tests for three lists | 1,300 | 15 |
| Safety-net tests (PR-1) and decision docs (PR-0) | Section 6.1; `backend.md`, `data-and-performance-rules.md` | 800 | 7 | Same | 800 | 7 |
| OpenAPI and web (22.8 k hand-written TS, 4.7 k generated) | regenerate the client; ids `number` to `string` in about 100 files (stores x7, editor, product pages, users, partners, warehouses, settings), 4 `Number(...)` removed, 33 spec files, `ngsw-config` data-cache version bump | 900 | 9 | Same | 900 | 9 |
| e2e (1,863 lines), smoke (298), k6 (689), perf seed (228) | `api.ts` and `documents.ts` (`savedId`) ids opaque; smoke passes ids through; **k6 scenarios use id arithmetic** (`1 + rnd(maxDocId)`, `offset + zipf(...)`, `afterId=${1 + rnd(total)}`) and must sample real ids in `setup()`; the perf seed keeps working through the column default | 650 | 8 | As B, and the perf seed must generate GUID keys and FKs for 19 tables (set-based) | 850 | 10 |
| DBA: re-measure and docs (`docs/dba`, 10 sections) | Re-run the 1M tier once, update `README.md` | 150 | 6 | Re-derive every index of README sections 4 to 7 and every measurement | 600 | 22 |
| Security and QA retest, architect review of 12 PRs | IDOR matrix, client-id abuse cases, SEC-11, QA smoke | 0 | 8 | Same | 0 | 8 |
| **Total** | | **about 6,250** | **about 85** | | **about 7,350** | **about 115** |

(C: 0 hours; with an `Idempotency-Key` header and table for creates and the SEC-11 fix, about 12 hours.)

### 3.2 Breaking contract changes for the web client and other API consumers

1. **Every id is a string (`format: uuid`)**: 42 id parameters and 49 id properties in 30 schemas; the generated client changes type everywhere (`number` to `string`). Affected payloads include `DocumentSaveRequest` (warehouse, partner, `lines[].productId`, `lines[].uomId`), `ProductSaveRequest` (category, base unit, `conversions[].fromUomId/toUomId`), `UserSaveRequest` (warehouse list), filters (`warehouseId`, `productId`, `categoryId`, `partnerId`), `UserInfo` (login and `me`: `id`, `warehouseIds`) and attachments (`ownerId`).
2. **Routes:** `/receipts/:id`, `/issues/:id`, `/stock/:id`, `/catalog/:id` carry a GUID. The 4 `Number(id)` conversions go; `NEW_DOCUMENT_ID = 'new'` cannot collide with a GUID. An old bookmark `/receipts/31` becomes a 404 page.
3. **Cursor:** `afterId` (number) becomes `cursor` (string), `nextCursor` becomes `string | null`, on documents, ledger and audit. The web does not use it; scripts and tests do.
4. **`DocumentLineDto.id` is removed** (a line is identified by its position); `LedgerRow.id` and `AuditLogDto.id` stay numbers (row positions, not addressable) unless D2 says otherwise.
5. **Problem details:** the `productId` extension of the insufficient-stock 409 and `shortages[].productId` become GUID strings (`document-editor.component.ts:841` parses it with `Number`). New 409 code `id_in_use`.
6. **New optional field `id`** on document and attachment creation (client-minted).
7. **JWT claims stay internal ints** (nobody in the web decodes them); the server needs no database lookup per request for scope.
8. **Excel exports and import templates do not change** (no row ids in them).
9. **Browser state:** the service worker caches `GET /api/products|categories|units|warehouses|partners` for 12 hours (`ngsw-config.json`, group `master-data`); bump that group's `version` so old integer-shaped payloads are dropped. Device-local state that holds ids (R06 preferences keyed by user id, R18 recents of documents and products) must store them as opaque strings and validate shape on read.
10. **e2e, smoke, k6:** section 3.1 row 7.

## 4. Migration strategy (no production data)

**B (recommended): migrate, do not reset.**
1. Migration `AddPublicIds` (PR-3): add `PublicId uniqueidentifier NOT NULL` with default `NEWSEQUENTIALID()` to the 9 tables, backfill existing rows (`UPDATE ... SET PublicId = NEWID()` before the unique index if the default does not fill them row by row), create `UX_<Table>_PublicId` (unique, unfiltered, `FILLFACTOR` 90). `Down` drops indexes and columns. Test up, down, up as the DBA did for earlier migrations.
2. Dev, QA and CI databases are migrated in place (`Ims_Dev` and `Ims_QA` are tiny; CI builds a new database per run). Old audit rows keep their integer `EntityId`; nothing links to them.
3. Existing migrations stay. If the team wants a clean history, a squash to one `Baseline` is a **separate** hygiene PR that is allowed only while no environment holds data worth keeping. Checklist if it is done: tag `pre-baseline`; take the live DDL extras from a database, not from code (the two append-only triggers are named `TR_StockLedgerEntry_AppendOnly` and `TR_AuditLogs_AppendOnly`; `TriggerNames.LedgerAppendOnly` says `TR_StockLedgerEntries_AppendOnly` and is unused, so it is wrong); `READ_COMMITTED_SNAPSHOT` (suppress-transaction SQL); the filtered indexes; delete `Migrations/`; `dotnet ef migrations add Baseline`; paste the SQL blocks; compare `sys.columns`, `sys.indexes`, `sys.foreign_keys` of a database built from the old chain with one built from the baseline; rebuild `Ims_Dev`, `Ims_QA`, `Ims_QA2`, `Ims_Verify`, `Ims_Perf` and compose volumes. The SQL repair statements in old migrations (`UniquePartnerNamePerType`, `AddDocumentReversedAt` backfill) are not needed in a baseline.

**A: reset is forced.** There is no safe incremental path (re-keying 19 tables and 26 FKs), so: back up `Ims_QA` (5 seconds), tag, delete the 12 migrations, generate `Baseline` from the GUID model plus the SQL extras above, rebuild every database, re-seed demo data (`Seeder` is idempotent), re-run the 1M perf seed. This is possible **only before the first environment with real data**; afterwards it would be a multi-day re-keying with downtime. That is the "cheapest moment" argument, and it is true for A only. For B the cheap moment applies to the *wire contract* (nothing outside our own web client holds an id yet), which is the part that gets expensive later (saved URLs, printed labels, integrations, offline caches).

**Rollback.**
- Before merge to main: the integration branch (section 6) is simply not merged.
- After merge, B: revert the merge commit; `dotnet ef database update <previous migration>` runs `Down` (drops `PublicId`). The web and API are one release unit, so they are reverted together. Client-minted ids created in between would be lost, which is acceptable until offline clients exist.
- A: no `Down`; restore the pre-merge backup and rebuild.
- Gate: the PO confirms in writing that no environment outside developer, QA and CI machines holds data before PR-3 merges.

## 5. Recommendation and conditions

**Recommendation: option 1 (hybrid), with the D2 hardening, the typed codes and the opaque cursor.** Full reasoning, the three costed options and the single open question are in "Owner preference: GUID primary keys plus user-defined unique code".

**With D1 = no, what stays of the case for GUIDs:** the owner's preference, non-guessable ids, and D2 (ids that do not count activity). What falls away: cross-database identity and offline queues as requirements. They remain free side effects of option 1.

**I would change the recommendation to option 2b** (GUID clustered key on the master and document tables, integer alternate keys for the big tables' foreign keys) if the owner states that the SQL primary key column itself must be a `uniqueidentifier` there (a database standard, a tool or a reporting requirement). About 10 more hours, +4 % size.

**I would change it to option 2a or 3** only if rows, including ledger rows, must be created on several nodes without a central sequence (true multi-master or replication). Not planned (D1 = no). Ledger and audit would still need an order column.

**I would change it to the cheapest choice (keep integers, fix SEC-11, add an `Idempotency-Key`, about 12 hours)** only if the owner dropped the GUID preference and D2 were "no".

**I would stop and re-decide if** the DBA finds that the `PublicId` lookup (an extra non-clustered seek and key lookup, 5 to 6 logical reads instead of 3) moves any endpoint of the 1M tier over 1 s P95 (it should not: by-id reads are the cheapest endpoints, and writes replace an id lookup by a `PublicId` lookup rather than adding one).

## 6. Step plan

Rules: twelve PRs (PR-0 is documentation; PR-7b is optional and not counted in the 85 hours), each under about 1,500 hand-written changed lines (generated code excluded), each green on its own CI. PR-4 to PR-8 are stacked on an integration branch `feature/public-ids`, which PR-9 merges to main in one step once the web is ready; main is never in a state where API and web disagree. PR-1, PR-2, PR-2b and PR-3 go straight to main: they change nothing the web depends on (the cursor is not used by the web, PR-3 only adds a column).

| PR | Content | Owner | Lines | Hours | Depends on | Runs in parallel with |
|---|---|---|---|---|---|---|
| PR-0 | ADR accepted, PO answers D1 and D2 recorded, `backend.md` and `data-and-performance-rules.md` updated (the rule "a sort ends in `Id`" gains "internal `Id`") | be-architect | 150 | 2 | PO | none |
| PR-1 | Safety net (tests only): schema-shape pin (every table, key, FK, unique index), paging walks with equal timestamps for documents, ledger and audit, concurrent posting reconcile; SEC-11 fix (404 for out-of-scope) as PR-1b, about 80 lines | integration-test-engineer, unit-test-engineer, be-framework (1b) | 650 | 5 | PR-0 | PR-2, PR-2b |
| PR-2 | Opaque cursors (`cursor` / `nextCursor` strings, `CursorToken`), tests; smoke and k6 follow in PR-2b | be-framework | 400 | 3 | PR-0 | PR-1 |
| PR-2b | Tests treat ids as opaque and use the new cursor: e2e `api.ts`/`documents.ts`, smoke, **k6 samples ids in `setup()` instead of arithmetic** (works with ints today) | behavior-test-engineer, be-dba (k6) | 350 | 4 | PR-0, PR-2 | PR-1, PR-3 |
| PR-3 | Storage and plumbing: `PublicEntity`, 9 entities, `AddPublicIds` migration, unique unfiltered index and exemption in the soft-delete loop, audit `KeyOf`, `IdMap`, `ByPublicIdSpec`, `PublicIdsSpec`, `SqlExceptionMapper`; nothing public uses it yet | be-framework (code), be-dba (migration review, index, `FILLFACTOR`) | 750 | 8 | PR-1 | PR-2, PR-2b |
| PR-4 | Leaves flip: Uom, Category, Warehouse, Partner (their endpoints and every field that references them) | be-framework, integration-test-engineer | 700 | 9 | PR-3 | web waves 1 and 2 |
| PR-5 | User flip (`UserInfo`, created/approved-by fields, user warehouses; JWT stays int) | be-framework | 400 | 5 | PR-4 | web waves 1 and 2 |
| PR-6 | Product flip (products, barcode lookup, conversions, stock rows, low stock; imports keep resolving by SKU) | be-framework | 700 | 9 | PR-5 | web waves 1 and 2 |
| PR-7 | Document and Attachment flip: documents, approvals, ledger source, movements, attachments, `ShortageNamingBehavior`, drop `DocumentLineDto.id`; the optional client-minted id with idempotent create and `id_in_use` is PR-7b (4 hours, can be dropped since D1 = no) | be-framework, integration-test-engineer | 1,000 | 14 | PR-6 | web waves 1 and 2 |
| PR-8 | Web: regenerate the client, ids to string, routes, stores, specs, `ngsw` version bump, recents and preferences as opaque strings | fe-framework, fe-architect (review) | 900 | 9 | PR-7 | e2e prep |
| PR-9 | e2e fixes and new scenarios, smoke, perf seed check; merge the integration branch to main | behavior-test-engineer | 300 | 3 | PR-8 | DBA run |
| PR-10 | Verification: 1M-tier re-measure (reads of by-id endpoints, write-mix, reconcile drill, fragmentation of the `PublicId` indexes), IDOR and client-id abuse retest, QA smoke on Staff, Supervisor and Admin, architecture test "no integer id on the wire", docs | be-dba, security-engineer, qa-edge-accessibility, be-architect | 150 | 14 | PR-9 | none |

Elapsed time with three lanes (backend, test, web), the stacked PRs 4 to 7 done by one backend agent in sequence (they share the test builders, so they do not run in parallel): PR-1, 2, 2b, 3 in about 2 working days, PR-4 to PR-7 in about 5, PR-8 and PR-9 in about 2, PR-10 in 1: about 10 working days and about 85 agent-hours including reviews.

### 6.1 Tests that must exist before the first flip (PR-1, PR-2b)

- Schema pin: tables, PK types, FK list (26), unique and filtered indexes, the two triggers, `READ_COMMITTED_SNAPSHOT`.
- Page-walk tests (no skip, no repeat) for documents, ledger and audit with equal `CreatedAt`/`OccurredAt`/`At` values and while rows are inserted between pages.
- Balance equals the ledger after a parallel posting run (`08-reconcile-drill` logic as an API test), and `ParallelPostingTests` green.
- Out-of-scope by-id reads answer 404 and are indistinguishable from missing ones (SEC-11).
- All 13 e2e scenarios, smoke and the k6 scenarios pass with ids treated as opaque.

### 6.2 Tests that must exist after

- `AddPublicIds` up/down/up; `PublicId` unique, never null, never updated (domain test: no setter path), unique index **not** filtered by `REC_DELETED` (a soft-deleted id cannot be re-created).
- Architecture test over the OpenAPI document: every property or parameter named `id` or ending in `Id` has `format: uuid`, except an allow-list of position tokens (`LedgerRow.id`, `AuditLogDto.id`) and quantities; no Application response type exposes an `int`/`long` entity id outside the allow-list. Another over `Entity` types: a table whose entity implements `IHasPublicId` appears in the PublicId index list.
- Idempotent create: same id twice gives one document and one consumed number and the second call returns 200 with the same body; same id from another user, another type, or a soft-deleted row gives 409 `id_in_use` with identical bodies; two parallel creates with one id give one row; `Guid.Empty` gives 400; a replay still enforces permission and warehouse scope.
- IDOR matrix on every by-id route: unknown GUID, other warehouse, other role, for documents and attachments (404, 404, 403 only for permission).
- Cursor: round trip, tampered value, a cursor from another list, a cursor with a filter change; opaque (no test parses it).
- Reports and exports unchanged byte for byte for the same data (movements page walk and export agree, `SignedStockEffectTests` green).
- Lock order: posting with products in both orders, 50 parallel, no deadlock (existing `ParallelPostingTests`) plus a unit test that the lock order helper is the only place that sorts ids.
- Web: Jest for stores and routes with GUID strings; `Number(id)` banned by a lint rule; e2e 01 to 13 green.
- DBA gate: the 1M tier by-id and list endpoints stay under 1 s P95; write-mix flows within noise of today; reconcile drill 0 differences.

### 6.3 Interaction with the 2026 UI refresh (`docs/design/2026-refresh-plan.md`)

The refresh needs no backend change and no id change, so its slices only care about the day the web client flips (PR-8).

- **Unaffected (build in parallel with PR-1 to PR-7):** R01 palette and contrast, R02 required marker and login, R03 reports fixes (cell templates; its store touches `reports.store.ts`, a type-only conflict), R04 money rules, R05 tokens and icons, R06 preferences (keep keys strings: `ims.pref.<userId>.*`, where `userId` becomes a GUID string), R07 motion, R08 controls, R09 tables, R10 shell, R12 density, R13 overlays, R15 skeletons, R16 palette component, R21 paging (offset based, no cursor), R23 dates, R24 clean-up.
- **Touch ids lightly (type-only conflicts with PR-8; rebase, no redesign):** R11 (editor error banner reads the `productId` extension), R14 (row highlight after save compares ids), R22 (focus by row).
- **Should wait for PR-8 or be written against string ids from the start:** R17 (Home feed links rows to `/receipts/:id`), R18 (`RecentsStore` stores document and product ids on the device), R19 (reviewer Home links), R20 (palette results open documents and `/stock/:id`). Tell the authors now: ids are opaque strings, never parsed, never compared numerically, never used as array indexes.
- **Wave placement:** the plan's waves 1 and 2 (R01 to R06) finish while the backend PRs are built. Aim to merge the integration branch at the wave 2 to 3 boundary, and in any case before R17 and R18 are built and before wave 5 (R14); if the backend is late, R17 to R20 wait. Freeze, for about one day, the lanes `containers/documents`, `features/*/services/*.store.ts`, `containers/catalog` and `containers/stock`; R09, R12 and R15 table work should not be mid-flight in those files at that moment (the collision map already serialises the table lane).
- **Backend items B1 and B2 of the plan** (a `returned` filter and an ascending order on `GET /api/documents`) change `DocumentsPageSpec` and the cursor: build them **after PR-2 and PR-7**, not before, so the spec is changed once. B4 (two-decimal rule) is independent.
- **e2e collisions:** page objects `documents.ts` (`savedId`) and `screens.ts` are edited by PR-2b and PR-9, one reserved slot, before `14` to `24` are written.

## 7. Evidence status and what the DBA numbers can change

Status at the revision (2026-10-11): `docs/dba/guid-pk/results/` holds only **variant C** (integer keys, 1M tier: sizes, fragmentation, 120 query rows, buffer-pool contents, write load of 16 writers at 252 to 398 tx/s, backup 8.3 s, restore 5.3 s) and `ef-guid-generators.txt`. Variants A1 (`NEWSEQUENTIALID()` keys), A2 (v7-shaped), A3 (`NEWID()`), B1 (hybrid, sequential `PublicId`) and B2 (hybrid, v7-shaped `PublicId`) were still running. Measured and used: C sizes, reads and throughput; the generator check (EF sequential 100 % ascending in SQL Server order, `CreateVersion7` 49.95 %, `NewGuid` 50.02 %, and consecutive EF ids differing by one counter step). Everything else about A and B is my estimate (C scaled by key width, +-15 %).

**Predictions to check against the results** (if one is false, the matching claim in the owner-preference section, 2.1 or 2.5 is wrong and this ADR must be corrected):

- A1 and B1 (sequential in SQL order): fragmentation near 0 and insert cost within noise of C.
- A3 (`NEWID()`) and **A2 (v7-shaped)**: both severely fragmented and about equal to each other. The generator check already shows v7 is not ascending in SQL order.
- B1 and B2: the clustered keys stay clean; only the small `PublicId` index fragments. B1 and B2 database size within 1 to 2 % of C.
- A1 database size about +45 % (ledger about 520 MB, lines about 495 MB, documents about 195 MB); ledger posting throughput 10 to 20 % under C.
- Single-row reads: C 3 logical reads; B 5 to 6; A 3.
- If B2 is measurably worse than B1 on the document insert path, server-made `PublicId`s should be random or v7 anyway when D2 is "leak-free" (a sequential value leaks volume), so the answer is a narrower index fill factor and a weekly reorganize, not a sequential generator.

| Assumption | If the DBA data says otherwise |
|---|---|
| Sequential-GUID (EF generator) inserts into a clustered index cost the same as identity within noise | Nothing changes for B. For A it removes the write-cost objection, not the ordering one |
| Random (v4/v7-in-`uniqueidentifier`) clustered inserts: about 99 % fragmentation and about 70 % page fullness after 1M rows, log volume 1.5 to 2x | If measured mild, a client-minted id in A is less bad; B still wins on ordering and effort |
| Ledger with indexes +80 % size and the database +30 % at the 1M tier | If measured much smaller (for example row compression), A's size objection weakens; ordering stands |
| A `PublicId` lookup adds 3 to 4 logical reads and no measurable latency | If a by-id endpoint moves above 1 s P95, I revisit (section 5) |
| The unique `PublicId` index fragments slowly on small tables (documents 167 k rows) | If fragmentation matters, move server-made ids to the EF sequential generator in `Infrastructure` (the column default already is `NEWSEQUENTIALID()`) |

Open for the DBA: index fill factor for `UX_*_PublicId` (suggested 90), whether to rebuild after `AddPublicIds` (`07-maintenance.sql`).

## 8. Decisions only the product owner can make

- **D1 (offline creation or data exchange between deployments within about a year): answered, no.** Consequence: client-minted ids and idempotent create are optional (PR-7b); the GUID case rests on the owner's preference, non-guessable ids and D2.
- **D2 (must a warehouse-scoped user be unable to infer other warehouses' activity from ids): open; this ADR assumes yes.** What each option gives: option 1 closes it fully (random or v7 public ids, no id on ledger and audit rows, encrypted cursors); options 2 and 3 close it only with random GUIDs as clustered keys (fragmentation, the cost shown in the owner-preference section) or not at all with sequential GUIDs, because the sequential counter reveals volume. If D2 is no, option 1 still stands and saves 4 hours.
- **The one question left:** is the purpose of the GUID that clients and users see non-guessable, non-countable ids (then option 1, about 85 hours), or must the SQL primary key column itself be a `uniqueidentifier` on the master and document tables for a standard or tooling reason (then option 2b, about 95 hours, +4 % size)?

Everything else in this note is engineering and I can decide it.
