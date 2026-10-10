/* 06-sizes.sql -- database + per-index size (MB) and row counts for the hot tables. Run with -d Ims_Perf. */
SET NOCOUNT ON;
SELECT DB_NAME() AS db, SUM(CASE WHEN type = 0 THEN size END) * 8 / 1024 AS data_mb, SUM(CASE WHEN type = 1 THEN size END) * 8 / 1024 AS log_mb FROM sys.database_files;
SELECT OBJECT_NAME(i.object_id) AS tbl, i.name AS idx, i.type_desc, SUM(ps.row_count) AS rows,
       CAST(SUM(ps.used_page_count) * 8 / 1024.0 AS decimal(12, 1)) AS mb
FROM sys.indexes i
JOIN sys.dm_db_partition_stats ps ON ps.object_id = i.object_id AND ps.index_id = i.index_id
WHERE OBJECT_NAME(i.object_id) IN (N'StockLedgerEntry', N'StockDocumentLine', N'StockDocument', N'StockBalance', N'Product', N'ProductBarcode', N'AuditLogs')
GROUP BY i.object_id, i.name, i.type_desc, i.index_id
ORDER BY tbl, i.index_id;
