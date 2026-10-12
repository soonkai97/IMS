/*
  07-maintenance.sql -- index + statistics maintenance for the IMS database (run weekly, and once right after any migration
  that adds columns to a big table, e.g. AuditColumns).

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -b -W -v Execute=1 -i docs/dba/07-maintenance.sql      (Execute=0 only prints the plan)

  Rules (Microsoft guidance, tuned for this schema):
    - ignore indexes under 1,000 pages (8 MB): fragmentation of tiny indexes is noise
    - fragmentation  5..30 %  -> ALTER INDEX ... REORGANIZE   (online, interruptible, no log explosion)
    - fragmentation  > 30 %  -> ALTER INDEX ... REBUILD       (ONLINE = ON only where the edition allows it: Enterprise/Azure SQL/
                                Developer. Express/LocalDB/Standard-without-online: offline, takes a schema lock for the duration)
    - page density < 70 % on a big index (after a column-adding migration the clustered indexes sit at 55-75 % and 98 % fragmentation)
      -> REBUILD as well
    - FILLFACTOR: append-only / identity-clustered tables (StockLedgerEntry, AuditLogs, StockDocumentLine, StockDocument) are rebuilt
      with FILLFACTOR = 100 (inserts only go to the end; no page splits); random-key indexes (Sku, No, Barcode, Name) with 90.
    - after REBUILD the index statistics are fresh (FULLSCAN); after REORGANIZE the statistics of that index are refreshed (done below;
      that had a REORGANIZE; every other statistic with >5 %% modified rows is refreshed at the end).
  Prerequisites: SQL Server 2022+/Azure SQL for RESUMABLE; not used here so the script runs on every edition.
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON;   -- required to maintain filtered indexes
DECLARE @execute bit = $(Execute);
DECLARE @online bit = CASE WHEN SERVERPROPERTY('EngineEdition') IN (3, 5, 8) THEN 1 ELSE 0 END;   -- 3 = Enterprise/Developer, 5 = Azure SQL DB, 8 = Managed Instance
DECLARE @t0 datetime2, @sql nvarchar(max), @obj sysname, @idx sysname, @action varchar(10), @ff int, @frag float, @pages bigint, @dens float, @id int;

SELECT s.name AS sch, o.name AS tbl, i.name AS idx, i.index_id, i.type,
       ps.avg_fragmentation_in_percent AS frag, ps.avg_page_space_used_in_percent AS density, ps.page_count AS pages
INTO #w
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'SAMPLED') ps
JOIN sys.objects o ON o.object_id = ps.object_id AND o.is_ms_shipped = 0 AND o.type = 'U'
JOIN sys.schemas s ON s.schema_id = o.schema_id
JOIN sys.indexes i ON i.object_id = ps.object_id AND i.index_id = ps.index_id
WHERE ps.index_id > 0 AND ps.page_count >= 1000 AND ps.alloc_unit_type_desc = 'IN_ROW_DATA'
  AND (ps.avg_fragmentation_in_percent >= 5 OR ps.avg_page_space_used_in_percent < 70);

ALTER TABLE #w ADD id int IDENTITY(1, 1);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT id FROM #w ORDER BY pages DESC;
OPEN c; FETCH NEXT FROM c INTO @id;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @obj = QUOTENAME(sch) + N'.' + QUOTENAME(tbl), @idx = QUOTENAME(idx), @frag = frag, @dens = density, @pages = pages,
           @ff = CASE WHEN idx LIKE N'UX[_]%[_]PublicId' THEN 90   -- random GUID keys (public ids): leave room for the inserts, even on the append-only tables
                      WHEN tbl IN (N'StockLedgerEntry', N'AuditLogs', N'StockDocumentLine', N'StockDocument') AND idx LIKE N'PK[_]%' THEN 100
                      WHEN tbl IN (N'StockLedgerEntry', N'AuditLogs', N'StockDocumentLine', N'StockDocument') THEN 100 ELSE 90 END
    FROM #w WHERE id = @id;
    SET @action = CASE WHEN @frag > 30 OR @dens < 70 THEN 'REBUILD' ELSE 'REORGANIZE' END;
    SET @sql = N'ALTER INDEX ' + @idx + N' ON ' + @obj + N' ' + @action
             + CASE WHEN @action = 'REBUILD' THEN N' WITH (FILLFACTOR = ' + CAST(@ff AS nvarchar(3)) + N', SORT_IN_TEMPDB = ON'
                         + CASE WHEN @online = 1 THEN N', ONLINE = ON' ELSE N'' END + N')' ELSE N'' END;
    PRINT CONCAT(@sql, N'   -- frag ', CAST(@frag AS decimal(5, 1)), N' %, density ', CAST(@dens AS decimal(5, 1)), N' %, ', @pages, N' pages');
    IF @execute = 1
    BEGIN
        SET @t0 = SYSDATETIME();
        EXEC sys.sp_executesql @sql;
        PRINT CONCAT('   done in ', DATEDIFF(MILLISECOND, @t0, SYSDATETIME()), ' ms');
        IF @action = 'REORGANIZE' BEGIN SET @sql = N'UPDATE STATISTICS ' + @obj + N' ' + @idx + N' WITH FULLSCAN'; EXEC sys.sp_executesql @sql; END
    END
    FETCH NEXT FROM c INTO @id;
END
CLOSE c; DEALLOCATE c;

IF @execute = 1
BEGIN
    -- statistics: refresh (FULLSCAN) where >5 % of the rows changed since the last update, or never updated
    SET @t0 = SYSDATETIME();
    DECLARE s CURSOR LOCAL FAST_FORWARD FOR
        SELECT QUOTENAME(sc.name) + N'.' + QUOTENAME(o.name), QUOTENAME(st.name)
        FROM sys.stats st JOIN sys.objects o ON o.object_id = st.object_id AND o.type = 'U' AND o.is_ms_shipped = 0
        JOIN sys.schemas sc ON sc.schema_id = o.schema_id
        CROSS APPLY sys.dm_db_stats_properties(st.object_id, st.stats_id) sp
        WHERE sp.rows > 1000 AND (sp.last_updated IS NULL OR sp.modification_counter > 0.05 * sp.rows);
    OPEN s; FETCH NEXT FROM s INTO @obj, @idx;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'UPDATE STATISTICS ' + @obj + N' ' + @idx + N' WITH FULLSCAN';
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM s INTO @obj, @idx;
    END
    CLOSE s; DEALLOCATE s;
    PRINT CONCAT('statistics refreshed in ', DATEDIFF(MILLISECOND, @t0, SYSDATETIME()), ' ms');
END
