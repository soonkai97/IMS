/*
  17-movements-by-product.sql -- evidence for the movements report filtered by product (parameter sniffing). The page statement is the one EF Core
  generates for GET /api/reports/movements?productId=<id> (page 1, size 50: postings branch UNION ALL reversals branch, merged and cut). It is run
  through sp_executesql exactly as EF sends it, so one cached plan serves every product, as in production.

  Why it matters: for a product with MANY lines the cheapest plan walks the documents by PostedAt (newest first) and stops after 50 matches (a few
  thousand reads); for a product with FEW or NO lines the same plan walks every document looking for matches (millions of reads). SQL Server compiles
  the statement once, for the first product that arrives, and reuses that plan for every other product.

  Variants:  V0  the statement as it was (shared plan)
             V1  the statement with OPTION (RECOMPILE), the shipped fix (each product gets its own plan; the compile is part of the time)
  Scenarios: fresh       the plan cache is cleared before every product: what each product costs with a plan of its own
             hot-first   cleared once, the hottest product compiles the plan, every other product reuses it (V0 only differs from fresh)
             cold-first  cleared once, a product with 3 lines compiles the plan, every other product (the hottest too) reuses it

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf_Mov -W -i docs/dba/17-movements-by-product.sql -o movements-by-product.txt
    awk -f docs/dba/summarize-stats.awk movements-by-product.txt        # one line per statement: logical reads, CPU, elapsed (compile included)

  The repo seed (01-seed-perf.sql) is Zipf-skewed with the hottest product at 0.5 % of the lines; there the shared plan for the hottest product is a
  hash join and every other product costs at most ~4,000 reads. The plan that walks the documents is chosen when the hottest product has a few
  percent of the lines, as in the review that found the problem (PR 70, M3). To reproduce that shape on a throw-away perf database first run, once:
      UPDATE TOP (50000) StockDocumentLine SET ProductId = 3 WHERE ProductId <> 3 AND Id % 20 = 0; UPDATE STATISTICS StockDocumentLine WITH FULLSCAN;
  The products measured are picked by their number of lines: the hottest, then ~10,000-line, ~3,000-line, ~100, ~50, ~10, ~3, 1 and 0 lines.
  The plan cache is cleared per database (ALTER DATABASE SCOPED CONFIGURATION), so the script refuses anything but a perf database.
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() NOT LIKE N'Ims[_]Perf%' THROW 50000, N'Experiment script: a perf database only.', 1;

DECLARE @q0 nvarchar(max) = N'
SELECT [u3].[At], [u3].[DocumentId], [u3].[LineId], [u3].[DocNo], [u3].[Type], [u3].[Status], [u3].[WarehouseCode], [u3].[Partner], [u3].[Sku], [u3].[ProductName], [u3].[Qty], [u3].[UomCode], [u3].[BaseQty], [u3].[StockEffect], [u3].[UnitCost], [u3].[Amount], [u3].[IsReversal]
FROM (
    SELECT [s2].[PostedAt] AS [At], [s2].[DocumentId], [s2].[Id] AS [LineId], [s2].[No] AS [DocNo], [s2].[Type], [s2].[Status], [w0].[Code] AS [WarehouseCode], [p0].[Name] AS [Partner], [p2].[Sku], [p2].[Name] AS [ProductName], [s2].[Qty], [u0].[Code] AS [UomCode], [s2].[BaseQty], CASE
        WHEN [s2].[Type] = 1 THEN [s2].[BaseQty]
        ELSE -[s2].[BaseQty]
    END AS [StockEffect], ROUND([s2].[UnitCost], 4) AS [UnitCost], [s2].[Amount], CAST(0 AS bit) AS [IsReversal]
    FROM (
        SELECT TOP(@p1) [s].[Id], [s].[Amount], [s].[BaseQty], [s].[DocumentId], [s].[ProductId], [s].[Qty], [s].[UnitCost], [s].[UomId], [s1].[No], [s1].[PartnerId], [s1].[PostedAt], [s1].[Status], [s1].[Type], [s1].[WarehouseId]
        FROM [StockDocumentLine] AS [s]
        INNER JOIN (
            SELECT [s0].[Id], [s0].[No], [s0].[PartnerId], [s0].[PostedAt], [s0].[Status], [s0].[Type], [s0].[WarehouseId]
            FROM [StockDocument] AS [s0]
            WHERE [s0].[REC_DELETED] = CAST(0 AS bit)
        ) AS [s1] ON [s].[DocumentId] = [s1].[Id]
        WHERE [s].[REC_DELETED] = CAST(0 AS bit) AND [s1].[Status] IN (3, 4) AND [s1].[PostedAt] IS NOT NULL AND [s].[ProductId] = @p
        ORDER BY [s1].[PostedAt] DESC, [s].[DocumentId] DESC, [s].[Id]
    ) AS [s2]
    INNER JOIN (
        SELECT [w].[Id], [w].[Code]
        FROM [Warehouse] AS [w]
        WHERE [w].[REC_DELETED] = CAST(0 AS bit)
    ) AS [w0] ON [s2].[WarehouseId] = [w0].[Id]
    LEFT JOIN (
        SELECT [p].[Id], [p].[Name]
        FROM [Partner] AS [p]
        WHERE [p].[REC_DELETED] = CAST(0 AS bit)
    ) AS [p0] ON [s2].[PartnerId] = [p0].[Id]
    INNER JOIN (
        SELECT [p1].[Id], [p1].[Name], [p1].[Sku]
        FROM [Product] AS [p1]
        WHERE [p1].[REC_DELETED] = CAST(0 AS bit)
    ) AS [p2] ON [s2].[ProductId] = [p2].[Id]
    INNER JOIN (
        SELECT [u].[Id], [u].[Code]
        FROM [Uom] AS [u]
        WHERE [u].[REC_DELETED] = CAST(0 AS bit)
    ) AS [u0] ON [s2].[UomId] = [u0].[Id]
    UNION ALL
    SELECT [s6].[ReversedAt] AS [At], [s6].[DocumentId], [s6].[Id] AS [LineId], [s6].[No] AS [DocNo], [s6].[Type], [s6].[Status], [w2].[Code] AS [WarehouseCode], [p4].[Name] AS [Partner], [p6].[Sku], [p6].[Name] AS [ProductName], [s6].[Qty], [u2].[Code] AS [UomCode], [s6].[BaseQty], CASE
        WHEN [s6].[Type] = 1 THEN -[s6].[BaseQty]
        ELSE [s6].[BaseQty]
    END AS [StockEffect], ROUND([s6].[UnitCost], 4) AS [UnitCost], [s6].[Amount], CAST(1 AS bit) AS [IsReversal]
    FROM (
        SELECT TOP(@p1) [s3].[Id], [s3].[Amount], [s3].[BaseQty], [s3].[DocumentId], [s3].[ProductId], [s3].[Qty], [s3].[UnitCost], [s3].[UomId], [s5].[No], [s5].[PartnerId], [s5].[ReversedAt], [s5].[Status], [s5].[Type], [s5].[WarehouseId]
        FROM [StockDocumentLine] AS [s3]
        INNER JOIN (
            SELECT [s4].[Id], [s4].[No], [s4].[PartnerId], [s4].[ReversedAt], [s4].[Status], [s4].[Type], [s4].[WarehouseId]
            FROM [StockDocument] AS [s4]
            WHERE [s4].[REC_DELETED] = CAST(0 AS bit)
        ) AS [s5] ON [s3].[DocumentId] = [s5].[Id]
        WHERE [s3].[REC_DELETED] = CAST(0 AS bit) AND [s5].[Status] = 4 AND [s5].[ReversedAt] IS NOT NULL AND [s3].[ProductId] = @p2
        ORDER BY [s5].[ReversedAt] DESC, [s3].[DocumentId] DESC, [s3].[Id]
    ) AS [s6]
    INNER JOIN (
        SELECT [w1].[Id], [w1].[Code]
        FROM [Warehouse] AS [w1]
        WHERE [w1].[REC_DELETED] = CAST(0 AS bit)
    ) AS [w2] ON [s6].[WarehouseId] = [w2].[Id]
    LEFT JOIN (
        SELECT [p3].[Id], [p3].[Name]
        FROM [Partner] AS [p3]
        WHERE [p3].[REC_DELETED] = CAST(0 AS bit)
    ) AS [p4] ON [s6].[PartnerId] = [p4].[Id]
    INNER JOIN (
        SELECT [p5].[Id], [p5].[Name], [p5].[Sku]
        FROM [Product] AS [p5]
        WHERE [p5].[REC_DELETED] = CAST(0 AS bit)
    ) AS [p6] ON [s6].[ProductId] = [p6].[Id]
    INNER JOIN (
        SELECT [u1].[Id], [u1].[Code]
        FROM [Uom] AS [u1]
        WHERE [u1].[REC_DELETED] = CAST(0 AS bit)
    ) AS [u2] ON [s6].[UomId] = [u2].[Id]
) AS [u3]
ORDER BY [u3].[At] DESC, [u3].[DocumentId] DESC, [u3].[LineId], [u3].[IsReversal] DESC
OFFSET @p4 ROWS FETCH NEXT @p1 ROWS ONLY';
DECLARE @q1 nvarchar(max) = @q0 + N'
OPTION (RECOMPILE)';

-- the products: the nearest product to each target number of lines (the hottest first; 0 lines = a product that no line mentions)
CREATE TABLE #c (pid int PRIMARY KEY, c int);
INSERT #c SELECT ProductId, COUNT(*) FROM StockDocumentLine WHERE REC_DELETED = 0 GROUP BY ProductId;
CREATE TABLE #t (k int IDENTITY, target int);
INSERT #t (target) VALUES (1000000), (10000), (3000), (100), (50), (10), (3), (1), (0);
CREATE TABLE #p (k int, pid int, c int, name varchar(30));
INSERT #p SELECT t.k, x.pid, x.c, CONCAT(x.c, ' lines')
FROM #t t CROSS APPLY (SELECT TOP 1 pid, c FROM (SELECT pid, c FROM #c UNION ALL SELECT TOP 1 p.Id, 0 FROM Product p WHERE NOT EXISTS (SELECT 1 FROM #c WHERE pid = p.Id)) y
                        ORDER BY ABS(y.c - t.target), y.pid) x;
SELECT k, pid, c FROM #p ORDER BY k;

DECLARE @v int = 0, @sc int, @k int, @n int = (SELECT COUNT(*) FROM #p), @pid int, @name varchar(30), @sname varchar(12), @rep int, @cold int = (SELECT pid FROM #p WHERE c BETWEEN 3 AND 3), @q nvarchar(max);
SET @cold = ISNULL(@cold, (SELECT TOP 1 pid FROM #p WHERE c BETWEEN 1 AND 20 ORDER BY c DESC));
WHILE @v <= 1
BEGIN
    SET @q = IIF(@v = 0, @q0, @q1);
    SET @sc = 1;
    WHILE @sc <= 3
    BEGIN
        SET @sname = CHOOSE(@sc, 'fresh', 'hot-first', 'cold-first');
        ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
        IF @sc = 3   -- compile the shared plan with a product that has a few lines
            EXEC sp_executesql @q, N'@p1 int,@p int,@p2 int,@p4 int', @p1 = 50, @p = @cold, @p2 = @cold, @p4 = 0;
        SET @k = 1;
        WHILE @k <= @n
        BEGIN
            SELECT @pid = pid, @name = name FROM #p WHERE k = @k;
            IF @sc = 1 ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
            SET @rep = IIF(@sc = 1, 2, 1);
            WHILE @rep > 0
            BEGIN
                PRINT '### V' + CAST(@v AS varchar(1)) + ' ' + @sname + ' | ' + @name + ' #' + CAST(@rep AS varchar(2));
                SET STATISTICS IO ON; SET STATISTICS TIME ON;
                EXEC sp_executesql @q, N'@p1 int,@p int,@p2 int,@p4 int', @p1 = 50, @p = @pid, @p2 = @pid, @p4 = 0;
                SET STATISTICS IO OFF; SET STATISTICS TIME OFF;
                SET @rep -= 1;
            END
            SET @k += 1;
        END
        SET @sc += 1;
    END
    SET @v += 1;
END

