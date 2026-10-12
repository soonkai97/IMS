using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// The migration that adds the public id (a random GUID) to the nine addressed tables, run against a database that already holds rows (soft-deleted ones and
/// more than one backfill batch included): every old row gets its own id, the unique index is not filtered by REC_DELETED, the migration goes down and up again,
/// and a run that stopped half way can be run again. The test has its own throw-away database.
/// </summary>
public class PublicIdMigrationTests
{
    private const string Before = "20261011133108_AddLedgerBalanceAfter";
    private const string Latest = "20261012031930_AddPublicIds";
    private const int BulkPartners = 9_500; // more than two backfill batches of 4,000

    private static readonly string[] Tables = ["ApprovalRule", "Attachment", "Category", "Partner", "Product", "StockDocument", "Uom", "User", "Warehouse"];

    private static async Task MigrateAsync(TestDatabase database, string target)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>A few rows in each of the nine tables (one soft-deleted in some) and many partners with holes in their ids, written in the schema BEFORE the migration.</summary>
    private static async Task SeedOldDataAsync(TestDatabase database)
    {
        await database.ExecuteAsync("""
            INSERT [User] (Username, Email, DisplayName, PasswordHash, Role, IsActive, FailedLoginCount, REC_DELETED) VALUES
                (N'first', N'first@example.test', N'First', N'x', N'Admin', 1, 0, 0), (N'second', N'second@example.test', N'Second', N'x', N'Supervisor', 1, 0, 0),
                (N'gone', N'gone@example.test', N'Gone', N'x', N'Clerk', 0, 0, 1);
            INSERT Uom (Code, Name) VALUES (N'PCS', N'Pieces'), (N'BOX', N'Boxes');
            INSERT Warehouse (Code, Name, IsActive) VALUES (N'W1', N'One', 1), (N'W2', N'Two', 1);
            INSERT Category (Name, REC_DELETED) VALUES (N'Tools', 0), (N'Retired', 1);
            INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive) SELECT N'P1', N'Product one', Id, 0, 1 FROM Uom WHERE Code = N'PCS';
            INSERT ApprovalRule (DocType, AmountThreshold, QtyThreshold, RoleRequired, IsActive) VALUES (1, 100, NULL, N'Supervisor', 1), (2, NULL, 50, N'Owner', 0);
            """);
        await database.ExecuteAsync("""
            INSERT Partner (Type, Name, Contact, IsActive)
                SELECT TOP (@n) 1, CONCAT(N'Bulk ', ROW_NUMBER() OVER (ORDER BY (SELECT NULL))), NULL, 1 FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            DELETE FROM Partner WHERE Id % 7 = 0; -- holes in the key, so a batch is not a plain range of consecutive ids
            """, ("@n", BulkPartners));
        await database.ExecuteAsync("""
            DECLARE @u int = (SELECT Id FROM [User] WHERE Username = N'first'), @w int = (SELECT Id FROM Warehouse WHERE Code = N'W1');
            INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval) VALUES
                (1, N'RCV-W1-0001', 3, @w, @u, SYSUTCDATETIME(), 0), (2, N'ISS-W1-0001', 3, @w, @u, SYSUTCDATETIME(), 0);
            INSERT Attachment (OwnerType, OwnerId, FileName, StoragePath, ContentType, Kind, SizeBytes, Sha256, UploadedBy, UploadedAt) VALUES
                (1, 1, N'a.pdf', N'2026/a.bin', N'application/pdf', 1, 10, REPLICATE('a', 64), @u, SYSUTCDATETIME()),
                (1, 1, N'b.pdf', N'2026/b.bin', N'application/pdf', 1, 10, REPLICATE('b', 64), @u, SYSUTCDATETIME());
            """);
    }

    private static async Task AssertEveryRowHasItsOwnIdAsync(TestDatabase database)
    {
        var total = 0;
        foreach (var table in Tables)
        {
            var rows = await database.ScalarAsync<int>($"SELECT COUNT(*) FROM [{table}]");
            Assert.True(rows > 0, $"{table} has no rows to check");
            Assert.Equal(rows, await database.ScalarAsync<int>($"SELECT COUNT(DISTINCT [PublicId]) FROM [{table}] WHERE [PublicId] <> '00000000-0000-0000-0000-000000000000'"));
            total += rows;
        }
        // One id per row across the nine tables: an id never repeats, soft-deleted rows included.
        var all = string.Join(" UNION ALL ", Tables.Select(t => $"SELECT [PublicId] FROM [{t}]"));
        Assert.Equal(total, await database.ScalarAsync<int>($"SELECT COUNT(DISTINCT [PublicId]) FROM ({all}) ids"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [User] WHERE Username = N'gone' AND [PublicId] IS NOT NULL"));
    }

    private static async Task AssertColumnAndIndexAsync(TestDatabase database)
    {
        foreach (var table in Tables)
        {
            Assert.Equal(0, await database.ScalarAsync<int>("SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = N'PublicId'", ("@t", table)));
            var index = $"UX_{table}_PublicId";

            // Unique, non-clustered, built with FILLFACTOR 90, and NOT filtered by REC_DELETED.
            Assert.Equal("1|NONCLUSTERED|90|0", await database.ScalarAsync<string>(
                "SELECT CONCAT(is_unique, '|', type_desc, '|', fill_factor, '|', has_filter) FROM sys.indexes WHERE object_id = OBJECT_ID(@t) AND name = @i", ("@t", table), ("@i", index)));
            Assert.Equal("PublicId", await database.ScalarAsync<string>(
                "SELECT STRING_AGG(c.name, ',') FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
                "JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.object_id = OBJECT_ID(@t) AND i.name = @i", ("@t", table), ("@i", index)));

            // The clustered key is still the integer Id.
            Assert.Equal("Id", await database.ScalarAsync<string>(
                "SELECT STRING_AGG(c.name, ',') FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id " +
                "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(@t) AND i.type = 1", ("@t", table)));
        }

        // A writer that does not know the column (a script, an old API instance) still gets an id, and a different one each time.
        await database.ExecuteAsync("INSERT Uom (Code, Name) VALUES (N'RAW1', N'Raw one'), (N'RAW2', N'Raw two')");
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(DISTINCT PublicId) FROM Uom WHERE Code IN (N'RAW1', N'RAW2')"));
        await database.ExecuteAsync("DELETE FROM Uom WHERE Code IN (N'RAW1', N'RAW2')");
    }

    [Fact]
    public async Task The_migration_gives_every_old_row_its_own_id_and_goes_down_and_up_again()
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, Before);
            await SeedOldDataAsync(database);
            var rows = await database.ScalarAsync<int>("SELECT COUNT(*) FROM Partner");
            Assert.InRange(rows, BulkPartners / 2, BulkPartners);

            await MigrateAsync(database, Latest);
            await AssertEveryRowHasItsOwnIdAsync(database);
            await AssertColumnAndIndexAsync(database);
            Assert.Equal(rows, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Partner"));

            await MigrateAsync(database, Before); // down: the column, its default and the index go, the rows stay
            foreach (var table in Tables)
            {
                Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE name = N'PublicId' AND object_id = OBJECT_ID(@t)", ("@t", table)));
                Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE name = @i", ("@i", $"UX_{table}_PublicId")));
                Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.default_constraints WHERE name = @d", ("@d", $"DF_{table}_PublicId")));
            }
            Assert.Equal(rows, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Partner"));
            Assert.Equal(3, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [User]"));

            await MigrateAsync(database, Latest); // and up again: new ids, again one per row
            await AssertEveryRowHasItsOwnIdAsync(database);
            await AssertColumnAndIndexAsync(database);
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [Fact]
    public async Task A_run_that_stopped_half_way_is_finished_by_running_it_again_and_keeps_the_ids_already_given()
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, Before);
            await SeedOldDataAsync(database);
            await MigrateAsync(database, Latest);

            // What a run killed in the middle of the Partner table leaves: the first rows without an id, the column still NULL-able, no index, no history row.
            var kept = await database.ScalarAsync<Guid>("SELECT TOP (1) PublicId FROM Partner ORDER BY Id DESC");
            await database.ExecuteAsync("DROP INDEX [UX_Partner_PublicId] ON [Partner]");
            await database.ExecuteAsync("ALTER TABLE [Partner] ALTER COLUMN [PublicId] uniqueidentifier NULL");
            await database.ExecuteAsync("UPDATE Partner SET PublicId = NULL WHERE Id < (SELECT MAX(Id) FROM Partner) - 100");
            await database.ExecuteAsync("DELETE FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest));

            await MigrateAsync(database, Latest);

            await AssertEveryRowHasItsOwnIdAsync(database);
            await AssertColumnAndIndexAsync(database);
            Assert.Equal(kept, await database.ScalarAsync<Guid>("SELECT TOP (1) PublicId FROM Partner ORDER BY Id DESC"));
        }
        finally
        {
            await database.DropAsync();
        }
    }
}
