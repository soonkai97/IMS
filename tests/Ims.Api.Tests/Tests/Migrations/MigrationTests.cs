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
/// The two migrations of this change, run against a database that already holds data: the backfill must give old rows the values new rows get, the append-only
/// trigger of the ledger must be back on afterwards, and the migrations must go down and up again. Each test has its own throw-away database.
/// </summary>
public class MigrationTests
{
    private const string BeforeReversedBy = "20261010145459_AddDocumentReversedAt";
    private const string Latest = "20261011054750_AddLedgerBalanceAfter";

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

    /// <summary>
    /// Interleaved movements of two products and two warehouses, an issue that was reversed, written in the schema BEFORE the migrations. Ledger ids ascend in the
    /// order below: p1/w1 +10, p2/w1 +5, p1/w2 +3, p1/w1 +4, p1/w1 -6 (issue), p1/w1 +6 (reversal of that issue, by the second user).
    /// </summary>
    private static async Task SeedOldDataAsync(TestDatabase database)
    {
        await database.ExecuteAsync("""
            INSERT [User] (Username, Email, DisplayName, PasswordHash, Role, IsActive, FailedLoginCount) VALUES
                (N'first', N'first@example.test', N'First', N'x', N'Admin', 1, 0), (N'second', N'second@example.test', N'Second', N'x', N'Supervisor', 1, 0);
            INSERT Uom (Code, Name) VALUES (N'PCS', N'Pieces');
            INSERT Warehouse (Code, Name, IsActive) VALUES (N'W1', N'One', 1), (N'W2', N'Two', 1);
            INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive) SELECT N'P1', N'Product one', Id, 0, 1 FROM Uom;
            INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive) SELECT N'P2', N'Product two', Id, 0, 1 FROM Uom;
            """);
        await database.ExecuteAsync("""
            DECLARE @u1 int = (SELECT Id FROM [User] WHERE Username = N'first'), @w1 int = (SELECT Id FROM Warehouse WHERE Code = N'W1');
            INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval) VALUES
                (1, N'RCV-W1-0001', 3, @w1, @u1, SYSUTCDATETIME(), 0),
                (2, N'ISS-W1-0001', 4, @w1, @u1, SYSUTCDATETIME(), 0);   -- 4 = Reversed
            """);
        await database.ExecuteAsync("""
            DECLARE @u1 int = (SELECT Id FROM [User] WHERE Username = N'first'), @u2 int = (SELECT Id FROM [User] WHERE Username = N'second');
            DECLARE @w1 int = (SELECT Id FROM Warehouse WHERE Code = N'W1'), @w2 int = (SELECT Id FROM Warehouse WHERE Code = N'W2');
            DECLARE @p1 int = (SELECT Id FROM Product WHERE Sku = N'P1'), @p2 int = (SELECT Id FROM Product WHERE Sku = N'P2');
            DECLARE @rcv int = (SELECT Id FROM StockDocument WHERE No = N'RCV-W1-0001'), @iss int = (SELECT Id FROM StockDocument WHERE No = N'ISS-W1-0001');
            INSERT StockLedgerEntry (ProductId, WarehouseId, QtyDelta, UnitCost, SourceDocId, Reason, OccurredAt, CreatedBy) VALUES
                (@p1, @w1, 10, 1, @rcv, 1, SYSUTCDATETIME(), @u1),
                (@p2, @w1,  5, 1, @rcv, 1, SYSUTCDATETIME(), @u1),
                (@p1, @w2,  3, 1, @rcv, 1, SYSUTCDATETIME(), @u1),
                (@p1, @w1,  4, 1, @rcv, 1, SYSUTCDATETIME(), @u1),
                (@p1, @w1, -6, 1, @iss, 2, SYSUTCDATETIME(), @u1),
                (@p1, @w1,  6, 1, @iss, 6, SYSUTCDATETIME(), @u2);   -- 6 = Reversal
            """);
    }

    private static async Task AssertBackfilledAsync(TestDatabase database)
    {
        // Ledger rows in id order, with the balance of their own (product, warehouse) after each.
        var balances = new List<decimal>();
        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT BalanceAfter FROM StockLedgerEntry ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) balances.Add(reader.GetDecimal(0));
        }
        Assert.Equal([10m, 5m, 3m, 14m, 8m, 14m], balances);

        // The last row of each pair is the stock balance's quantity once the stock balance is built from the ledger.
        Assert.Equal(14m, await database.ScalarAsync<decimal>("SELECT SUM(QtyDelta) FROM StockLedgerEntry WHERE ProductId = (SELECT Id FROM Product WHERE Sku = N'P1') AND WarehouseId = (SELECT Id FROM Warehouse WHERE Code = N'W1')"));

        // Who reversed the reversed document: the creator of its reversal entry. Documents that were never reversed stay empty.
        var second = await database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = N'second'");
        Assert.Equal(second, await database.ScalarAsync<int>("SELECT ReversedBy FROM StockDocument WHERE No = N'ISS-W1-0001'"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE No = N'RCV-W1-0001' AND ReversedBy IS NOT NULL"));

        // The ledger is append-only again: the trigger the backfill switched off is on.
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT is_disabled FROM sys.triggers WHERE name = N'TR_StockLedgerEntry_AppendOnly'"));
        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteAsync("UPDATE StockLedgerEntry SET BalanceAfter = 0"));
    }

    [Fact]
    public async Task The_migrations_backfill_old_rows_and_go_down_and_up_again()
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, BeforeReversedBy);
            await SeedOldDataAsync(database);

            await MigrateAsync(database, Latest);
            await AssertBackfilledAsync(database);

            await MigrateAsync(database, BeforeReversedBy); // down: both columns go, the data stays
            Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE name IN (N'BalanceAfter', N'ReversedBy')"));
            Assert.Equal(6, await database.ScalarAsync<int>("SELECT COUNT(*) FROM StockLedgerEntry"));

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
