/*
  05-backup-restore.sql -- timed full backup, verify, restore-to-new-name, integrity check of a perf database (recovery drill).
    $env:Db = 'Ims_Perf'; $env:BackupDir = 'C:\Temp\imsperf_bk'      (PowerShell; sqlcmd reads $(Db) and $(BackupDir) from the environment,
    sqlcmd -S "(localdb)\ImsDev" -E -W -b -i docs/dba/05-backup-restore.sql   because "sqlcmd -v" cannot carry a value with a drive-letter colon)
  The backup folder must exist and be writable by the SQL Server service. The copy is restored as <Db>_Restore and dropped again.
  LocalDB = Express engine: BACKUP ... WITH COMPRESSION is not available (a production Standard/Enterprise server should use it).
  Checks after the restore: row counts equal the source, the append-only trigger and READ_COMMITTED_SNAPSHOT survived, DBCC CHECKDB is clean.
*/
SET NOCOUNT ON;
DECLARE @db sysname = N'$(Db)', @restore sysname = N'$(Db)_Restore';
DECLARE @bak nvarchar(400) = N'$(BackupDir)\$(Db)_drill.bak';
DECLARE @t datetime2 = SYSDATETIME(), @ms int, @sql nvarchar(max);

SET @sql = N'BACKUP DATABASE ' + QUOTENAME(@db) + N' TO DISK = @b WITH INIT, CHECKSUM';
EXEC sp_executesql @sql, N'@b nvarchar(400)', @b = @bak;
SET @ms = DATEDIFF(MILLISECOND, @t, SYSDATETIME()); SELECT 'backup_full_ms' AS step, @ms AS ms;

SET @t = SYSDATETIME();
RESTORE VERIFYONLY FROM DISK = @bak WITH CHECKSUM;
SET @ms = DATEDIFF(MILLISECOND, @t, SYSDATETIME()); SELECT 'verifyonly_ms' AS step, @ms AS ms;

SET @t = SYSDATETIME();
DECLARE @move nvarchar(max) = N'';
SELECT @move += N', MOVE N''' + f.name + N''' TO N''' + LEFT(f.physical_name, LEN(f.physical_name) - CHARINDEX(N'\', REVERSE(f.physical_name)) + 1)
                 + @restore + CASE f.type WHEN 1 THEN N'_log.ldf' ELSE N'.mdf' END + N''''
FROM sys.master_files f WHERE f.database_id = DB_ID(@db);
SET @sql = N'RESTORE DATABASE ' + QUOTENAME(@restore) + N' FROM DISK = @b WITH CHECKSUM, REPLACE' + @move;
EXEC sp_executesql @sql, N'@b nvarchar(400)', @b = @bak;
SET @ms = DATEDIFF(MILLISECOND, @t, SYSDATETIME()); SELECT 'restore_ms' AS step, @ms AS ms;

SET @t = SYSDATETIME();
SET @sql = N'DBCC CHECKDB (' + QUOTENAME(@restore) + N') WITH NO_INFOMSGS';
EXEC (@sql);
SET @ms = DATEDIFF(MILLISECOND, @t, SYSDATETIME()); SELECT 'checkdb_ms' AS step, @ms AS ms;

CREATE TABLE #r (what varchar(30), source_value bigint, restored_value bigint);
SET @sql = N'INSERT #r SELECT ''ledger_rows'', (SELECT COUNT_BIG(*) FROM ' + QUOTENAME(@db) + N'.dbo.StockLedgerEntry), (SELECT COUNT_BIG(*) FROM ' + QUOTENAME(@restore) + N'.dbo.StockLedgerEntry);
             INSERT #r SELECT ''documents'', (SELECT COUNT_BIG(*) FROM ' + QUOTENAME(@db) + N'.dbo.StockDocument), (SELECT COUNT_BIG(*) FROM ' + QUOTENAME(@restore) + N'.dbo.StockDocument);
             INSERT #r SELECT ''append_only_trigger'', (SELECT COUNT(*) FROM ' + QUOTENAME(@db) + N'.sys.triggers WHERE name = N''TR_StockLedgerEntry_AppendOnly''), (SELECT COUNT(*) FROM ' + QUOTENAME(@restore) + N'.sys.triggers WHERE name = N''TR_StockLedgerEntry_AppendOnly'')';
EXEC (@sql);
INSERT #r SELECT 'read_committed_snapshot', MAX(CASE WHEN name = @db THEN CAST(is_read_committed_snapshot_on AS int) END), MAX(CASE WHEN name = @restore THEN CAST(is_read_committed_snapshot_on AS int) END)
FROM sys.databases WHERE name IN (@db, @restore);
SELECT * FROM #r;

SET @sql = N'ALTER DATABASE ' + QUOTENAME(@restore) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@restore);
EXEC (@sql);
SELECT 'bak_size_mb' AS what, CAST(MAX(backup_size) / 1048576.0 AS decimal(12, 1)) AS mb FROM msdb.dbo.backupset WHERE database_name = @db AND backup_set_id = (SELECT MAX(backup_set_id) FROM msdb.dbo.backupset WHERE database_name = @db);
