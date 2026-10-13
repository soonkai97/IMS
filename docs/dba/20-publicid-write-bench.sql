/*
  20-publicid-write-bench.sql -- what the random-GUID unique index UX_StockDocument_PublicId costs on the write path, and how it fragments.

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -b -W -v Rounds=3 N=3000 FragN=100000 -i docs/dba/20-publicid-write-bench.sql

  Part 1 (cost): like 14-write-path-bench.sql, N transactions that each create a Draft document with 5 lines and post it, alternating "without" and "with"
  the index (dropped and re-created with FILLFACTOR 90 between sides), Rounds times each; prints ms per transaction (compare the best of each side).
  The PublicId is the column default NEWID(), random like the Guid.NewGuid() the application makes.
  Part 2 (fragmentation): FragN single-row document inserts (one transaction each, the shape of the application's create) into the table WITH the index, and the
  fragmentation and page density of UX_StockDocument_PublicId, of the clustered key and of IX_StockDocument_No after each 10 % step of FragN.
  Everything it inserted is deleted again and the index is rebuilt at the end (the deleted rows leave the index sparse until the rebuild).
*/
SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;
IF DB_NAME() NOT LIKE N'Ims[_]Perf%' THROW 50000, N'Experiment script: a perf database only.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_StockDocument_PublicId') THROW 50001, N'Run it on the schema with AddPublicIds.', 1;

DECLARE @uom int = (SELECT MIN(Id) FROM Uom WHERE REC_DELETED = 0), @user int = (SELECT MIN(Id) FROM [User]), @wh int = (SELECT MIN(Id) FROM Warehouse WHERE REC_DELETED = 0);
DECLARE @p1 int = (SELECT MIN(Id) FROM Product WHERE REC_DELETED = 0);
CREATE TABLE #r (side varchar(10), round int, ms_per_tx decimal(9, 4));
CREATE TABLE #f (step int, docs_added int, idx sysname, frag_pct decimal(5, 1), density_pct decimal(5, 1), pages bigint);

DECLARE @round int = 1, @side int, @i int, @doc int, @t0 datetime2, @n int = $(N);
WHILE @round <= $(Rounds)
BEGIN
    SET @side = 0;
    WHILE @side <= 1
    BEGIN
        DROP INDEX IF EXISTS UX_StockDocument_PublicId ON dbo.StockDocument;
        IF @side = 1 CREATE UNIQUE INDEX UX_StockDocument_PublicId ON dbo.StockDocument (PublicId) WITH (FILLFACTOR = 90);
        SET @t0 = SYSDATETIME(); SET @i = 1;
        WHILE @i <= @n
        BEGIN
            BEGIN TRAN;
            INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval)
            VALUES (1, CONCAT(N'BENCH-', @round, N'-', @side, N'-', @i), 0, @wh, @user, SYSUTCDATETIME(), 0);
            SET @doc = SCOPE_IDENTITY();
            INSERT StockDocumentLine (DocumentId, ProductId, Qty, UomId, BaseQty, UnitCost)
            SELECT @doc, @p1 + v.value, 1, @uom, 1, 1 FROM GENERATE_SERIES(0, 4) v;
            UPDATE StockDocument SET Status = 3, PostedBy = @user, PostedAt = SYSUTCDATETIME() WHERE Id = @doc;
            UPDATE StockDocumentLine SET UnitCost = 2 WHERE DocumentId = @doc;
            COMMIT;
            SET @i += 1;
        END
        INSERT #r VALUES (IIF(@side = 1, 'with', 'without'), @round, DATEDIFF(MICROSECOND, @t0, SYSDATETIME()) / 1000.0 / @n);
        SET @side += 1;
    END
    SET @round += 1;
END
SELECT side, MIN(ms_per_tx) AS best_ms_per_tx, AVG(ms_per_tx) AS avg_ms_per_tx, COUNT(*) AS rounds FROM #r GROUP BY side;

-- part 2: fragmentation under single-row random inserts
DROP INDEX IF EXISTS UX_StockDocument_PublicId ON dbo.StockDocument;
CREATE UNIQUE INDEX UX_StockDocument_PublicId ON dbo.StockDocument (PublicId) WITH (FILLFACTOR = 90);
DECLARE @fragN int = $(FragN), @step int = 0, @k int = 0, @stepSize int = $(FragN) / 10;
SET @t0 = SYSDATETIME();
WHILE @step <= 10
BEGIN
    INSERT #f
    SELECT @step * 10, @k, i.name, ps.avg_fragmentation_in_percent, ps.avg_page_space_used_in_percent, ps.page_count
    FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID(N'dbo.StockDocument'), NULL, NULL, 'SAMPLED') ps
    JOIN sys.indexes i ON i.object_id = ps.object_id AND i.index_id = ps.index_id
    WHERE i.name IN (N'UX_StockDocument_PublicId', N'PK_StockDocument', N'IX_StockDocument_No') AND ps.alloc_unit_type_desc = 'IN_ROW_DATA';
    IF @step = 10 BREAK;
    SET @i = 1;
    WHILE @i <= @stepSize
    BEGIN
        SET @k += 1;
        INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval)
        VALUES (1, CONCAT(N'BENCH-F-', @k), 0, @wh, @user, SYSUTCDATETIME(), 0);
        SET @i += 1;
    END
    SET @step += 1;
END
PRINT CONCAT(@k, ' single-row inserts with the index in ', DATEDIFF(SECOND, @t0, SYSDATETIME()), ' s');
SELECT * FROM #f ORDER BY idx, step;

-- clean-up: the bench documents go (their lines with them: cascade) and the index is rebuilt dense again
DELETE FROM StockDocument WHERE No LIKE N'BENCH-%';
ALTER INDEX UX_StockDocument_PublicId ON dbo.StockDocument REBUILD WITH (FILLFACTOR = 90);
