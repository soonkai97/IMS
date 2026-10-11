/*
  40-hot-queries.sql -- the hot read queries of the product (shapes taken from docs/dba/README.md and the EF statements it measured), run against the
  database in its CURRENT state. Same text for every key strategy; only the literals differ (a key is an int or a GUID).
  For every case: rep 0 = cold cache (CHECKPOINT + DROPCLEANBUFFERS first, plan compiled here), reps 1..$(Reps) = warm. Per execution it records
  logical reads and physical reads (sys.dm_exec_requests deltas, equal to SET STATISTICS IO) and elapsed microseconds.
  Variables: Tag (label stored with the rows), Reps (warm repetitions, 5). Results: dbo._QueryResults. The result sets of the queries themselves
  go to the sqlcmd output file and are discarded by the driver.
  Not instance-safe: DROPCLEANBUFFERS flushes the cache of the whole instance (scratch machine only).
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
IF DB_NAME() NOT LIKE N'Ims[_]PerfGuid%' THROW 50000, N'Scratch databases only.', 1;
IF OBJECT_ID(N'dbo._QueryResults') IS NULL
    CREATE TABLE dbo._QueryResults (Id int IDENTITY PRIMARY KEY, Tag varchar(60) NOT NULL, Q varchar(40) NOT NULL, PSet int NOT NULL, Rep int NOT NULL,
                                    LogicalReads bigint NOT NULL, PhysicalReads bigint NOT NULL, ElapsedUs bigint NOT NULL);

------------------------------------------------------------------ parameters (derived without key literals, so the same code serves int and GUID keys)
CREATE TABLE #P (k int PRIMARY KEY, n int, pid nvarchar(50), sku nvarchar(64), bc nvarchar(64), cnt int, aft nvarchar(50));
DECLARE @np int = (SELECT COUNT(*) FROM dbo._Pick_Product);
INSERT #P (k, n) VALUES (1, 1), (2, 10), (3, 300), (4, @np * 8 / 50), (5, @np * 40 / 50);   -- product numbers: hottest ... cold (Zipf-skewed popularity; 8000 and 40000 of 50000)
UPDATE p SET pid = CONVERT(nvarchar(50), pp.ProductId), sku = CONCAT(N'SKU-', RIGHT(CONCAT('00000', p.n), 5)), bc = CONCAT(N'9557', RIGHT(CONCAT('000000000', p.n), 9))
FROM #P p JOIN dbo._Pick_Product pp ON pp.n = p.n;
UPDATE p SET cnt = (SELECT COUNT(*) FROM dbo.StockLedgerEntry e WHERE e.ProductId = pp.ProductId) FROM #P p JOIN dbo._Pick_Product pp ON pp.n = p.n;
UPDATE p SET aft = CONVERT(nvarchar(50), (SELECT x.Id FROM (SELECT e.Id, ROW_NUMBER() OVER (ORDER BY e.Id DESC) AS rn FROM dbo.StockLedgerEntry e WHERE e.ProductId = pp.ProductId) x
                                           WHERE x.rn = p.cnt / 2 + 1))
FROM #P p JOIN dbo._Pick_Product pp ON pp.n = p.n;

CREATE TABLE #D (k int PRIMARY KEY, id nvarchar(50), no nvarchar(50), pub nvarchar(50));
;WITH d AS (SELECT Id, No, ROW_NUMBER() OVER (ORDER BY CreatedAt, Id) AS rn, COUNT(*) OVER () AS total FROM dbo.StockDocument WHERE Status = 3)
INSERT #D (k, id, no)
SELECT v.k, CONVERT(nvarchar(50), d.Id), d.No FROM (VALUES (1, 0.02), (2, 0.25), (3, 0.5), (4, 0.75), (5, 0.98)) v (k, f) JOIN d ON d.rn = CAST(d.total * v.f AS int) + 1;
DECLARE @hasPub bit = IIF(COL_LENGTH(N'dbo.StockDocument', N'PublicId') IS NULL, 0, 1);
IF @hasPub = 1 EXEC (N'UPDATE d SET pub = CONVERT(nvarchar(50), s.PublicId) FROM #D d JOIN dbo.StockDocument s ON s.No = d.no');

DECLARE @mp datetime2 = (SELECT MAX(PostedAt) FROM dbo.StockDocument);
DECLARE @mc datetime2 = (SELECT MAX(CreatedAt) FROM dbo.StockDocument);
CREATE TABLE #W (k int PRIMARY KEY, pFrom nvarchar(40), pTo nvarchar(40), cFrom nvarchar(40), cTo nvarchar(40), offs int);   -- date windows ending at the newest document
INSERT #W (k, pFrom, pTo, cFrom, cTo, offs) VALUES
 (1, NULL, NULL, NULL, NULL, 0),
 (2, CONVERT(nvarchar(40), DATEADD(DAY, -1, @mp), 126), CONVERT(nvarchar(40), DATEADD(DAY, 1, @mp), 126), CONVERT(nvarchar(40), DATEADD(DAY, -1, @mc), 126), CONVERT(nvarchar(40), DATEADD(DAY, 1, @mc), 126), 0),
 (3, CONVERT(nvarchar(40), DATEADD(DAY, -7, @mp), 126), CONVERT(nvarchar(40), DATEADD(DAY, 1, @mp), 126), CONVERT(nvarchar(40), DATEADD(DAY, -7, @mc), 126), CONVERT(nvarchar(40), DATEADD(DAY, 1, @mc), 126), 0),
 (4, CONVERT(nvarchar(40), DATEADD(DAY, -30, @mp), 126), CONVERT(nvarchar(40), DATEADD(DAY, 1, @mp), 126), CONVERT(nvarchar(40), DATEADD(DAY, -30, @mc), 126), CONVERT(nvarchar(40), DATEADD(DAY, 1, @mc), 126), 0),
 (5, CONVERT(nvarchar(40), DATEADD(DAY, -30, @mp), 126), CONVERT(nvarchar(40), DATEADD(DAY, 1, @mp), 126), CONVERT(nvarchar(40), DATEADD(DAY, -30, @mc), 126), CONVERT(nvarchar(40), DATEADD(DAY, 1, @mc), 126), 1000);

------------------------------------------------------------------ the cases
CREATE TABLE #Cases (id int IDENTITY PRIMARY KEY, q varchar(40), pset int, sql nvarchar(max));

-- stock search by SKU / by barcode / by SKU prefix (barcode resolved by one seek, as R1 in docs/dba/README.md)
INSERT #Cases (q, pset, sql) SELECT 'stock_by_sku', k, CONCAT(N'SELECT p.Id, p.Sku, p.Name, b.WarehouseId, b.QtyOnHand, b.AvgCost FROM dbo.Product p LEFT JOIN dbo.StockBalance b ON b.ProductId = p.Id AND b.REC_DELETED = 0 WHERE p.REC_DELETED = 0 AND p.Sku = N''', sku, N''' ORDER BY p.Sku') FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'stock_by_barcode', k, CONCAT(N'SELECT p.Id, p.Sku, p.Name, b.WarehouseId, b.QtyOnHand, b.AvgCost FROM dbo.ProductBarcode pb JOIN dbo.Product p ON p.Id = pb.ProductId LEFT JOIN dbo.StockBalance b ON b.ProductId = p.Id AND b.REC_DELETED = 0 WHERE pb.REC_DELETED = 0 AND pb.Barcode = N''', bc, N''' AND p.REC_DELETED = 0') FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'stock_sku_prefix_page', k, CONCAT(N'SELECT TOP (50) p.Id, p.Sku, p.Name, SUM(b.QtyOnHand) AS Qty FROM dbo.Product p LEFT JOIN dbo.StockBalance b ON b.ProductId = p.Id AND b.REC_DELETED = 0 WHERE p.REC_DELETED = 0 AND p.Sku LIKE N''', LEFT(sku, 8), N'%'' GROUP BY p.Id, p.Sku, p.Name ORDER BY p.Sku') FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'stock_by_warehouse_top', k, N'SELECT TOP (50) b.ProductId, b.WarehouseId, b.QtyOnHand FROM dbo.StockBalance b JOIN dbo.Warehouse w ON w.Id = b.WarehouseId WHERE w.Code = N''WH0' + CAST(k AS nvarchar(2)) + N''' AND b.REC_DELETED = 0 ORDER BY b.QtyOnHand DESC' FROM #P;

-- product list by name (pages at growing offsets) and its count
INSERT #Cases (q, pset, sql) SELECT 'product_list_name', k, CONCAT(N'SELECT p.Id, p.Sku, p.Name, p.IsActive FROM dbo.Product p WHERE p.REC_DELETED = 0 ORDER BY p.Name, p.Id OFFSET ', CHOOSE(k, 0, 50, 500, 5000, 9950), N' ROWS FETCH NEXT 50 ROWS ONLY') FROM #P;
INSERT #Cases (q, pset, sql) VALUES ('product_list_count', 1, N'SELECT COUNT(*) FROM dbo.Product p WHERE p.REC_DELETED = 0');

-- movements report: page (newest posting first, document id then line id as tie order) and count; windows: all, 1 day, 7 days, 30 days, 30 days page 21
DECLARE @mv nvarchar(max) = N'FROM dbo.StockDocumentLine l JOIN dbo.StockDocument d ON d.Id = l.DocumentId JOIN dbo.Product p ON p.Id = l.ProductId WHERE l.REC_DELETED = 0 AND d.REC_DELETED = 0 AND d.Status = 3 AND d.PostedAt IS NOT NULL';
INSERT #Cases (q, pset, sql)
SELECT 'movements_page', k, CONCAT(N'SELECT l.Id, d.PostedAt, d.No, d.Type, l.ProductId, p.Sku, l.Qty, l.BaseQty, l.UnitCost ', @mv,
       IIF(pFrom IS NULL, N'', CONCAT(N' AND d.PostedAt >= ''', pFrom, N''' AND d.PostedAt < ''', pTo, N'''')),
       N' ORDER BY d.PostedAt DESC, d.Id DESC, l.Id OFFSET ', offs, N' ROWS FETCH NEXT 50 ROWS ONLY') FROM #W;
INSERT #Cases (q, pset, sql)
SELECT 'movements_count', k, CONCAT(N'SELECT COUNT_BIG(*) ', @mv, IIF(pFrom IS NULL, N'', CONCAT(N' AND d.PostedAt >= ''', pFrom, N''' AND d.PostedAt < ''', pTo, N''''))) FROM #W WHERE offs = 0;
INSERT #Cases (q, pset, sql)
SELECT 'movements_hot_product', k, CONCAT(N'SELECT TOP (50) l.Id, d.PostedAt, d.No, d.Type, l.ProductId, l.Qty, l.UnitCost ', @mv, N' AND l.ProductId = ''', pid, N''' ORDER BY d.PostedAt DESC, d.Id DESC, l.Id') FROM #P;

-- ledger page by product: first page and deep cursor ordered by the key (identity: chronological; GUID: see 50-cursor-walk.sql), and sums
INSERT #Cases (q, pset, sql) SELECT 'ledger_first_page_by_id', k, CONCAT(N'SELECT TOP (50) e.Id, e.ProductId, e.WarehouseId, e.QtyDelta, e.UnitCost, e.SourceDocId, e.Reason, e.OccurredAt FROM dbo.StockLedgerEntry e WHERE e.ProductId = ''', pid, N''' ORDER BY e.Id DESC') FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'ledger_cursor_by_id', k, CONCAT(N'SELECT TOP (50) e.Id, e.ProductId, e.WarehouseId, e.QtyDelta, e.UnitCost, e.SourceDocId, e.Reason, e.OccurredAt FROM dbo.StockLedgerEntry e WHERE e.ProductId = ''', pid, N''' AND e.Id < ''', aft, N''' ORDER BY e.Id DESC') FROM #P WHERE aft IS NOT NULL;
INSERT #Cases (q, pset, sql) SELECT 'ledger_sum_by_product', k, CONCAT(N'SELECT COUNT_BIG(*), SUM(e.QtyDelta) FROM dbo.StockLedgerEntry e WHERE e.ProductId = ''', pid, N'''') FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'ledger_by_source_doc', k, CONCAT(N'SELECT e.Id, e.ProductId, e.QtyDelta, e.UnitCost, e.OccurredAt FROM dbo.StockLedgerEntry e WHERE e.SourceDocId = ''', id, N'''') FROM #D;

-- audit by actor (rare and common users), count, and by entity id prefix
INSERT #Cases (q, pset, sql) SELECT 'audit_by_actor', k, CONCAT(N'SELECT TOP (50) a.Id, a.Actor, a.Action, a.EntityType, a.EntityId, a.[At] FROM dbo.AuditLogs a WHERE a.Actor = N''', CHOOSE(k, N'clerk', N'staff3', N'staff', N'supervisor', N'admin'), N''' ORDER BY a.Id DESC') FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'audit_by_actor_count', k, CONCAT(N'SELECT COUNT_BIG(*) FROM dbo.AuditLogs a WHERE a.Actor = N''', CHOOSE(k, N'clerk', N'staff3', N'staff', N'supervisor', N'admin'), N'''') FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'audit_by_entity_prefix', k, CONCAT(N'SELECT TOP (50) a.Id, a.Actor, a.Action, a.EntityId, a.[At] FROM dbo.AuditLogs a WHERE a.EntityId LIKE N''', LEFT(no, 14), N'%'' ORDER BY a.Id DESC') FROM #D;

-- document list: by status and type (newest first), by date range (page and count)
INSERT #Cases (q, pset, sql) SELECT 'docs_by_status_type', k, CONCAT(N'SELECT TOP (50) d.Id, d.No, d.Type, d.Status, d.WarehouseId, d.CreatedAt FROM dbo.StockDocument d WHERE d.REC_DELETED = 0 AND d.Status = ', CHOOSE(k, 3, 3, 0, 5, 3), IIF(k <= 2, CONCAT(N' AND d.Type = ', k), N''), N' ORDER BY d.Id DESC') FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'docs_by_status_type_count', k, CONCAT(N'SELECT COUNT_BIG(*) FROM dbo.StockDocument d WHERE d.REC_DELETED = 0 AND d.Status = ', CHOOSE(k, 3, 3, 0, 5, 3), IIF(k <= 2, CONCAT(N' AND d.Type = ', k), N'')) FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'docs_by_date_page', k, CONCAT(N'SELECT d.Id, d.No, d.Type, d.Status, d.WarehouseId, d.CreatedAt FROM dbo.StockDocument d WHERE d.REC_DELETED = 0',
       IIF(cFrom IS NULL, N'', CONCAT(N' AND d.CreatedAt >= ''', cFrom, N''' AND d.CreatedAt < ''', cTo, N'''')), N' ORDER BY d.CreatedAt DESC, d.Id DESC OFFSET ', offs, N' ROWS FETCH NEXT 50 ROWS ONLY') FROM #W;
INSERT #Cases (q, pset, sql) SELECT 'docs_by_date_count', k, CONCAT(N'SELECT COUNT_BIG(*) FROM dbo.StockDocument d WHERE d.REC_DELETED = 0 AND d.CreatedAt >= ''', cFrom, N''' AND d.CreatedAt < ''', cTo, N'''') FROM #W WHERE k BETWEEN 2 AND 4;

-- point lookups by id (document, product, ledger row), document by number
INSERT #Cases (q, pset, sql) SELECT 'doc_by_id', k, CONCAT(N'SELECT d.* FROM dbo.StockDocument d WHERE d.Id = ''', id, N'''') FROM #D;
INSERT #Cases (q, pset, sql) SELECT 'doc_by_no', k, CONCAT(N'SELECT d.* FROM dbo.StockDocument d WHERE d.REC_DELETED = 0 AND d.No = N''', no, N'''') FROM #D;
INSERT #Cases (q, pset, sql) SELECT 'product_by_id', k, CONCAT(N'SELECT p.* FROM dbo.Product p WHERE p.Id = ''', pid, N'''') FROM #P;
INSERT #Cases (q, pset, sql) SELECT 'ledger_by_id', k, CONCAT(N'SELECT e.* FROM dbo.StockLedgerEntry e WHERE e.Id = ''', aft, N'''') FROM #P WHERE aft IS NOT NULL;
IF @hasPub = 1
BEGIN
    INSERT #Cases (q, pset, sql) SELECT 'doc_by_public_id', k, CONCAT(N'SELECT d.* FROM dbo.StockDocument d WHERE d.PublicId = ''', pub, N'''') FROM #D;
    INSERT #Cases (q, pset, sql) SELECT 'doc_by_public_id_with_lines', k, CONCAT(N'SELECT d.Id, d.No, d.Status, l.Id, l.ProductId, l.Qty, l.UnitCost FROM dbo.StockDocument d JOIN dbo.StockDocumentLine l ON l.DocumentId = d.Id WHERE d.PublicId = ''', pub, N''' AND l.REC_DELETED = 0') FROM #D;
END

-- foreign-key joins: document with its lines and product names; a day/week/month of documents joined to their lines
INSERT #Cases (q, pset, sql) SELECT 'doc_with_lines', k, CONCAT(N'SELECT d.Id, d.No, d.Status, l.Id AS LineId, l.ProductId, p.Sku, p.Name, l.Qty, l.UnitCost FROM dbo.StockDocument d JOIN dbo.StockDocumentLine l ON l.DocumentId = d.Id JOIN dbo.Product p ON p.Id = l.ProductId WHERE d.Id = ''', id, N''' AND l.REC_DELETED = 0') FROM #D;
INSERT #Cases (q, pset, sql) SELECT 'docs_join_lines_range', k, CONCAT(N'SELECT COUNT_BIG(*), SUM(l.Qty) FROM dbo.StockDocument d JOIN dbo.StockDocumentLine l ON l.DocumentId = d.Id WHERE d.REC_DELETED = 0 AND l.REC_DELETED = 0 AND d.CreatedAt >= ''', cFrom, N''' AND d.CreatedAt < ''', cTo, N'''') FROM #W WHERE k BETWEEN 2 AND 4;

------------------------------------------------------------------ run
EXEC (N'UPDATE STATISTICS dbo.StockLedgerEntry; ');   -- keep the statistics the optimizer sees identical in spirit across variants (sampled default)
CHECKPOINT;
DECLARE @id int, @q varchar(40), @pset int, @sql nvarchar(max), @rep int, @reps int = $(Reps);
DECLARE @r0 bigint, @r1 bigint, @f0 bigint, @f1 bigint, @t0 datetime2(7), @t1 datetime2(7);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT id, q, pset, sql FROM #Cases ORDER BY id;
OPEN c; FETCH NEXT FROM c INTO @id, @q, @pset, @sql;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @rep = 0;
    WHILE @rep <= @reps
    BEGIN
        IF @rep = 0 DBCC DROPCLEANBUFFERS WITH NO_INFOMSGS;
        SELECT @r0 = logical_reads, @f0 = reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
        SET @t0 = SYSDATETIME();
        EXEC (@sql);
        SET @t1 = SYSDATETIME();
        SELECT @r1 = logical_reads, @f1 = reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
        INSERT dbo._QueryResults (Tag, Q, PSet, Rep, LogicalReads, PhysicalReads, ElapsedUs) VALUES ('$(Tag)', @q, @pset, @rep, @r1 - @r0, @f1 - @f0, DATEDIFF_BIG(MICROSECOND, @t0, @t1));
        SET @rep += 1;
    END
    FETCH NEXT FROM c INTO @id, @q, @pset, @sql;
END
CLOSE c; DEALLOCATE c;
SELECT COUNT(*) AS cases_run FROM #Cases;



