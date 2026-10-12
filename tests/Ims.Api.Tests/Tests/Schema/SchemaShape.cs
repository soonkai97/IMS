using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Tests.Schema;

/// <summary>
/// Reads the shape of a migrated database as one text line per fact: each table with its primary key, each foreign key, each unique index,
/// each trigger and the snapshot-isolation switch. Column lists are written in key order, so a changed key or index changes a line.
/// </summary>
public static class SchemaShape
{
    private const string HistoryTable = "__EFMigrationsHistory";

    private const string Tables = $"""
        SELECT CONCAT('TABLE ', t.name, ' PK(', ISNULL(pk.Cols, 'none'), ') ', ISNULL(LOWER(pk.TypeDesc COLLATE DATABASE_DEFAULT), ''))
        FROM sys.tables t
        OUTER APPLY (
            SELECT i.type_desc AS TypeDesc,
                   STRING_AGG(CONCAT(c.name, ':', ty.name COLLATE DATABASE_DEFAULT, IIF(c.is_identity = 1, ' identity', '')), ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Cols
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE i.object_id = t.object_id AND i.is_primary_key = 1
            GROUP BY i.type_desc) pk
        WHERE t.is_ms_shipped = 0 AND t.name <> '{HistoryTable}'
        """;

    private const string ForeignKeys = """
        SELECT CONCAT('FK ', parent.name, '(', cols.Child, ') -> ', referenced.name, '(', cols.Parent, ') on delete ', LOWER(fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT))
        FROM sys.foreign_keys fk
        JOIN sys.tables parent ON parent.object_id = fk.parent_object_id
        JOIN sys.tables referenced ON referenced.object_id = fk.referenced_object_id
        CROSS APPLY (
            SELECT STRING_AGG(cc.name, ', ') WITHIN GROUP (ORDER BY fkc.constraint_column_id) AS Child,
                   STRING_AGG(pc.name, ', ') WITHIN GROUP (ORDER BY fkc.constraint_column_id) AS Parent
            FROM sys.foreign_key_columns fkc
            JOIN sys.columns cc ON cc.object_id = fkc.parent_object_id AND cc.column_id = fkc.parent_column_id
            JOIN sys.columns pc ON pc.object_id = fkc.referenced_object_id AND pc.column_id = fkc.referenced_column_id
            WHERE fkc.constraint_object_id = fk.object_id) cols
        """;

    private const string UniqueIndexes = $"""
        SELECT CONCAT('UNIQUE ', t.name, '.', i.name, ' (', cols.Keys, ')', IIF(i.filter_definition IS NULL, '', CONCAT(' where ', i.filter_definition COLLATE DATABASE_DEFAULT)))
        FROM sys.indexes i
        JOIN sys.tables t ON t.object_id = i.object_id
        CROSS APPLY (
            SELECT STRING_AGG(CONCAT(c.name, IIF(ic.is_descending_key = 1, ' desc', '')), ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Keys
            FROM sys.index_columns ic
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0) cols
        WHERE i.is_unique = 1 AND i.is_primary_key = 0 AND t.is_ms_shipped = 0 AND t.name <> '{HistoryTable}'
        """;

    private const string Triggers = """
        SELECT CONCAT('TRIGGER ', OBJECT_NAME(tr.parent_id), '.', tr.name) FROM sys.triggers tr WHERE tr.parent_class = 1
        """;

    private const string Isolation = """
        SELECT IIF(is_read_committed_snapshot_on = 1, 'READ_COMMITTED_SNAPSHOT on', 'READ_COMMITTED_SNAPSHOT off') FROM sys.databases WHERE name = DB_NAME()
        """;

    public static async Task<IReadOnlyList<string>> ReadAsync(string connectionString)
    {
        var lines = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var query in new[] { Tables, ForeignKeys, UniqueIndexes, Triggers, Isolation })
        {
            await using var command = new SqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        }
        return lines.Order(StringComparer.Ordinal).ToList();
    }
}
