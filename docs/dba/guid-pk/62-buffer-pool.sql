/*
  62-buffer-pool.sql -- what the buffer pool holds for this database right now, by table and index (8 KB pages) -- the footprint of a workload when run
  after CHECKPOINT + DBCC DROPCLEANBUFFERS. One result set: tbl, idx, kind, cached_pages, cached_mb, dirty_pages. Row "(all)" = whole database.
*/
SET NOCOUNT ON;
SELECT tbl, idx, kind, SUM(cnt) AS cached_pages, CAST(SUM(cnt) * 8 / 1024.0 AS decimal(12,2)) AS cached_mb, SUM(dirty) AS dirty_pages
FROM (
    SELECT OBJECT_NAME(p.object_id) AS tbl, i.name AS idx, i.type_desc AS kind, COUNT(*) AS cnt, SUM(CASE WHEN b.is_modified = 1 THEN 1 ELSE 0 END) AS dirty
    FROM sys.dm_os_buffer_descriptors b
    JOIN sys.allocation_units au ON au.allocation_unit_id = b.allocation_unit_id
    JOIN sys.partitions p ON p.hobt_id = au.container_id AND au.type IN (1, 3)
    JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
    WHERE b.database_id = DB_ID() AND OBJECTPROPERTY(p.object_id, 'IsUserTable') = 1
    GROUP BY p.object_id, i.name, i.type_desc
    UNION ALL
    SELECT OBJECT_NAME(p.object_id), i.name, i.type_desc + N' (LOB)', COUNT(*), SUM(CASE WHEN b.is_modified = 1 THEN 1 ELSE 0 END)
    FROM sys.dm_os_buffer_descriptors b
    JOIN sys.allocation_units au ON au.allocation_unit_id = b.allocation_unit_id
    JOIN sys.partitions p ON p.partition_id = au.container_id AND au.type = 2
    JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
    WHERE b.database_id = DB_ID() AND OBJECTPROPERTY(p.object_id, 'IsUserTable') = 1
    GROUP BY p.object_id, i.name, i.type_desc
) x
GROUP BY tbl, idx, kind
UNION ALL
SELECT N'(all)', NULL, NULL, COUNT(*), CAST(COUNT(*) * 8 / 1024.0 AS decimal(12,2)), SUM(CASE WHEN is_modified = 1 THEN 1 ELSE 0 END)
FROM sys.dm_os_buffer_descriptors WHERE database_id = DB_ID()
ORDER BY tbl, idx;
