/*
  01-seed-perf.sql  --  set-based, batched volume seed for the PERF database ONLY (never Ims_Dev).
  Run after the EF migrations have been applied to Ims_Perf (start the API once against it).

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -b -W -v Products=5000  Docs=21000   Days=360  Warehouses=3 -i docs/dba/01-seed-perf.sql   (~130k ledger rows)
    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -b -W -v Products=50000 Docs=161500  Days=1080 Warehouses=5 -i docs/dba/01-seed-perf.sql   (~1.0M ledger rows)
    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -b -W -v Products=50000 Docs=323000  Days=1800 Warehouses=5 -i docs/dba/01-seed-perf.sql   (~2.0M ledger rows)

  Ledger rows ~= 6.2 x Docs. Every 21st historic doc is Cancelled (no ledger); posted = Docs*20/21; plus 300 recent
  Draft/Submitted docs. One document line per product per document; one ledger row per line of every Posted document.
  Product popularity is skewed (Zipf-like, power 2) so a few hot products have thousands of ledger rows. Issues only draw from
  products already received in that warehouse and the books never go negative. StockBalance equals the ledger sums;
  DocumentSequence equals the numbers used; AuditLogs gets 1 row per status change (created + posted).

  Append-only trigger TR_StockLedgerEntry_AppendOnly is INSTEAD OF UPDATE, DELETE: plain INSERTs do not fire it, so it is NOT
  disabled for this load, and the ledger is never updated (corrections are applied to the staging table before the INSERT).
  Recovery model is SIMPLE during the load (Ims_Perf only) so the log does not grow with it.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;   -- required by the filtered indexes
IF DB_NAME() NOT LIKE N'Ims[_]Perf%' THROW 50000, N'Refusing to run: this script is for a perf database (name starts with Ims_Perf) only.', 1;
IF (SELECT COUNT(*) FROM Product) > 100 THROW 50001, N'This database already looks seeded.', 1;
DECLARE @recoverySql nvarchar(200) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET RECOVERY SIMPLE'; EXEC (@recoverySql);

DECLARE @now datetime2(7) = SYSUTCDATETIME();
DECLARE @products int = $(Products), @docs int = $(Docs), @days int = $(Days), @whs int = $(Warehouses);
DECLARE @cancelEvery int = 21, @recent int = 300, @batch int = 40000;
DECLARE @t0 datetime2 = SYSDATETIME(), @lo int, @hi int;
DECLARE @spacing float = @days * 86400.0 / @docs;

------------------------------------------------------------------ reference data
INSERT Category (Name) SELECT CONCAT(N'Category ', RIGHT(CONCAT('00', value), 2)) FROM GENERATE_SERIES(1, 49);
INSERT Warehouse (Code, Name, Contact, Notes, IsActive)
SELECT CONCAT(N'WH0', value), CONCAT(N'Branch warehouse ', value), NULL, NULL, 1 FROM GENERATE_SERIES(2, @whs);
INSERT Partner (Type, Name, Contact, IsActive) SELECT 1, CONCAT(N'Supplier ', value, N' Sdn Bhd'), NULL, 1 FROM GENERATE_SERIES(1, 40);
INSERT Partner (Type, Name, Contact, IsActive) SELECT 2, CONCAT(N'Customer ', value, N' Enterprise'), NULL, 1 FROM GENERATE_SERIES(1, 80);

------------------------------------------------------------------ products
SELECT value AS n,
       ABS(CHECKSUM(NEWID())) AS r1, ABS(CHECKSUM(NEWID())) AS r2, ABS(CHECKSUM(NEWID())) AS r3, ABS(CHECKSUM(NEWID())) AS r4
INTO #pr FROM GENERATE_SERIES(1, @products);

SELECT c.Id, ROW_NUMBER() OVER (ORDER BY c.Name) AS k INTO #cat FROM Category c WHERE c.Name LIKE N'Category %';
CREATE UNIQUE CLUSTERED INDEX ix ON #cat (k);

INSERT Product (Sku, Name, CategoryId, BaseUomId, SafetyStock, IsActive)
SELECT CONCAT(N'SKU-', RIGHT(CONCAT('00000', n), 5)),
       CONCAT(CHOOSE(1 + n % 8, N'Steel', N'Brass', N'Nylon', N'Copper', N'Zinc', N'Rubber', N'Plastic', N'Alloy'), N' ',
              CHOOSE(1 + (n / 8) % 10, N'bolt', N'nut', N'washer', N'bearing', N'gasket', N'valve', N'pipe', N'cable', N'switch', N'bracket'), N' ',
              CHOOSE(1 + (n / 80) % 6, N'M6', N'M8', N'M10', N'25mm', N'50mm', N'1 inch'), N' #', n),
       (SELECT c.Id FROM #cat c WHERE c.k = 1 + r1 % 49),
       1 + r2 % 4,
       CASE WHEN r3 % 100 < 40 THEN 0 ELSE 20 + r4 % 1500 END,
       CASE WHEN r1 % 50 = 0 THEN 0 ELSE 1 END
FROM #pr ORDER BY n;

SELECT p.Id AS pid, CAST(SUBSTRING(p.Sku, 5, 10) AS int) AS n, p.BaseUomId AS uom,
       CAST(1 + (r.r3 % 20000) / 100.0 AS decimal(18, 6)) AS cost            -- 1.00 .. 201.00
INTO #P FROM Product p JOIN #pr r ON r.n = CAST(SUBSTRING(p.Sku, 5, 10) AS int) WHERE p.Sku LIKE N'SKU-%';
CREATE UNIQUE CLUSTERED INDEX ix ON #P (n);

INSERT ProductBarcode (ProductId, Barcode) SELECT pid, CONCAT(N'9557', RIGHT(CONCAT('000000000', n), 9)) FROM #P ORDER BY n;

------------------------------------------------------------------ documents (staging)
SELECT value AS n, ABS(CHECKSUM(NEWID())) AS r1, ABS(CHECKSUM(NEWID())) AS r2, ABS(CHECKSUM(NEWID())) AS r3,
       ABS(CHECKSUM(NEWID())) AS r4, ABS(CHECKSUM(NEWID())) AS r5
INTO #r FROM GENERATE_SERIES(1, @docs + @recent);

SELECT n,
       CASE WHEN n <= @docs / 100 THEN 1 WHEN r1 % 100 < 40 THEN 1 ELSE 2 END AS typ,                -- 1 Receipt, 2 Issue
       CASE WHEN n > @docs THEN CASE WHEN n % 2 = 0 THEN 0 ELSE 1 END WHEN n % @cancelEvery = 0 THEN 5 ELSE 3 END AS st,
       1 + CAST(FLOOR(@whs * POWER((r2 % 1000000) / 1000000.0, 1.5)) AS int) AS wh,
       CASE WHEN n > @docs THEN DATEADD(SECOND, -((n - @docs) * 600), @now)
            ELSE DATEADD(SECOND, CAST((n - 1) * @spacing AS int) + r3 % CAST(0.6 * @spacing + 1 AS int), DATEADD(DAY, -(@days + 2), @now)) END AS createdAt,
       2 + r4 % 4 AS lc,
       r5, r4, r2, r1
INTO #D0 FROM #r;

SELECT d.n, d.typ, d.st, w.Id AS wh, d.createdAt, d.lc, d.r5, d.r4, d.r2, d.r1, w.Code AS whCode, CAST(d.createdAt AS datetime2) AS ca
INTO #D1 FROM #D0 d JOIN Warehouse w ON w.Code = CONCAT(N'WH0', d.wh);   -- map by Code: identity values can have gaps

SELECT *, CASE typ WHEN 1 THEN N'RCV' ELSE N'ISS' END AS prefix,
       ((YEAR(DATEADD(HOUR, 8, createdAt)) % 100) * 100 + MONTH(DATEADD(HOUR, 8, createdAt))) AS ym
INTO #D2 FROM #D1;

SELECT *, ROW_NUMBER() OVER (PARTITION BY prefix, wh, ym ORDER BY n) AS running
INTO #D FROM #D2;
CREATE UNIQUE CLUSTERED INDEX ix ON #D (n);
DROP TABLE #r, #D0, #D1, #D2, #pr;

------------------------------------------------------------------ receipt lines first (skewed product popularity)
SELECT d.n AS docn, j.value AS ln, ABS(CHECKSUM(NEWID())) AS a, ABS(CHECKSUM(NEWID())) AS b, ABS(CHECKSUM(NEWID())) AS c
INTO #rl0
FROM #D d CROSS APPLY GENERATE_SERIES(1, CASE WHEN d.lc + d.r5 % 7 > 11 THEN 11 ELSE d.lc + d.r5 % 7 END) j
WHERE d.typ = 1;

SELECT docn, ln,
       1 + CAST(FLOOR(@products * POWER((a % 1000000) / 1000000.0, 2)) AS int) AS pn,                -- Zipf-like: low n = hot product
       20 + b % 480 AS qty, 90 + c % 21 AS costpct
INTO #rl1 FROM #rl0;
DROP TABLE #rl0;

SELECT docn, ln, pn, qty, costpct, ROW_NUMBER() OVER (PARTITION BY docn, pn ORDER BY ln) AS dup
INTO #rl2 FROM #rl1;                                   -- one line per (doc, product)
DELETE FROM #rl2 WHERE dup > 1;
DROP TABLE #rl1;

SELECT d.wh, r.docn, r.ln, r.pn, r.qty, r.costpct, ROW_NUMBER() OVER (PARTITION BY d.wh ORDER BY r.docn, r.ln) AS rk
INTO #RL FROM #rl2 r JOIN #D d ON d.n = r.docn WHERE d.st = 3;   -- POSTED receipt lines per warehouse: what issues draw from
CREATE UNIQUE CLUSTERED INDEX ix ON #RL (wh, rk);
CREATE INDEX ix2 ON #RL (wh, docn) INCLUDE (rk);
------------------------------------------------------------------ issue lines: draw from earlier posted receipts in the same warehouse
-- maxrk = number of posted receipt lines in the doc's warehouse created before this doc (exclusive running total; O(n), no per-row probe)
SELECT d.n, d.wh, ISNULL(r.cnt, 0) AS cnt INTO #rc0 FROM #D d LEFT JOIN (SELECT docn, COUNT(*) AS cnt FROM #RL GROUP BY docn) r ON r.docn = d.n;
SELECT n, SUM(cnt) OVER (PARTITION BY wh ORDER BY n ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS maxrk INTO #rc FROM #rc0;
CREATE UNIQUE CLUSTERED INDEX ix ON #rc (n);
DROP TABLE #rc0;

SELECT d.n AS docn, d.wh, j.value AS ln, ABS(CHECKSUM(NEWID())) AS a, ABS(CHECKSUM(NEWID())) AS b, rc.maxrk
INTO #il0
FROM #D d JOIN #rc rc ON rc.n = d.n CROSS APPLY GENERATE_SERIES(1, CASE WHEN d.lc + d.r5 % 7 > 11 THEN 11 ELSE d.lc + d.r5 % 7 END) j
WHERE d.typ = 2;

SELECT i.docn, i.ln, rl.pn, 1 + i.b % 25 AS qty,
       ROW_NUMBER() OVER (PARTITION BY i.docn, rl.pn ORDER BY i.ln) AS dup
INTO #il1 FROM #il0 i JOIN #RL rl ON rl.wh = i.wh AND rl.rk = 1 + i.a % i.maxrk WHERE i.maxrk > 0;
DELETE FROM #il1 WHERE dup > 1;
DROP TABLE #il0;

SELECT docn, ln, pn, CAST(qty AS decimal(18, 4)) AS qty, costpct INTO #L0 FROM #rl2
UNION ALL SELECT docn, ln, pn, CAST(qty AS decimal(18, 4)), 100 FROM #il1;
DROP TABLE #rl2, #il1, #RL;

SELECT l.docn, l.ln, p.pid, p.uom, d.typ, d.st, d.wh, l.qty, CAST(p.cost * l.costpct / 100.0 AS decimal(18, 6)) AS unitcost
INTO #L FROM #L0 l JOIN #P p ON p.n = l.pn JOIN #D d ON d.n = l.docn;
CREATE UNIQUE CLUSTERED INDEX ix ON #L (docn, ln);
DROP TABLE #L0;

-- keep the books non-negative: any deficit goes onto the earliest posted receipt line of that (product, warehouse)
;WITH bal AS (
    SELECT wh, pid, SUM(CASE WHEN typ = 1 THEN qty ELSE -qty END) AS b FROM #L WHERE st = 3 GROUP BY wh, pid HAVING SUM(CASE WHEN typ = 1 THEN qty ELSE -qty END) < 0),
first_rcv AS (
    SELECT l.docn, l.ln, l.wh, l.pid, ROW_NUMBER() OVER (PARTITION BY l.wh, l.pid ORDER BY l.docn, l.ln) AS r FROM #L l WHERE l.st = 3 AND l.typ = 1)
UPDATE l SET qty = l.qty - bal.b
FROM #L l JOIN first_rcv f ON f.docn = l.docn AND f.ln = l.ln AND f.r = 1 JOIN bal ON bal.wh = f.wh AND bal.pid = f.pid;

DELETE d FROM #D d WHERE NOT EXISTS (SELECT 1 FROM #L l WHERE l.docn = d.n);   -- documents left without lines (not expected)

------------------------------------------------------------------ real tables
INSERT StockDocument (Type, No, Status, WarehouseId, PartnerId, Purpose, Remark, CreatedBy, CreatedAt, SubmittedBy, SubmittedAt,
                      ApprovedBy, ApprovedAt, PostedBy, PostedAt, RequiresApproval, RejectionReason, CancelReason, ReversalReason,
                      ReversesDocumentId, NegativeStockOverrideReason)
SELECT d.typ,
       CONCAT(d.prefix, N'-', d.whCode, N'-', RIGHT(CONCAT('0000', d.ym), 4), N'-', RIGHT(CONCAT('0000', d.running), 4)),
       d.st, d.wh,
       CASE WHEN d.typ = 1 THEN (SELECT p.Id FROM Partner p WHERE p.Type = 1 AND p.Name = CONCAT(N'Supplier ', 1 + d.r1 % 40, N' Sdn Bhd'))
            WHEN d.r4 % 10 < 7 THEN (SELECT p.Id FROM Partner p WHERE p.Type = 2 AND p.Name = CONCAT(N'Customer ', 1 + d.r1 % 80, N' Enterprise')) END,
       CASE WHEN d.typ = 2 THEN CASE WHEN d.r4 % 10 < 7 THEN 1 WHEN d.r4 % 10 < 9 THEN 2 ELSE 3 END END,
       CASE WHEN d.r5 % 10 = 0 THEN N'Perf seed document' END,
       3 + d.r2 % 3,                                                    -- supervisor, staff, clerk
       d.ca,
       CASE WHEN d.st <> 0 THEN 3 + d.r2 % 3 END, CASE WHEN d.st <> 0 THEN DATEADD(MINUTE, 10, d.ca) END,
       NULL, NULL,
       CASE WHEN d.st = 3 THEN 3 END, CASE WHEN d.st = 3 THEN DATEADD(HOUR, 1, d.ca) END,
       0, NULL, CASE WHEN d.st = 5 THEN N'Customer changed the order' END, NULL, NULL, NULL
FROM #D d ORDER BY d.n;
PRINT CONCAT('documents inserted ', DATEDIFF(SECOND, @t0, SYSDATETIME()), 's');

SELECT d.n AS docn, s.Id AS docid INTO #M FROM #D d JOIN StockDocument s ON s.No = CONCAT(d.prefix, N'-', d.whCode, N'-', RIGHT(CONCAT('0000', d.ym), 4), N'-', RIGHT(CONCAT('0000', d.running), 4));
CREATE UNIQUE CLUSTERED INDEX ix ON #M (docn);

-- lines and ledger in batches of ~@batch documents (ascending, so identity order follows document order and time)
SELECT @lo = MIN(n), @hi = MAX(n) FROM #D;
WHILE @lo <= @hi
BEGIN
    INSERT StockDocumentLine (DocumentId, ProductId, Qty, UomId, BaseQty, UnitCost, Remark)
    SELECT m.docid, l.pid, l.qty, l.uom, l.qty, l.unitcost, NULL
    FROM #L l JOIN #M m ON m.docn = l.docn WHERE l.docn >= @lo AND l.docn < @lo + @batch ORDER BY l.docn, l.ln;

    INSERT StockLedgerEntry (ProductId, WarehouseId, QtyDelta, UnitCost, SourceDocId, Reason, OccurredAt, CreatedBy)
    SELECT l.pid, l.wh, CASE WHEN l.typ = 1 THEN l.qty ELSE -l.qty END, l.unitcost, m.docid, CASE WHEN l.typ = 1 THEN 1 ELSE 2 END,
           DATEADD(HOUR, 1, d.ca), 3
    FROM #L l JOIN #M m ON m.docn = l.docn JOIN #D d ON d.n = l.docn
    WHERE d.st = 3 AND l.docn >= @lo AND l.docn < @lo + @batch ORDER BY d.ca, l.docn, l.ln;

    SET @lo += @batch;
    PRINT CONCAT('lines+ledger batch done, next doc ', @lo, ' at ', DATEDIFF(SECOND, @t0, SYSDATETIME()), 's');
END

INSERT StockBalance (ProductId, WarehouseId, QtyOnHand, QtyInTransit, AvgCost)
SELECT ProductId, WarehouseId, SUM(QtyDelta), 0,
       CAST(ISNULL(SUM(CASE WHEN QtyDelta > 0 THEN QtyDelta * UnitCost END) / NULLIF(SUM(CASE WHEN QtyDelta > 0 THEN QtyDelta END), 0), 0) AS decimal(22, 10))
FROM StockLedgerEntry GROUP BY ProductId, WarehouseId;

INSERT DocumentSequence (Prefix, YearMonth, WarehouseId, LastNumber)
SELECT prefix, ym, wh, MAX(running) FROM #D GROUP BY prefix, ym, wh;

INSERT AuditLogs (Actor, Action, EntityType, EntityId, Before, After, At)
SELECT x.Actor, x.Action, x.EntityType, x.EntityId, x.Before, x.After, x.At FROM (
    SELECT N'staff' AS Actor, N'DocumentCreated' AS Action, N'StockDocument' AS EntityType, s.No AS EntityId, CAST(NULL AS nvarchar(max)) AS Before,
           N'{"Status":0}' AS After, s.CreatedAt AS At
    FROM StockDocument s
    UNION ALL
    SELECT N'supervisor', N'DocumentPosted', N'StockDocument', s.No, N'{"Status":1}', N'{"Status":3}', s.PostedAt
    FROM StockDocument s WHERE s.Status = 3) x
ORDER BY x.At;

------------------------------------------------------------------ statistics + sanity
UPDATE STATISTICS Product WITH FULLSCAN;        UPDATE STATISTICS ProductBarcode WITH FULLSCAN;
UPDATE STATISTICS StockDocument WITH FULLSCAN;  UPDATE STATISTICS StockDocumentLine WITH FULLSCAN;
UPDATE STATISTICS StockLedgerEntry WITH FULLSCAN; UPDATE STATISTICS StockBalance WITH FULLSCAN;
UPDATE STATISTICS AuditLogs WITH FULLSCAN;
SET @recoverySql = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET RECOVERY FULL'; EXEC (@recoverySql);   -- back to the model default

SELECT N'products' AS what, COUNT(*) AS n FROM Product UNION ALL
SELECT N'warehouses', COUNT(*) FROM Warehouse UNION ALL
SELECT N'documents', COUNT(*) FROM StockDocument UNION ALL
SELECT N'  posted', COUNT(*) FROM StockDocument WHERE Status = 3 UNION ALL
SELECT N'document lines', COUNT(*) FROM StockDocumentLine UNION ALL
SELECT N'ledger rows', COUNT(*) FROM StockLedgerEntry UNION ALL
SELECT N'balance rows', COUNT(*) FROM StockBalance UNION ALL
SELECT N'balance rows qty<0', COUNT(*) FROM StockBalance WHERE QtyOnHand < 0 UNION ALL
SELECT N'balance <> ledger sum (must be 0)', COUNT(*) FROM (SELECT ProductId, WarehouseId, SUM(QtyDelta) q FROM StockLedgerEntry GROUP BY ProductId, WarehouseId) l
  FULL JOIN StockBalance b ON b.ProductId = l.ProductId AND b.WarehouseId = l.WarehouseId WHERE ISNULL(b.QtyOnHand, -1) <> ISNULL(l.q, -2) UNION ALL
SELECT N'audit rows', COUNT(*) FROM AuditLogs UNION ALL
SELECT N'low-stock balance rows', COUNT(*) FROM StockBalance b JOIN Product p ON p.Id = b.ProductId WHERE p.IsActive = 1 AND p.SafetyStock > 0 AND b.QtyOnHand < p.SafetyStock;
SELECT DATEDIFF(SECOND, @t0, SYSDATETIME()) AS seed_seconds;
EXEC sp_spaceused;
