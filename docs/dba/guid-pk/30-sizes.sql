/*
  30-sizes.sql -- size of every index of the measured tables, one row per index: rows, used and reserved MB, key columns, whether it is a
  foreign-key index (first key column is a foreign-key column of its table) and whether it carries the public id (hybrid B).
  Excludes the writers' helper tables (names starting with _). Result set 1 only; bytes per row = used_mb * 1048576 / table rows.
*/
SET NOCOUNT ON;
SELECT OBJECT_NAME(i.object_id) AS tbl, i.name AS idx, i.type_desc AS kind, i.is_unique, i.has_filter,
       SUM(ps.row_count) AS rows,
       CAST(SUM(ps.used_page_count) * 8 / 1024.0 AS decimal(14,2)) AS used_mb,
       CAST(SUM(ps.reserved_page_count) * 8 / 1024.0 AS decimal(14,2)) AS reserved_mb,
       SUM(ps.used_page_count) AS used_pages,
       STUFF((SELECT ',' + c.name + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE '' END FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0 ORDER BY ic.key_ordinal FOR XML PATH('')), 1, 1, '') AS key_columns,
       CAST(CASE WHEN i.type = 2 AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.foreign_key_columns fkc ON fkc.parent_object_id = ic.object_id AND fkc.parent_column_id = ic.column_id
                                             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1) THEN 1 ELSE 0 END AS bit) AS is_fk_index,
       CAST(CASE WHEN i.name LIKE N'UX[_]%[_]PublicId' THEN 1 ELSE 0 END AS bit) AS is_public_id_index
FROM sys.indexes i
JOIN sys.dm_db_partition_stats ps ON ps.object_id = i.object_id AND ps.index_id = i.index_id
WHERE OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1 AND OBJECT_NAME(i.object_id) NOT LIKE N'[_]%' AND i.type IN (1, 2)
GROUP BY i.object_id, i.index_id, i.name, i.type, i.type_desc, i.is_unique, i.has_filter
ORDER BY tbl, i.index_id;
