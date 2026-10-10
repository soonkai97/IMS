/*
  04-query-store-top.sql -- the real EF-generated statements of a workload run, worst first, from the Query Store.
  Express/LocalDB supports the Query Store, so no tracing is needed to see the exact SQL (with the REC_DELETED predicates EF adds):

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -b -Q "ALTER DATABASE Ims_Perf SET QUERY_STORE = ON; ALTER DATABASE Ims_Perf SET QUERY_STORE (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL, INTERVAL_LENGTH_MINUTES = 1); ALTER DATABASE Ims_Perf SET QUERY_STORE CLEAR"
    ... run the workload (02-measure-api.ps1) ...
    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -W -s"|" -i docs/dba/04-query-store-top.sql

  Statements that ran fewer than 3 times (setup noise) are left out. avg_reads = logical page reads per execution.
*/
SET NOCOUNT ON;
EXEC sys.sp_query_store_flush_db;
SELECT TOP (25)
       q.query_id,
       SUM(rs.count_executions) AS runs,
       CAST(SUM(rs.avg_duration * rs.count_executions) / SUM(rs.count_executions) / 1000.0 AS decimal(12, 1)) AS avg_ms,
       CAST(MAX(rs.max_duration) / 1000.0 AS decimal(12, 1)) AS max_ms,
       CAST(SUM(rs.avg_logical_io_reads * rs.count_executions) / SUM(rs.count_executions) AS bigint) AS avg_reads,
       COUNT(DISTINCT p.plan_id) AS plans,
       LEFT(REPLACE(REPLACE(qt.query_sql_text, CHAR(13), N' '), CHAR(10), N' '), 160) AS sql_text
FROM sys.query_store_query q
JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
JOIN sys.query_store_plan p ON p.query_id = q.query_id
JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
WHERE qt.query_sql_text NOT LIKE N'%sys.%' AND qt.query_sql_text NOT LIKE N'%query_store%'
GROUP BY q.query_id, qt.query_sql_text
HAVING SUM(rs.count_executions) >= 3
ORDER BY SUM(rs.avg_duration * rs.count_executions) / SUM(rs.count_executions) DESC;
