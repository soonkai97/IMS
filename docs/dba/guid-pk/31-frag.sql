/*
  31-frag.sql -- physical state of every index leaf level (DETAILED scan): pages, fragmentation %, average page density %.
  Fragmentation is the share of out-of-order leaf pages; density is how full the pages are (random inserts leave ~60-70 %).
*/
SET NOCOUNT ON;
SELECT OBJECT_NAME(ips.object_id) AS tbl, i.name AS idx, i.type_desc AS kind, ips.page_count, CAST(ips.avg_fragmentation_in_percent AS decimal(6,2)) AS frag_pct,
       CAST(ips.avg_page_space_used_in_percent AS decimal(6,2)) AS density_pct, ips.fragment_count, ips.record_count
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'DETAILED') ips
JOIN sys.indexes i ON i.object_id = ips.object_id AND i.index_id = ips.index_id
WHERE ips.index_level = 0 AND ips.alloc_unit_type_desc = N'IN_ROW_DATA' AND OBJECTPROPERTY(ips.object_id, 'IsUserTable') = 1 AND OBJECT_NAME(ips.object_id) NOT LIKE N'[_]%' AND ips.page_count >= 8
ORDER BY tbl, i.index_id;
