/*
  09-rewrite-candidates.sql -- evidence for the query rewrites in docs/dba/README.md (section "Rewrites"). Each pair runs the statement
  exactly as EF Core generates it today ("EF") and the rewritten statement ("RW"), twice (compile, then warm; the second run counts),
  with SET STATISTICS IO/TIME. The extra indexes the rewrites need are created here as IX_cand_* and dropped at the end, so the
  database is left as it was found.

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -W -i docs/dba/09-rewrite-candidates.sql -o rewrites.txt
    awk -f docs/dba/summarize-stats.awk rewrites.txt          # one line per statement: logical reads, CPU, elapsed

  R1 (barcode resolved up front) and R3 (movements report order, see 12-movements-order.sql) are IMPLEMENTED. R2 and R4 change the order of
  rows that tie on the sort key or the matching rule, so they are recommendations that need an owner's decision, not applied code.
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() NOT LIKE N'Ims[_]Perf%' THROW 50000, N'Experiment script: a perf database only.', 1;

-- candidate indexes (dropped again at the end) -------------------------------------------------------------------------------------------
CREATE UNIQUE INDEX IX_cand_Product_Sku_cover ON dbo.Product (Sku) INCLUDE (Name, BaseUomId, CategoryId, SafetyStock) WHERE REC_DELETED = 0;
CREATE INDEX IX_cand_StockBalance_QtyOnHand ON dbo.StockBalance (QtyOnHand DESC) INCLUDE (AvgCost, QtyInTransit) WHERE REC_DELETED = 0;


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
 -- R4 document number search: prefix instead of contains ---------------------------------------------------------------------------------------
 ('R4 EF: documents No LIKE %0076%', N'SELECT TOP(@take) s.Id, s.No FROM StockDocument s WHERE s.REC_DELETED = CAST(0 AS bit) AND s.No LIKE @likeNo ESCAPE N''\'' ORDER BY s.Id DESC'),
 ('R4 RW: documents No LIKE RCV-WH02-2610% (prefix)', N'SELECT TOP(@take) s.Id, s.No FROM StockDocument s WHERE s.REC_DELETED = CAST(0 AS bit) AND s.No LIKE @prefixNo ESCAPE N''\'' ORDER BY s.Id DESC');

DECLARE @i int = 1, @n int = (SELECT COUNT(*) FROM #t), @sql nvarchar(max), @label varchar(90), @k int;
DECLARE @barcodePid int = (SELECT TOP 1 ProductId FROM ProductBarcode WHERE Barcode = N'9557000000042' AND REC_DELETED = 0);
SET STATISTICS IO ON; SET STATISTICS TIME ON;
WHILE @i <= @n
BEGIN
    SELECT @label = label, @sql = sql FROM #t WHERE id = @i;
    SET @k = 1;
    WHILE @k <= 2
    BEGIN
        PRINT '### ' + @label + ' #' + CAST(@k AS varchar(2));
        EXEC sp_executesql @sql, N'@take int, @like nvarchar(300), @code nvarchar(64), @barcodePid int, @likeNo nvarchar(100), @prefixNo nvarchar(100)',
             @take = 50, @like = N'%9557000000042%', @code = N'9557000000042', @barcodePid = @barcodePid,
             @likeNo = N'%0076%', @prefixNo = N'RCV-WH02-2610%';
        SET @k += 1;
    END
    SET @i += 1;
END
SET STATISTICS IO OFF; SET STATISTICS TIME OFF;

DROP INDEX IX_cand_Product_Sku_cover ON dbo.Product;
DROP INDEX IX_cand_StockBalance_QtyOnHand ON dbo.StockBalance;
