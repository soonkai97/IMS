using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Ims.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// The migration of this change, run against a database that already holds data: the backfill must give old rows the values new rows get, and the migration must
/// go down and up again. Each test has its own throw-away database.
/// </summary>
public class MigrationTests
{
    private const string Before = "20261010145459_AddDocumentReversedAt";
    private const string Latest = "20261011072009_AddDocumentReversedBy";

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
    }

    private static async Task MigrateAsync(TestDatabase database, string target)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FixedClock());
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>A received document, an issued one that was reversed (by the second user, who created its reversal ledger entry), written in the schema BEFORE the migration.</summary>
    private static async Task SeedOldDataAsync(TestDatabase database)
    {
        await database.ExecuteAsync("""
            INSERT [User] (Username, Email, DisplayName, PasswordHash, Role, IsActive, FailedLoginCount) VALUES
                (N'first', N'first@example.test', N'First', N'x', N'Admin', 1, 0), (N'second', N'second@example.test', N'Second', N'x', N'Supervisor', 1, 0);
            INSERT Uom (Code, Name) VALUES (N'PCS', N'Pieces');
            INSERT Warehouse (Code, Name, IsActive) VALUES (N'W1', N'One', 1);
            INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive) SELECT N'P1', N'Product one', Id, 0, 1 FROM Uom;
            """);
        await database.ExecuteAsync("""
            DECLARE @u1 int = (SELECT Id FROM [User] WHERE Username = N'first'), @u2 int = (SELECT Id FROM [User] WHERE Username = N'second');
            DECLARE @w1 int = (SELECT Id FROM Warehouse WHERE Code = N'W1'), @p1 int = (SELECT Id FROM Product WHERE Sku = N'P1');
            INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval) VALUES
                (1, N'RCV-W1-0001', 3, @w1, @u1, SYSUTCDATETIME(), 0),
                (2, N'ISS-W1-0001', 4, @w1, @u1, SYSUTCDATETIME(), 0);   -- 4 = Reversed
            INSERT StockLedgerEntry (ProductId, WarehouseId, QtyDelta, UnitCost, SourceDocId, Reason, OccurredAt, CreatedBy)
                SELECT @p1, @w1, 6, 1, Id, 6, SYSUTCDATETIME(), @u2 FROM StockDocument WHERE No = N'ISS-W1-0001';   -- 6 = Reversal, by the second user
            """);
    }

    private static async Task AssertBackfilledAsync(TestDatabase database)
    {
        // Who reversed the reversed document: the creator of its reversal entry. Documents that were never reversed stay empty.
        var second = await database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = N'second'");
        Assert.Equal(second, await database.ScalarAsync<int>("SELECT ReversedBy FROM StockDocument WHERE No = N'ISS-W1-0001'"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE No = N'RCV-W1-0001' AND ReversedBy IS NOT NULL"));
    }

    [Fact]
    public async Task The_migration_backfills_old_rows_and_goes_down_and_up_again()
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, Before);
            await SeedOldDataAsync(database);

            await MigrateAsync(database, Latest);
            await AssertBackfilledAsync(database);

            await MigrateAsync(database, Before); // down: the column goes, the data stays
            Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE name = N'ReversedBy' AND object_id = OBJECT_ID(N'StockDocument')"));
            Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM StockLedgerEntry"));

            await MigrateAsync(database, Latest); // and up again: the backfill gives the same answer
            await AssertBackfilledAsync(database);
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [Fact]
    public void The_numbers_the_backfill_states_are_the_stored_values_of_the_enums()
    {
        // A migration states the stored value (it must not follow a later renumbering of the enum); this test fails when the enums move under it.
        Assert.Equal((int)DocumentStatus.Reversed, AddDocumentReversedBy.ReversedStatus);
        Assert.Equal((int)LedgerReason.Reversal, AddDocumentReversedBy.ReversalReason);
    }
}
