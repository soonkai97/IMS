/*
  20-posting-proc.sql -- the posting-like write path used by every measurement (organic load, throughput windows, buffer-pool workload).
  Extends the pattern of docs/dba/14-write-path-bench.sql to the whole flow. One transaction = one document:
    1 StockDocument insert (Draft), 2-10 lines (distinct products, Zipf-skewed like 01-seed-perf.sql), then
      94.9 % Posted: document update (status, user, time columns: touches the status/date/user indexes), line cost update, one ledger row per line,
                     StockBalance update per line (the rows exist from the seed, updated in key order as the last statement), two AuditLogs rows;
       5 %   Cancelled: document update and two audit rows, no stock effect;
       0.1 % left Draft: one audit row.
  The generated keys are the variant's (identity, NEWSEQUENTIALID, NEWID or the v7-like default).
  Not included: the application locks and the document-number sequence row (same for every strategy, they only add waiting).
  Deadlock victims and key races are retried (counted); the content is deterministic from (writer, iteration, ordinal base), so every strategy
  stores the same logical data. Event time is simulated: ordinal * spacing seconds after @epoch, so dates spread over the history like the seed.
  Usage: EXEC dbo._RunWriter @tag, @w, @n, @stride, @ordBase, @epoch, @spacing, @products
*/
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
IF DB_NAME() NOT LIKE N'Ims[_]PerfGuid%' THROW 50000, N'Scratch databases only.', 1;
GO
CREATE OR ALTER FUNCTION dbo._Hash (@a bigint, @b int) RETURNS int WITH SCHEMABINDING AS
BEGIN
    RETURN CAST(SUBSTRING(HASHBYTES('MD5', CAST(@a AS binary(8)) + CAST(@b AS binary(4))), 1, 4) AS int) & 2147483647;   -- deterministic, well mixed, never negative
END
GO
CREATE OR ALTER PROCEDURE dbo._RunWriter @tag varchar(60), @w int, @n int, @stride int, @ordBase bigint, @epoch datetime2(7), @spacing float, @products int
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;
    DECLARE @i int = 1, @retries int = 0, @start datetime2 = SYSUTCDATETIME(), @fate int;
    DECLARE @ord bigint, @typ int, @lc int, @whn int, @t datetime2(7), @no nvarchar(40);
    DECLARE @wh $(KT), @partner $(KT), @creator $(KT), @poster $(KT), @doc $(KT), @creatorName nvarchar(100), @posterName nvarchar(100), @cn int, @pn int, @part int;
    DECLARE @d TABLE (Id $(KT) NOT NULL);
    DECLARE @L TABLE (pn int NOT NULL, pid $(KT) NOT NULL PRIMARY KEY, uom $(KT) NOT NULL, qty decimal(18,4) NOT NULL, cost decimal(18,6) NOT NULL, signed decimal(18,4) NOT NULL);

    WHILE @i <= @n
    BEGIN
        SET @ord = @ordBase + CAST(@i AS bigint) * @stride + @w;
        SET @fate = CASE WHEN @ord % 1000 = 0 THEN 0 WHEN @ord % 20 = 1 THEN 5 ELSE 3 END;
        SET @typ = CASE WHEN dbo._Hash(@ord, 3) % 100 < 40 THEN 1 ELSE 2 END;
        SET @lc = 2 + dbo._Hash(@ord, 4) % 9;
        SET @whn = 1 + CAST(FLOOR(5 * POWER((dbo._Hash(@ord, 5) % 1000000) / 1000000.0, 1.5)) AS int);
        SET @t = DATEADD(SECOND, CAST(@ord * @spacing AS bigint) % 2000000000, @epoch);
        SET @no = CONCAT(IIF(@typ = 1, N'RCV', N'ISS'), N'-WH0', @whn, N'-', RIGHT(CONCAT(N'00000000', @ord), 8));
        SET @cn = CHOOSE(1 + dbo._Hash(@ord, 6) % 5, 6, 7, 8, 3, 4);
        SET @pn = CHOOSE(1 + dbo._Hash(@ord, 7) % 2, 9, 10);
        SET @part = CASE WHEN @typ = 1 THEN 1 + dbo._Hash(@ord, 8) % 40 WHEN dbo._Hash(@ord, 9) % 10 < 7 THEN 41 + dbo._Hash(@ord, 8) % 80 END;

        BEGIN TRY
            DELETE @d; DELETE @L;
            SELECT @wh = WarehouseId FROM dbo._Pick_Wh WHERE n = @whn;
            SELECT @creator = UserId, @creatorName = Username FROM dbo._Pick_User WHERE n = @cn;
            SELECT @poster = UserId, @posterName = Username FROM dbo._Pick_User WHERE n = @pn;
            SET @partner = NULL; SELECT @partner = PartnerId FROM dbo._Pick_Partner WHERE n = @part;

            INSERT @L (pn, pid, uom, qty, cost, signed)
            SELECT g.pn, pp.ProductId, pp.UomId, g.qty, g.cost, CASE WHEN @typ = 1 THEN g.qty ELSE -g.qty END
            FROM (SELECT x.pn, MIN(x.qty) AS qty, MIN(x.cost) AS cost
                  FROM (SELECT 1 + CAST(FLOOR(@products * POWER((dbo._Hash(@ord, j.value * 10 + 1) % 1000000) / 1000000.0, 2)) AS int) AS pn,
                               CAST(1 + dbo._Hash(@ord, j.value * 10 + 2) % 50 AS decimal(18,4)) AS qty,
                               CAST(1 + (dbo._Hash(@ord, j.value * 10 + 3) % 20000) / 100.0 AS decimal(18,6)) AS cost
                        FROM GENERATE_SERIES(1, @lc) j) x GROUP BY x.pn) g
            JOIN dbo._Pick_Product pp ON pp.n = g.pn;

            BEGIN TRAN;
            INSERT dbo.StockDocument (Type, No, Status, WarehouseId, PartnerId, Purpose, Remark, CreatedBy, CreatedAt, RequiresApproval)
            OUTPUT inserted.Id INTO @d (Id)
            VALUES (@typ, @no, 0, @wh, @partner, IIF(@typ = 2, 1, NULL), NULL, @creator, @t, 0);
            SELECT @doc = Id FROM @d;

            INSERT dbo.StockDocumentLine (DocumentId, ProductId, Qty, UomId, BaseQty, UnitCost)
            SELECT @doc, pid, qty, uom, qty, cost FROM @L;

            IF @fate = 3
            BEGIN
                UPDATE dbo.StockDocument SET Status = 3, SubmittedBy = @creator, SubmittedAt = @t, PostedBy = @poster, PostedAt = @t, REC_REVDATE = SYSUTCDATETIME() WHERE Id = @doc;
                UPDATE l SET UnitCost = l.UnitCost + 0.01, REC_REVDATE = SYSUTCDATETIME() FROM dbo.StockDocumentLine AS l WITH (INDEX (IX_StockDocumentLine_DocumentId)) WHERE l.DocumentId = @doc;   -- seek on the foreign-key index (a plan compiled on an empty table scans and deadlocks)

                INSERT dbo.StockLedgerEntry (ProductId, WarehouseId, QtyDelta, UnitCost, SourceDocId, Reason, OccurredAt, CreatedBy)
                SELECT pid, @wh, signed, cost, @doc, @typ, @t, @poster FROM @L;

            END
            ELSE IF @fate = 5
                UPDATE dbo.StockDocument SET Status = 5, CancelReason = N'Customer changed the order', REC_REVDATE = SYSUTCDATETIME() WHERE Id = @doc;

            INSERT dbo.AuditLogs (Actor, Action, EntityType, EntityId, [Before], [After], [At])
            VALUES (@creatorName, N'DocumentCreated', N'StockDocument', @no, NULL, N'{"Status":0}', @t);
            IF @fate <> 0
                INSERT dbo.AuditLogs (Actor, Action, EntityType, EntityId, [Before], [After], [At])
                VALUES (@posterName, IIF(@fate = 5, N'DocumentCancelled', N'DocumentPosted'), N'StockDocument', @no, N'{"Status":1}', IIF(@fate = 5, N'{"Status":5}', N'{"Status":3}'), @t);
            IF @fate = 3   -- last statement before COMMIT: hot rows are locked as briefly as possible, always in key order (the balance rows exist from the start, see 11-seed-master.sql)
                UPDATE b SET QtyOnHand = b.QtyOnHand + l.signed, AvgCost = l.cost, REC_REVDATE = SYSUTCDATETIME()
                FROM @L l JOIN dbo.StockBalance b ON b.ProductId = l.pid AND b.WarehouseId = @wh
                OPTION (FORCE ORDER, LOOP JOIN);
            COMMIT;
            SET @i += 1;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() <> 0 ROLLBACK;
            IF ERROR_NUMBER() IN (1205, 2627, 2601) AND @retries < 100000 SET @retries += 1; ELSE THROW;
        END CATCH
    END
    INSERT dbo._WriterLog (Tag, Writer, Tx, Retries, StartUtc, EndUtc) VALUES (@tag, @w, @n, @retries, @start, SYSUTCDATETIME());
END
GO



