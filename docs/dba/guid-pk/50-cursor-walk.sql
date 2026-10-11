/*
  50-cursor-walk.sql -- cursor (seek) paging of the ledger by one product, page size 50, walked from the newest row to the last:
    walk "id"       ORDER BY Id DESC,              next page = Id < last Id                          (the product's afterId contract)
    walk "time_id"  ORDER BY OccurredAt DESC, Id DESC, next page = (OccurredAt, Id) < (last, last)   (needs IX_StockLedgerEntry_ProductId_At_Id, built here and timed)
  Each walk is checked for correctness (rows returned vs rows that exist, duplicates, missing) and for meaning (chronological inversions: adjacent
  rows of the walk where the older row comes first), and costed (pages, logical reads, elapsed). A second run of both walks inserts 3 new ledger
  rows for the product (OccurredAt in the future, Reason 100+) between every two pages and counts how many of them the walk then returns: for a
  newest-first list they must not appear in the middle of an older page.
  WRITES to the database (the extra index, the Reason 100+ rows): run it on a copy only. Variables: Tag.
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
IF DB_NAME() NOT LIKE N'Ims[_]PerfGuid%' THROW 50000, N'Scratch databases only.', 1;
IF OBJECT_ID(N'dbo._CursorWalk') IS NULL
    CREATE TABLE dbo._CursorWalk (Id int IDENTITY PRIMARY KEY, Tag varchar(60) NOT NULL, Walk varchar(20) NOT NULL, WithInserts bit NOT NULL, ProductN int NOT NULL, Pages int NOT NULL,
        RowsReturned int NOT NULL, RowsExpected int NOT NULL, Duplicates int NOT NULL, Missing int NOT NULL, Inversions int NOT NULL, BigInversions int NOT NULL, LogicalReads bigint NOT NULL, ElapsedMs bigint NOT NULL,
        LateRowsInserted int NOT NULL, LateRowsSeen int NOT NULL);
IF OBJECT_ID(N'dbo._ExtraIndex') IS NULL
    CREATE TABLE dbo._ExtraIndex (Id int IDENTITY PRIMARY KEY, Tag varchar(60) NOT NULL, Idx sysname NOT NULL, BuildMs bigint NOT NULL, UsedMb decimal(12,2) NOT NULL, Rows bigint NOT NULL);

DECLARE @t0 datetime2 = SYSDATETIME();
IF INDEXPROPERTY(OBJECT_ID(N'dbo.StockLedgerEntry'), N'IX_StockLedgerEntry_ProductId_At_Id', 'IndexId') IS NULL
    CREATE INDEX IX_StockLedgerEntry_ProductId_At_Id ON dbo.StockLedgerEntry (ProductId, OccurredAt DESC, Id DESC);
INSERT dbo._ExtraIndex (Tag, Idx, BuildMs, UsedMb, Rows)
SELECT '$(Tag)', N'IX_StockLedgerEntry_ProductId_At_Id', DATEDIFF_BIG(MILLISECOND, @t0, SYSDATETIME()), SUM(ps.used_page_count) * 8 / 1024.0, SUM(ps.row_count)
FROM sys.dm_db_partition_stats ps WHERE ps.object_id = OBJECT_ID(N'dbo.StockLedgerEntry') AND ps.index_id = INDEXPROPERTY(OBJECT_ID(N'dbo.StockLedgerEntry'), N'IX_StockLedgerEntry_ProductId_At_Id', 'IndexId');

CREATE TABLE #page (seq int IDENTITY PRIMARY KEY, id nvarchar(50), at datetime2(7), reason int);
CREATE TABLE #seen (seq int IDENTITY PRIMARY KEY, id nvarchar(50), at datetime2(7), reason int);

DECLARE @walk varchar(20), @ins bit, @pn int, @pid nvarchar(50), @sql nvarchar(max), @afterId nvarchar(50), @afterAt nvarchar(40), @pages int, @expected int, @late int, @lateSeen int;
DECLARE @wn int = 0, @r0 bigint, @r1 bigint, @e0 datetime2(7), @elapsed bigint, @reads bigint, @n int;

DECLARE w CURSOR LOCAL FAST_FORWARD FOR
    SELECT v.walk, v.ins, p.n FROM (VALUES ('id', 0), ('time_id', 0), ('id', 1), ('time_id', 1)) v (walk, ins) CROSS JOIN (VALUES (1), (300), ((SELECT COUNT(*) FROM dbo._Pick_Product) * 8 / 50)) p (n) ORDER BY v.ins, p.n DESC, v.walk;
OPEN w; FETCH NEXT FROM w INTO @walk, @ins, @pn;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @wn += 1;
    SELECT @pid = CONVERT(nvarchar(50), ProductId) FROM dbo._Pick_Product WHERE n = @pn;
    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.StockLedgerEntry WHERE ProductId = ''' + @pid + N''' AND Reason < 99';
    EXEC sp_executesql @sql, N'@n int OUTPUT', @n = @expected OUTPUT;
    TRUNCATE TABLE #seen;
    SET @afterId = NULL; SET @afterAt = NULL; SET @pages = 0; SET @reads = 0; SET @elapsed = 0; SET @late = 0;
    WHILE 1 = 1
    BEGIN
        SET @sql = N'SELECT TOP (50) CONVERT(nvarchar(50), e.Id), e.OccurredAt, e.Reason FROM dbo.StockLedgerEntry e WHERE e.ProductId = ''' + @pid + N''''
                 + CASE WHEN @afterId IS NULL THEN N''
                        WHEN @walk = 'id' THEN N' AND e.Id < ''' + @afterId + N''''
                        ELSE N' AND (e.OccurredAt < ''' + @afterAt + N''' OR (e.OccurredAt = ''' + @afterAt + N''' AND e.Id < ''' + @afterId + N'''))' END
                 + CASE WHEN @walk = 'id' THEN N' ORDER BY e.Id DESC' ELSE N' ORDER BY e.OccurredAt DESC, e.Id DESC' END;
        TRUNCATE TABLE #page;
        SELECT @r0 = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
        SET @e0 = SYSDATETIME();
        INSERT #page (id, at, reason) EXEC (@sql);
        SET @elapsed += DATEDIFF_BIG(MICROSECOND, @e0, SYSDATETIME());
        SELECT @r1 = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
        SET @reads += @r1 - @r0;
        IF NOT EXISTS (SELECT 1 FROM #page) BREAK;
        SET @pages += 1;
        INSERT #seen (id, at, reason) SELECT id, at, reason FROM #page ORDER BY seq;
        SELECT TOP (1) @afterId = id, @afterAt = CONVERT(nvarchar(40), at, 126) FROM #page ORDER BY seq DESC;
        IF @ins = 1
        BEGIN   -- three new ledger rows for the product, newest of all (OccurredAt in 2030), between two pages
            SET @sql = N'INSERT dbo.StockLedgerEntry (ProductId, WarehouseId, QtyDelta, UnitCost, SourceDocId, Reason, OccurredAt, CreatedBy)
                         SELECT TOP (3) ProductId, WarehouseId, 1, 1, SourceDocId, ' + CAST(100 + @wn AS nvarchar(10)) + N', ''2030-01-01'', CreatedBy FROM dbo.StockLedgerEntry WHERE ProductId = ''' + @pid + N''' AND Reason < 99';
            EXEC (@sql); SET @late += 3;
        END
    END
    SELECT @lateSeen = COUNT(*) FROM #seen WHERE reason = 100 + @wn;
    INSERT dbo._CursorWalk (Tag, Walk, WithInserts, ProductN, Pages, RowsReturned, RowsExpected, Duplicates, Missing, Inversions, BigInversions, LogicalReads, ElapsedMs, LateRowsInserted, LateRowsSeen)
    SELECT '$(Tag)', @walk, @ins, @pn, @pages, COUNT(*), @expected, COUNT(*) - COUNT(DISTINCT id),
           @expected - (SELECT COUNT(DISTINCT id) FROM #seen WHERE reason < 99),
           (SELECT COUNT(*) FROM (SELECT at, LAG(at) OVER (ORDER BY seq) AS prev FROM #seen) x WHERE x.prev < x.at),
           (SELECT COUNT(*) FROM (SELECT at, LAG(at) OVER (ORDER BY seq) AS prev FROM #seen) x WHERE DATEADD(DAY, 1, x.prev) < x.at),
           @reads, @elapsed / 1000, @late, @lateSeen
    FROM #seen;
    FETCH NEXT FROM w INTO @walk, @ins, @pn;
END
CLOSE w; DEALLOCATE w;
SELECT * FROM dbo._CursorWalk WHERE Tag = '$(Tag)' ORDER BY Id;


