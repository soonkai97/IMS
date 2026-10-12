using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the public id (a random GUID) to the nine tables a client addresses and a unique index on it, <c>UX_&lt;Table&gt;_PublicId</c>, NOT filtered by REC_DELETED
    /// (an id is never handed out twice, soft-deleted rows included). The integer keys stay the clustered ones; nothing reads the new column yet.
    /// Built for a table that already holds millions of rows, in five steps per table, each of which can be run again after a failure:
    ///   1. add the column as NULL with a NEWID() default and no WITH VALUES: only the table's metadata changes, so it takes no time whatever the size, and a row
    ///      inserted from now on (old API instance, seed script) already has an id;
    ///   2. give the old rows an id in batches of 4,000 by clustered key (below SQL Server's lock-escalation threshold of 5,000 locks, so writers on other rows
    ///      keep running), each batch its own transaction, so the log can be reused between batches;
    ///   3. make the column NOT NULL (a scan that checks there is no NULL, with a schema lock);
    ///   4. rebuild the clustered index: every row grew by 16 bytes in place, so the pages split (measured on 1,000,000 documents: the table doubled from 279 MB at
    ///      97 % full to 558 MB at 51 % full and 98.6 % fragmented), and the rebuild brings it back to 279 MB (+16 MB for the new column);
    ///   5. build the unique index (FILLFACTOR 90: the first random inserts do not split pages).
    /// Offline, since ONLINE needs Enterprise or Developer edition, so run it in the maintenance window with the API stopped, like the other migrations that touch big
    /// tables. Measured on 1,000,000 StockDocument rows (LocalDB, warm): 0.02 s, 11.7 s, 2.5 s, 4.0 s and 1.7 s, 20 s in all; the log grows by about 400 MB per
    /// million documents, and the index takes 28 MB. The backfill of a table is ONE command of about 86,000 rows a second, so beyond about 5 million rows in one table
    /// it passes the 60 s command timeout of the application's connection (Limits.DbCommandTimeoutSeconds, a constant: there is no setting, and neither the
    /// application's start-up migration nor `dotnet ef` can raise it). Only the BACKFILL resumes after such a timeout (its batches are committed, a new run carries on
    /// with the rows still NULL). NOT NULL, the rebuild and the index are single statements that roll back as a whole; they would need about 24, 15 and 35 million rows
    /// to reach 60 s. For a table that big run the migration as a script without a timeout (`dotnet ef migrations script` and `sqlcmd -t 0`), or raise the constant for
    /// the window. The steps run outside the migration's transaction (suppressTransaction); the history row is written only when all of them are done. Giving a row its
    /// id changes its ROW_VERSION, so a document's ETag held by an open browser tab goes stale (a reload fixes it). Down drops the index, the default and the column.
    /// </summary>
    public partial class AddPublicIds : Migration
    {
        /// <summary>The tables of the entities that derive from <c>PublicEntity</c>.</summary>
        private static readonly string[] Tables = ["ApprovalRule", "Attachment", "Category", "Partner", "Product", "StockDocument", "Uom", "User", "Warehouse"];

        private const int BatchRows = 4000;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"""
                    IF COL_LENGTH(N'[{table}]', N'PublicId') IS NULL
                        ALTER TABLE [{table}] ADD [PublicId] uniqueidentifier NULL CONSTRAINT [DF_{table}_PublicId] DEFAULT (NEWID());
                    """, suppressTransaction: true);

                migrationBuilder.Sql($"""
                    DECLARE @last int = 0, @next int;
                    WHILE 1 = 1
                    BEGIN
                        SELECT @next = MAX([Id]) FROM (SELECT TOP ({BatchRows}) [Id] FROM [{table}] WHERE [Id] > @last ORDER BY [Id]) AS next_batch;
                        IF @next IS NULL BREAK;
                        UPDATE [{table}] SET [PublicId] = NEWID() WHERE [Id] > @last AND [Id] <= @next AND [PublicId] IS NULL;
                        SET @last = @next;
                    END
                    """, suppressTransaction: true);

                migrationBuilder.Sql($"""
                    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[{table}]') AND [name] = N'PublicId' AND [is_nullable] = 1)
                        ALTER TABLE [{table}] ALTER COLUMN [PublicId] uniqueidentifier NOT NULL;
                    """, suppressTransaction: true);

                migrationBuilder.Sql($"ALTER INDEX [PK_{table}] ON [{table}] REBUILD;", suppressTransaction: true);

                migrationBuilder.Sql($"""
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[{table}]') AND [name] = N'UX_{table}_PublicId')
                        CREATE UNIQUE NONCLUSTERED INDEX [UX_{table}_PublicId] ON [{table}] ([PublicId]) WITH (FILLFACTOR = 90);
                    """, suppressTransaction: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"""
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[{table}]') AND [name] = N'UX_{table}_PublicId')
                        DROP INDEX [UX_{table}_PublicId] ON [{table}];
                    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE [parent_object_id] = OBJECT_ID(N'[{table}]') AND [name] = N'DF_{table}_PublicId')
                        ALTER TABLE [{table}] DROP CONSTRAINT [DF_{table}_PublicId];
                    IF COL_LENGTH(N'[{table}]', N'PublicId') IS NOT NULL
                        ALTER TABLE [{table}] DROP COLUMN [PublicId];
                    """);
            }
        }
    }
}
