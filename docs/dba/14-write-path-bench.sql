/*
  14-write-path-bench.sql -- what an index costs on the write path: N transactions that each create a document with 5 lines (Draft) and post it
  (status, PostedAt, line costs updated), the shape of a real flow minus the ledger and balance (those tables are not touched by the indexes
  under test). Alternates "without" and "with" the two R3 indexes on the same database, @Rounds times each, and prints milliseconds per
  transaction; compare the minimum of each side, a shared machine makes single rounds noisy. The rows it creates are deleted again.

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -b -W -v Rounds=3 N=3000 -i docs/dba/14-write-path-bench.sql
*/
SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;
IF DB_NAME() NOT LIKE N'Ims[_]Perf%' THROW 50000, N'Experiment script: a perf database only.', 1;

DECLARE @hadPosted bit = IIF(EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockDocument_PostedAt_Movements'), 1, 0);
DECLARE @hadLive bit = IIF(EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockDocumentLine_DocumentId_Live'), 1, 0);
DECLARE @uom int = (SELECT MIN(Id) FROM Uom WHERE REC_DELETED = 0), @user int = (SELECT MIN(Id) FROM [User]), @wh int = (SELECT MIN(Id) FROM Warehouse WHERE REC_DELETED = 0);
DECLARE @p1 int = (SELECT MIN(Id) FROM Product WHERE REC_DELETED = 0);
CREATE TABLE #r (side varchar(10), round int, ms_per_tx decimal(9, 4));

DECLARE @round int = 1, @side int, @i int, @doc int, @t0 datetime2, @n int = $(N);
WHILE @round <= $(Rounds)
BEGIN
    SET @side = 0;
    WHILE @side <= 1
    BEGIN
        DROP INDEX IF EXISTS IX_StockDocument_PostedAt_Movements ON dbo.StockDocument;
        DROP INDEX IF EXISTS IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine;
        IF @side = 1
        BEGIN
            CREATE INDEX IX_StockDocument_PostedAt_Movements ON dbo.StockDocument (PostedAt) INCLUDE (Status, Type, WarehouseId) WHERE REC_DELETED = 0 AND PostedAt IS NOT NULL;
            CREATE INDEX IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine (DocumentId) WHERE REC_DELETED = 0;
        END
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

DELETE FROM StockDocument WHERE No LIKE N'BENCH-%';   -- lines go with their documents (cascade)
DROP INDEX IF EXISTS IX_StockDocument_PostedAt_Movements ON dbo.StockDocument;
DROP INDEX IF EXISTS IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine;
IF @hadPosted = 1 CREATE INDEX IX_StockDocument_PostedAt_Movements ON dbo.StockDocument (PostedAt) INCLUDE (Status, Type, WarehouseId) WHERE REC_DELETED = 0 AND PostedAt IS NOT NULL;
IF @hadLive = 1 CREATE INDEX IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine (DocumentId) WHERE REC_DELETED = 0;
