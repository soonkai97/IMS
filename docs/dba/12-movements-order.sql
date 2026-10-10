/*
  12-movements-order.sql -- evidence for R3 (movements report: order and indexes). The statements are the ones EF Core generates for
  MovementsPageSpec (page), MovementsSpec (export) and MovementsCountSpec (total): the page once with the OLD order (PostedAt DESC, line Id)
  and once with the NEW code (PostedAt IS NOT NULL, order PostedAt DESC, DocumentId DESC, line Id), against index sets that are created and
  dropped here, so the database is left as it was found. The two indexes of the migration AddMovementsReportIndexes are dropped first and
  re-created at the end if they existed.

    V0  the indexes before R3 (AddHotPathIndexes)
    V1  + IX_StockDocument_PostedAt_Movements   (PostedAt) INCLUDE (Status, Type, WarehouseId) WHERE REC_DELETED = 0 AND PostedAt IS NOT NULL
    V2  V1 + IX_StockDocumentLine_DocumentId_Live   (DocumentId) WHERE REC_DELETED = 0                                  <- shipped
    V3  V1 + a covering lines index (DocumentId, Id) INCLUDE (ProductId, Qty, UomId, BaseQty, UnitCost) WHERE REC_DELETED = 0    <- rejected
    V4  the lines index of V2 without the PostedAt index

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -W -v Hot=3 Mid=161 -i docs/dba/12-movements-order.sql -o movements-order.txt
    awk -f docs/dba/summarize-stats.awk movements-order.txt        # one line per statement: logical reads, CPU, elapsed (the second run)

  Every statement runs twice (compile, then warm); the summary keeps the second. The date windows use the same UTC boundaries as the API
  for "the last 7 / 30 days" in Malaysia time (UTC+8). Elapsed times of one run are noisy on a shared machine; the logical reads are exact.
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() NOT LIKE N'Ims[_]Perf%' THROW 50000, N'Experiment script: a perf database only.', 1;

DECLARE @hadPosted bit = IIF(EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockDocument_PostedAt_Movements'), 1, 0);
DECLARE @hadLive bit = IIF(EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockDocumentLine_DocumentId_Live'), 1, 0);
DROP INDEX IF EXISTS IX_StockDocument_PostedAt_Movements ON dbo.StockDocument;
DROP INDEX IF EXISTS IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine;

DECLARE @today date = CAST(SYSDATETIME() AS date);
DECLARE @end datetime2 = DATEADD(HOUR, 16, CAST(@today AS datetime2));                               -- end of today, Malaysia time, in UTC
DECLARE @s7 datetime2 = DATEADD(HOUR, -8, CAST(DATEADD(DAY, -7, @today) AS datetime2));
DECLARE @s30 datetime2 = DATEADD(HOUR, -8, CAST(DATEADD(DAY, -30, @today) AS datetime2));
DECLARE @rare int = (SELECT TOP 1 ProductId FROM StockDocumentLine WHERE REC_DELETED = 0 GROUP BY ProductId HAVING COUNT(*) = 3 ORDER BY ProductId);

DECLARE @head nvarchar(max) = N'SELECT s2.PostedAt, s2.No, s2.Type, s2.Status, w0.Code, p0.Name, p2.Sku, p2.Name, s2.Qty, u0.Code, s2.BaseQty, s2.UnitCost, s2.BaseQty * s2.UnitCost
FROM (SELECT @@TOP@@ s.Id, s.DocumentId, s.BaseQty, s.ProductId, s.Qty, s.UnitCost, s.UomId, s1.No, s1.PartnerId, s1.PostedAt, s1.Status, s1.Type, s1.WarehouseId
      FROM StockDocumentLine s
      INNER JOIN (SELECT s0.Id, s0.No, s0.PartnerId, s0.PostedAt, s0.Status, s0.Type, s0.WarehouseId FROM StockDocument s0 WHERE s0.REC_DELETED = CAST(0 AS bit)) s1 ON s.DocumentId = s1.Id
      WHERE s.REC_DELETED = CAST(0 AS bit) AND s1.Status IN (3, 4) ';
DECLARE @tail nvarchar(max) = N') s2
INNER JOIN (SELECT w.Id, w.Code FROM Warehouse w WHERE w.REC_DELETED = CAST(0 AS bit)) w0 ON s2.WarehouseId = w0.Id
LEFT JOIN (SELECT p.Id, p.Name FROM Partner p WHERE p.REC_DELETED = CAST(0 AS bit)) p0 ON s2.PartnerId = p0.Id
INNER JOIN (SELECT p1.Id, p1.Name, p1.Sku FROM Product p1 WHERE p1.REC_DELETED = CAST(0 AS bit)) p2 ON s2.ProductId = p2.Id
INNER JOIN (SELECT u.Id, u.Code FROM Uom u WHERE u.REC_DELETED = CAST(0 AS bit)) u0 ON s2.UomId = u0.Id
ORDER BY ';
DECLARE @countHead nvarchar(max) = N'SELECT COUNT(*) FROM StockDocumentLine s
INNER JOIN (SELECT s0.Id, s0.PostedAt, s0.Status, s0.Type, s0.WarehouseId FROM StockDocument s0 WHERE s0.REC_DELETED = CAST(0 AS bit)) s1 ON s.DocumentId = s1.Id
WHERE s.REC_DELETED = CAST(0 AS bit) AND s1.Status IN (3, 4) AND s1.PostedAt IS NOT NULL ';
DECLARE @notNull nvarchar(40) = N'AND s1.PostedAt IS NOT NULL ';
DECLARE @oldOrder nvarchar(max) = N's1.PostedAt DESC, s.Id',                          @oldOuter nvarchar(max) = N's2.PostedAt DESC, s2.Id';
DECLARE @newOrder nvarchar(max) = N's1.PostedAt DESC, s.DocumentId DESC, s.Id',       @newOuter nvarchar(max) = N's2.PostedAt DESC, s2.DocumentId DESC, s2.Id';
DECLARE @offset nvarchar(80) = N' OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY';

CREATE TABLE #w (id int IDENTITY, name varchar(40), filter nvarchar(max), top_ nvarchar(40), page nvarchar(80), countOnly bit);
INSERT #w (name, filter, top_, page, countOnly) VALUES
 ('page: unfiltered p1',         N'',                                                         N'TOP(@take)',  N'',      0),
 ('page: unfiltered p200',       N'',                                                         N'',            @offset,  0),
 ('page: 30 days',               N'AND s1.PostedAt >= @s30 AND s1.PostedAt < @end ',          N'TOP(@take)',  N'',      0),
 ('page: 7 days',                N'AND s1.PostedAt >= @s7 AND s1.PostedAt < @end ',           N'TOP(@take)',  N'',      0),
 ('page: hot product',           N'AND s.ProductId = @hot ',                                  N'TOP(@take)',  N'',      0),
 ('page: mid product',           N'AND s.ProductId = @mid ',                                  N'TOP(@take)',  N'',      0),
 ('page: rare product (3 rows)', N'AND s.ProductId = @rare ',                                 N'TOP(@take)',  N'',      0),
 ('page: warehouse 2 + issues',  N'AND s1.Type = 2 AND s1.WarehouseId = 2 ',                  N'TOP(@take)',  N'',      0),
 ('page: scoped to warehouse 1', N'AND s1.WarehouseId IN (1) ',                               N'TOP(@take)',  N'',      0),
 ('page: export 50,000 rows',    N'',                                                         N'TOP(50000)',  N'',      0),
 ('count: unfiltered',           N'',                                                         N'',            N'',      1),
 ('count: 30 days',              N'AND s1.PostedAt >= @s30 AND s1.PostedAt < @end ',          N'',            N'',      1),
 ('count: 7 days',               N'AND s1.PostedAt >= @s7 AND s1.PostedAt < @end ',           N'',            N'',      1),
 ('count: hot product',          N'AND s.ProductId = @hot ',                                  N'',            N'',      1);

CREATE TABLE #v (id int IDENTITY, name varchar(40), ddl nvarchar(max));
INSERT #v (name, ddl) VALUES
 ('V0 before R3', N''),
 ('V1 PostedAt', N'CREATE INDEX IX_StockDocument_PostedAt_Movements ON dbo.StockDocument (PostedAt) INCLUDE (Status, Type, WarehouseId) WHERE REC_DELETED = 0 AND PostedAt IS NOT NULL'),
 ('V2 PostedAt + Live (shipped)', N'CREATE INDEX IX_StockDocument_PostedAt_Movements ON dbo.StockDocument (PostedAt) INCLUDE (Status, Type, WarehouseId) WHERE REC_DELETED = 0 AND PostedAt IS NOT NULL;
  CREATE INDEX IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine (DocumentId) WHERE REC_DELETED = 0'),
 ('V3 PostedAt + lines cover (rejected)', N'CREATE INDEX IX_StockDocument_PostedAt_Movements ON dbo.StockDocument (PostedAt) INCLUDE (Status, Type, WarehouseId) WHERE REC_DELETED = 0 AND PostedAt IS NOT NULL;
  CREATE INDEX IX_cand_Lines_Cover ON dbo.StockDocumentLine (DocumentId, Id) INCLUDE (ProductId, Qty, UomId, BaseQty, UnitCost) WHERE REC_DELETED = 0'),
 ('V4 Live only', N'CREATE INDEX IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine (DocumentId) WHERE REC_DELETED = 0');

DECLARE @v int = 1, @vn int = (SELECT COUNT(*) FROM #v), @w int, @wn int = (SELECT COUNT(*) FROM #w), @o int, @orders int, @k int;
DECLARE @vname varchar(40), @ddl nvarchar(max), @wname varchar(40), @filter nvarchar(max), @top_ nvarchar(40), @page nvarchar(80), @countOnly bit;
DECLARE @sql nvarchar(max), @order nvarchar(max), @outer nvarchar(max), @pred nvarchar(40), @label varchar(160);

WHILE @v <= @vn
BEGIN
    SELECT @vname = name, @ddl = ddl FROM #v WHERE id = @v;
    IF @ddl <> N'' EXEC (@ddl);
    SET @w = 1;
    WHILE @w <= @wn
    BEGIN
        SELECT @wname = name, @filter = filter, @top_ = top_, @page = page, @countOnly = countOnly FROM #w WHERE id = @w;
        SET @orders = IIF(@countOnly = 1, 1, 2);
        SET @o = 1;
        WHILE @o <= @orders
        BEGIN
            SELECT @order = IIF(@o = 1, @oldOrder, @newOrder), @outer = IIF(@o = 1, @oldOuter, @newOuter), @pred = IIF(@o = 1, N'', @notNull);
            SET @sql = IIF(@countOnly = 1, @countHead + @filter,
                           REPLACE(@head, N'@@TOP@@', @top_) + @pred + @filter + N'ORDER BY ' + @order + @page + @tail + @outer);
            SET @label = CONCAT(@vname, ' | ', @wname, IIF(@countOnly = 1, '', IIF(@o = 1, ' | OLD order', ' | NEW order')));
            SET @k = 1;
            WHILE @k <= 2
            BEGIN
                PRINT '### ' + @label + ' #' + CAST(@k AS varchar(2));
                SET STATISTICS IO ON; SET STATISTICS TIME ON;
                EXEC sp_executesql @sql, N'@take int, @skip int, @s30 datetime2, @s7 datetime2, @end datetime2, @hot int, @mid int, @rare int',
                     @take = 50, @skip = 9950, @s30 = @s30, @s7 = @s7, @end = @end, @hot = $(Hot), @mid = $(Mid), @rare = @rare;
                SET STATISTICS IO OFF; SET STATISTICS TIME OFF;
                SET @k += 1;
            END
            SET @o += 1;
        END
        SET @w += 1;
    END
    DROP INDEX IF EXISTS IX_StockDocument_PostedAt_Movements ON dbo.StockDocument;
    DROP INDEX IF EXISTS IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine;
    DROP INDEX IF EXISTS IX_cand_Lines_Cover ON dbo.StockDocumentLine;
    SET @v += 1;
END

IF @hadPosted = 1 CREATE INDEX IX_StockDocument_PostedAt_Movements ON dbo.StockDocument (PostedAt) INCLUDE (Status, Type, WarehouseId) WHERE REC_DELETED = 0 AND PostedAt IS NOT NULL;
IF @hadLive = 1 CREATE INDEX IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine (DocumentId) WHERE REC_DELETED = 0;
