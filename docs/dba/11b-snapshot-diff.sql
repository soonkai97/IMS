/*
  11b-snapshot-diff.sql -- difference between two snapshots taken by 11-snapshot.sql.
    sqlcmd -S "(localdb)\ImsDev" -E -d tempdb -W -v A=before_read-mix B=after_read-mix -i docs/dba/11b-snapshot-diff.sql
  Output: contention counters (deadlocks first), then the top 8 waits by wait time during the interval.
*/
SET NOCOUNT ON;
SELECT b.name AS counter, b.value - a.value AS delta
FROM tempdb.dbo.perf_snapshot a JOIN tempdb.dbo.perf_snapshot b ON b.kind = a.kind AND b.name = a.name
WHERE a.label = N'$(A)' AND b.label = N'$(B)' AND a.kind = 'counter'
ORDER BY b.name;
SELECT TOP 8 b.name AS wait_type, b.value - a.value AS wait_ms, c2.value - c1.value AS tasks
FROM tempdb.dbo.perf_snapshot a JOIN tempdb.dbo.perf_snapshot b ON b.kind = 'wait' AND b.name = a.name
JOIN tempdb.dbo.perf_snapshot c1 ON c1.kind = 'wait_cnt' AND c1.name = a.name AND c1.label = a.label
JOIN tempdb.dbo.perf_snapshot c2 ON c2.kind = 'wait_cnt' AND c2.name = a.name AND c2.label = b.label
WHERE a.kind = 'wait' AND a.label = N'$(A)' AND b.label = N'$(B)'
  AND a.name NOT LIKE N'%SLEEP%' AND a.name NOT LIKE N'%QUEUE%' AND a.name NOT LIKE N'%IDLE%' AND a.name NOT LIKE N'%XE_%' AND a.name NOT LIKE N'%HADR%'
  AND a.name NOT IN (N'REQUEST_FOR_DEADLOCK_SEARCH', N'CLR_AUTO_EVENT', N'DISPATCHER_QUEUE_SEMAPHORE', N'FT_IFTS_SCHEDULER_IDLE_WAIT', N'ONDEMAND_TASK_QUEUE',
                     N'SQLTRACE_BUFFER_FLUSH', N'WAITFOR', N'CHECKPOINT_QUEUE', N'DIRTY_PAGE_POLL', N'PVS_PREALLOCATE', N'BROKER_EVENTHANDLER', N'BROKER_RECEIVE_WAITFOR',
                     N'SOS_WORK_DISPATCHER', N'PWAIT_ALL_COMPONENTS_INITIALIZED', N'MEMORY_ALLOCATION_EXT', N'PREEMPTIVE_OS_FLUSHFILEBUFFERS', N'BROKER_TO_FLUSH', N'BROKER_TASK_STOP', N'LOGMGR_QUEUE')
ORDER BY b.value - a.value DESC;
