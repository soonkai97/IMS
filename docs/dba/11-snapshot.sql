/*
  11-snapshot.sql -- store a labelled snapshot of what proves (or disproves) deadlocks and lock contention during a load test.
  LocalDB/Express exposes no 'Locks' performance counters (sys.dm_os_performance_counters only lists XTP objects), so deadlocks are
  counted from (1) the always-on system_health Extended Events ring buffer (xml_deadlock_report events = full deadlock graphs) and
  (2) sys.dm_os_ring_buffers RING_BUFFER_EXCEPTION records with error 1205 (deadlock victim). Lock waits come from sys.dm_os_wait_stats LCK_M_*.
    sqlcmd -S "(localdb)\ImsDev" -E -d tempdb -b -W -v Label=before_read-mix -i docs/dba/11-snapshot.sql
  Snapshots live in tempdb.dbo.perf_snapshot (recreated automatically; lost when the instance restarts, which is fine).
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
IF OBJECT_ID(N'tempdb.dbo.perf_snapshot') IS NULL
    CREATE TABLE tempdb.dbo.perf_snapshot (label nvarchar(100), taken datetime2 DEFAULT SYSDATETIME(), kind varchar(10), name nvarchar(128), value bigint);
DELETE tempdb.dbo.perf_snapshot WHERE label = N'$(Label)';
INSERT tempdb.dbo.perf_snapshot (label, kind, name, value)
SELECT N'$(Label)', 'counter', N'ring_buffer error 1205 (deadlock victim) records',
       (SELECT COUNT(*) FROM (SELECT CAST(record AS xml) AS r FROM sys.dm_os_ring_buffers WHERE ring_buffer_type = N'RING_BUFFER_EXCEPTION') q
        WHERE q.r.value('(Record/Exception/Error)[1]', 'int') = 1205);
INSERT tempdb.dbo.perf_snapshot (label, kind, name, value)
SELECT N'$(Label)', 'counter', N'system_health xml_deadlock_report events',
       (SELECT COUNT(*) FROM (SELECT CAST(t.target_data AS xml) AS x FROM sys.dm_xe_session_targets t JOIN sys.dm_xe_sessions s ON s.address = t.event_session_address
                              WHERE s.name = N'system_health' AND t.target_name = N'ring_buffer') q
        CROSS APPLY q.x.nodes('RingBufferTarget/event[@name="xml_deadlock_report"]') n(e));
INSERT tempdb.dbo.perf_snapshot (label, kind, name, value)
SELECT N'$(Label)', 'counter', N'lock waits (LCK_M_* waiting tasks)', ISNULL(SUM(waiting_tasks_count), 0) FROM sys.dm_os_wait_stats WHERE wait_type LIKE N'LCK[_]M[_]%';
INSERT tempdb.dbo.perf_snapshot (label, kind, name, value)
SELECT N'$(Label)', 'counter', N'lock wait time ms (LCK_M_*)', ISNULL(SUM(wait_time_ms), 0) FROM sys.dm_os_wait_stats WHERE wait_type LIKE N'LCK[_]M[_]%';
INSERT tempdb.dbo.perf_snapshot (label, kind, name, value)
SELECT N'$(Label)', 'wait', wait_type, wait_time_ms FROM sys.dm_os_wait_stats WHERE wait_time_ms > 0;
INSERT tempdb.dbo.perf_snapshot (label, kind, name, value)
SELECT N'$(Label)', 'wait_cnt', wait_type, waiting_tasks_count FROM sys.dm_os_wait_stats WHERE waiting_tasks_count > 0;
