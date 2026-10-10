/*
  09-rewrite-candidates.sql -- evidence for the query rewrites in docs/dba/README.md (section "Rewrites"). Each pair runs the statement
  exactly as EF Core generates it today ("EF") and the rewritten statement ("RW"), twice (compile, then warm; the second run counts),
  with SET STATISTICS IO/TIME. The extra indexes the rewrites need are created here as IX_cand_* and dropped at the end, so the
  database is left as it was found.

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -W -v Pid=3 -i docs/dba/09-rewrite-candidates.sql -o rewrites.txt
    awk -f docs/dba/summarize-stats.awk rewrites.txt          # one line per statement: logical reads, CPU, elapsed

  R1 (stock/product search: barcode resolved up front) is IMPLEMENTED in src/Ims.Application (ProductSearch). R2-R4 change the order of
  rows that tie on the sort key, so they are recommendations that need an owner's decision, not applied code.
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() NOT LIKE N'Ims[_]Perf%' THROW 50000, N'Experiment script: a perf database only.', 1;

-- candidate indexes (dropped again at the end) -------------------------------------------------------------------------------------------
CREATE UNIQUE INDEX IX_cand_Product_Sku_cover ON dbo.Product (Sku) INCLUDE (Name, BaseUomId, CategoryId, SafetyStock) WHERE REC_DELETED = 0;
CREATE INDEX IX_cand_StockBalance_QtyOnHand ON dbo.StockBalance (QtyOnHand DESC) INCLUDE (AvgCost, QtyInTransit) WHERE REC_DELETED = 0;
CREATE INDEX IX_cand_StockDocument_PostedAt ON dbo.StockDocument (PostedAt DESC) INCLUDE (Status, Type, WarehouseId, No, PartnerId) WHERE REC_DELETED = 0;
CREATE INDEX IX_cand_StockDocumentLine_DocumentId_Cover ON dbo.StockDocumentLine (DocumentId, Id) INCLUDE (ProductId, Qty, UomId, BaseQty, UnitCost) WHERE REC_DELETED = 0;

DECLARE @now datetime2 = SYSUTCDATETIME();
DECLARE @s30 datetime2 = DATEADD(DAY, -30, @now);

-- shared statement pieces (EF shapes after the AuditColumns migration) ---------------------------------------------------------------------
DECLARE @stockHead nvarchar(max) = N'SELECT s0.ProductId, s0.Sku, s0.Name, c0.Name, u0.Code, s0.WarehouseId, s0.Code, s0.QtyOnHand, s0.QtyInTransit, s0.AvgCost, s0.QtyOnHand * s0.AvgCost, s0.SafetyStock
FROM (SELECT TOP(@take) s.ProductId, s.WarehouseId, s.AvgCost, s.QtyInTransit, s.QtyOnHand, p0.BaseUomId, p0.CategoryId, p0.Name, p0.SafetyStock, p0.Sku, w0.Code
      FROM StockBalance s
      INNER JOIN (SELECT p.Id, p.BaseUomId, p.CategoryId, p.Name, p.SafetyStock, p.Sku FROM Product p WHERE p.REC_DELETED = CAST(0 AS bit)) p0 ON s.ProductId = p0.Id
      INNER JOIN (SELECT w.Id, w.Code FROM Warehouse w WHERE w.REC_DELETED = CAST(0 AS bit)) w0 ON s.WarehouseId = w0.Id
      WHERE s.REC_DELETED = CAST(0 AS bit) ';
DECLARE @stockTail nvarchar(max) = N') s0
LEFT JOIN (SELECT c.Id, c.Name FROM Category c WHERE c.REC_DELETED = CAST(0 AS bit)) c0 ON s0.CategoryId = c0.Id
INNER JOIN (SELECT u.Id, u.Code FROM Uom u WHERE u.REC_DELETED = CAST(0 AS bit)) u0 ON s0.BaseUomId = u0.Id
ORDER BY @@ORDER@@';
DECLARE @searchEf nvarchar(max) = N'AND (p0.Sku LIKE @like ESCAPE N''\'' OR p0.Name LIKE @like ESCAPE N''\'' OR EXISTS (SELECT 1 FROM ProductBarcode p1 WHERE p1.REC_DELETED = CAST(0 AS bit) AND p0.Id = p1.ProductId AND p1.Barcode = @code)) ';
DECLARE @searchRw nvarchar(max) = N'AND (p0.Sku LIKE @like ESCAPE N''\'' OR p0.Name LIKE @like ESCAPE N''\'' OR p0.Id = @barcodePid) ';
DECLARE @movHead nvarchar(max) = N'SELECT s2.PostedAt, s2.No, s2.Type, s2.Status, w0.Code, p0.Name, p2.Sku, p2.Name, s2.Qty, u0.Code, s2.BaseQty, s2.UnitCost, s2.BaseQty * s2.UnitCost
FROM (SELECT TOP(@take) s.Id, s.DocumentId, s.BaseQty, s.ProductId, s.Qty, s.UnitCost, s.UomId, s1.No, s1.PartnerId, s1.PostedAt, s1.Status, s1.Type, s1.WarehouseId
      FROM StockDocumentLine s
      INNER JOIN (SELECT s0.Id, s0.No, s0.PartnerId, s0.PostedAt, s0.Status, s0.Type, s0.WarehouseId FROM StockDocument s0 WHERE s0.REC_DELETED = CAST(0 AS bit)) s1 ON s.DocumentId = s1.Id
      WHERE s.REC_DELETED = CAST(0 AS bit) AND s1.Status IN (3, 4) ';
DECLARE @movTail nvarchar(max) = N') s2
INNER JOIN (SELECT w.Id, w.Code FROM Warehouse w WHERE w.REC_DELETED = CAST(0 AS bit)) w0 ON s2.WarehouseId = w0.Id
LEFT JOIN (SELECT p.Id, p.Name FROM Partner p WHERE p.REC_DELETED = CAST(0 AS bit)) p0 ON s2.PartnerId = p0.Id
INNER JOIN (SELECT p1.Id, p1.Name, p1.Sku FROM Product p1 WHERE p1.REC_DELETED = CAST(0 AS bit)) p2 ON s2.ProductId = p2.Id
INNER JOIN (SELECT u.Id, u.Code FROM Uom u WHERE u.REC_DELETED = CAST(0 AS bit)) u0 ON s2.UomId = u0.Id
ORDER BY s2.PostedAt DESC, @@ORDER@@';

CREATE TABLE #t (id int IDENTITY, label varchar(90), sql nvarchar(max));
INSERT #t (label, sql) VALUES
 -- R1 barcode resolved up front (implemented) ------------------------------------------------------------------------------------------------
 ('R1 EF: stock search 9557000000042 (OR EXISTS barcode)', @stockHead + @searchEf + N'ORDER BY p0.Sku, w0.Code' + REPLACE(@stockTail, N'@@ORDER@@', N's0.Sku, s0.Code')),
 ('R1 RW: stock search 9557000000042 (barcode id up front)', @stockHead + @searchRw + N'ORDER BY p0.Sku, w0.Code' + REPLACE(@stockTail, N'@@ORDER@@', N's0.Sku, s0.Code')),
 -- R2 stock list order: (Sku, WarehouseId) instead of (Sku, Warehouse.Code) --------------------------------------------------------------------
 ('R2 EF: stock page ORDER BY Sku, Warehouse.Code', @stockHead + N'ORDER BY p0.Sku, w0.Code' + REPLACE(@stockTail, N'@@ORDER@@', N's0.Sku, s0.Code')),
 ('R2 RW: stock page ORDER BY Sku, Id, WarehouseId (+cover index)', @stockHead + N'ORDER BY p0.Sku, p0.Id, s.WarehouseId' + REPLACE(@stockTail, N'@@ORDER@@', N's0.Sku, s0.WarehouseId')),
 ('R2 EF: stock sort QtyOnHand desc, Sku', @stockHead + N'ORDER BY s.QtyOnHand DESC, p0.Sku' + REPLACE(@stockTail, N'@@ORDER@@', N's0.QtyOnHand DESC, s0.Sku')),
 ('R2 RW: stock sort QtyOnHand desc, ProductId, WarehouseId (+index)', @stockHead + N'ORDER BY s.QtyOnHand DESC, s.ProductId, s.WarehouseId' + REPLACE(@stockTail, N'@@ORDER@@', N's0.QtyOnHand DESC, s0.ProductId, s0.WarehouseId')),
 -- R3 movements report: tie-break on DocumentId so the PostedAt index can stream -------------------------------------------------------------
 ('R3 EF: movements unfiltered page', @movHead + N'ORDER BY s1.PostedAt DESC, s.Id' + REPLACE(@movTail, N'@@ORDER@@', N's2.Id')),
 ('R3 RW: movements unfiltered page (+PostedAt, +lines cover)', @movHead + N'ORDER BY s1.PostedAt DESC, s.DocumentId, s.Id' + REPLACE(@movTail, N'@@ORDER@@', N's2.DocumentId, s2.Id')),
 ('R3 EF: movements 30d page', @movHead + N'AND s1.PostedAt >= @s30 AND s1.PostedAt < @now ORDER BY s1.PostedAt DESC, s.Id' + REPLACE(@movTail, N'@@ORDER@@', N's2.Id')),
 ('R3 RW: movements 30d page (+PostedAt, +lines cover)', @movHead + N'AND s1.PostedAt >= @s30 AND s1.PostedAt < @now ORDER BY s1.PostedAt DESC, s.DocumentId, s.Id' + REPLACE(@movTail, N'@@ORDER@@', N's2.DocumentId, s2.Id')),
 ('R3 EF: movements hot product page', @movHead + N'AND s.ProductId = @pid ORDER BY s1.PostedAt DESC, s.Id' + REPLACE(@movTail, N'@@ORDER@@', N's2.Id')),
 ('R3 RW: movements hot product page (+PostedAt, +lines cover)', @movHead + N'AND s.ProductId = @pid ORDER BY s1.PostedAt DESC, s.DocumentId, s.Id' + REPLACE(@movTail, N'@@ORDER@@', N's2.DocumentId, s2.Id')),
 -- R4 document number search: prefix instead of contains ---------------------------------------------------------------------------------------
 ('R4 EF: documents No LIKE %0076%', N'SELECT TOP(@take) s.Id, s.No FROM StockDocument s WHERE s.REC_DELETED = CAST(0 AS bit) AND s.No LIKE @likeNo ESCAPE N''\'' ORDER BY s.Id DESC'),
 ('R4 RW: documents No LIKE RCV-WH02-2610% (prefix)', N'SELECT TOP(@take) s.Id, s.No FROM StockDocument s WHERE s.REC_DELETED = CAST(0 AS bit) AND s.No LIKE @prefixNo ESCAPE N''\'' ORDER BY s.Id DESC');

DECLARE @i int = 1, @n int = (SELECT COUNT(*) FROM #t), @sql nvarchar(max), @label varchar(90), @k int;
DECLARE @pid int = $(Pid);
DECLARE @barcodePid int = (SELECT TOP 1 ProductId FROM ProductBarcode WHERE Barcode = N'9557000000042' AND REC_DELETED = 0);
SET STATISTICS IO ON; SET STATISTICS TIME ON;
WHILE @i <= @n
BEGIN
    SELECT @label = label, @sql = sql FROM #t WHERE id = @i;
    SET @k = 1;
    WHILE @k <= 2
    BEGIN
        PRINT '### ' + @label + ' #' + CAST(@k AS varchar(2));
        EXEC sp_executesql @sql, N'@take int, @like nvarchar(300), @code nvarchar(64), @barcodePid int, @pid int, @s30 datetime2, @now datetime2, @likeNo nvarchar(100), @prefixNo nvarchar(100)',
             @take = 50, @like = N'%9557000000042%', @code = N'9557000000042', @barcodePid = @barcodePid, @pid = @pid, @s30 = @s30, @now = @now,
             @likeNo = N'%0076%', @prefixNo = N'RCV-WH02-2610%';
        SET @k += 1;
    END
    SET @i += 1;
END
SET STATISTICS IO OFF; SET STATISTICS TIME OFF;

DROP INDEX IX_cand_Product_Sku_cover ON dbo.Product;
DROP INDEX IX_cand_StockBalance_QtyOnHand ON dbo.StockBalance;
DROP INDEX IX_cand_StockDocument_PostedAt ON dbo.StockDocument;
DROP INDEX IX_cand_StockDocumentLine_DocumentId_Cover ON dbo.StockDocumentLine;
