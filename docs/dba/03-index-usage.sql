/*
  03-index-usage.sql -- which indexes does the workload actually read, and what do they cost to maintain?
  Run it after a realistic workload (02-measure-api.ps1, the k6 scenarios) against the same perf database:

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -W -s"|" -i docs/dba/03-index-usage.sql

  dm_db_index_usage_stats is reset when the database is (re)attached or the instance restarts, so take the baseline and the
  "after" numbers in separate sessions (ALTER DATABASE ... SET OFFLINE / ONLINE resets it without a restart).
  reads  = seeks + scans + lookups (queries that used the index); writes = updates (every insert/update/delete that had to maintain it).
  An index with many writes and no reads is pure cost: drop it, or find the query it was meant for.
*/
SET NOCOUNT ON;
SELECT OBJECT_NAME(i.object_id) AS tbl, i.name AS idx, i.is_unique AS uq, i.filter_definition AS filt,
       ISNULL(u.user_seeks, 0) AS seeks, ISNULL(u.user_scans, 0) AS scans, ISNULL(u.user_lookups, 0) AS lookups, ISNULL(u.user_updates, 0) AS writes,
       CAST(ps.mb AS decimal(10, 1)) AS mb
FROM sys.indexes i
JOIN sys.objects o ON o.object_id = i.object_id AND o.type = 'U' AND o.is_ms_shipped = 0
CROSS APPLY (SELECT SUM(used_page_count) * 8 / 1024.0 AS mb FROM sys.dm_db_partition_stats p WHERE p.object_id = i.object_id AND p.index_id = i.index_id) ps
LEFT JOIN sys.dm_db_index_usage_stats u ON u.database_id = DB_ID() AND u.object_id = i.object_id AND u.index_id = i.index_id
WHERE i.index_id > 0 AND i.is_primary_key = 0 AND ps.mb > 1
ORDER BY tbl, idx;
