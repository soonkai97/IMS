using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// The migration that adds the balance after each ledger row, run against a database that already holds ledger rows: the backfill must give old rows the value
/// new rows get, the append-only trigger must be back on afterwards, and the migration must go down and up again. The test has its own throw-away database.
/// </summary>
public class LedgerBalanceMigrationTests
{
    private const string Before = "20261010145459_AddDocumentReversedAt";
    private const string Latest = "20261011065810_AddLedgerBalanceAfter";

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
    /// Interleaved movements of two products and two warehouses, written in the schema BEFORE the migration. Ledger ids ascend in the order below:
    /// p1/w1 +10, p2/w1 +5, p1/w2 +3, p1/w1 +4, p1/w1 -6, p1/w1 +6.
    /// </summary>
    private static async Task SeedOldDataAsync(TestDatabase database)
    {
        await database.ExecuteAsync("""
            INSERT [User] (Username, Email, DisplayName, PasswordHash, Role, IsActive, FailedLoginCount) VALUES (N'first', N'first@example.test', N'First', N'x', N'Admin', 1, 0);
            INSERT Uom (Code, Name) VALUES (N'PCS', N'Pieces');
            INSERT Warehouse (Code, Name, IsActive) VALUES (N'W1', N'One', 1), (N'W2', N'Two', 1);
            INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive) SELECT N'P1', N'Product one', Id, 0, 1 FROM Uom;
            INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive) SELECT N'P2', N'Product two', Id, 0, 1 FROM Uom;
            """);
        await database.ExecuteAsync("""
            DECLARE @u1 int = (SELECT Id FROM [User] WHERE Username = N'first'), @w1 int = (SELECT Id FROM Warehouse WHERE Code = N'W1');
            INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval) VALUES (1, N'RCV-W1-0001', 3, @w1, @u1, SYSUTCDATETIME(), 0);
            """);
        await database.ExecuteAsync("""
            DECLARE @u1 int = (SELECT Id FROM [User] WHERE Username = N'first');
            DECLARE @w1 int = (SELECT Id FROM Warehouse WHERE Code = N'W1'), @w2 int = (SELECT Id FROM Warehouse WHERE Code = N'W2');
            DECLARE @p1 int = (SELECT Id FROM Product WHERE Sku = N'P1'), @p2 int = (SELECT Id FROM Product WHERE Sku = N'P2');
            DECLARE @doc int = (SELECT Id FROM StockDocument WHERE No = N'RCV-W1-0001');
            INSERT StockLedgerEntry (ProductId, WarehouseId, QtyDelta, UnitCost, SourceDocId, Reason, OccurredAt, CreatedBy) VALUES
                (@p1, @w1, 10, 1, @doc, 1, SYSUTCDATETIME(), @u1),
                (@p2, @w1,  5, 1, @doc, 1, SYSUTCDATETIME(), @u1),
                (@p1, @w2,  3, 1, @doc, 1, SYSUTCDATETIME(), @u1),
                (@p1, @w1,  4, 1, @doc, 1, SYSUTCDATETIME(), @u1),
                (@p1, @w1, -6, 1, @doc, 2, SYSUTCDATETIME(), @u1),
                (@p1, @w1,  6, 1, @doc, 6, SYSUTCDATETIME(), @u1);
            """);
    }

    private static async Task AssertBackfilledAsync(TestDatabase database)
    {
        var balances = new List<decimal>();
        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT BalanceAfter FROM StockLedgerEntry ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) balances.Add(reader.GetDecimal(0));
        }
        Assert.Equal([10m, 5m, 3m, 14m, 8m, 14m], balances); // each row: the running balance of ITS product and warehouse

        // The ledger is append-only again: the trigger the backfill switched off is on.
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT is_disabled FROM sys.triggers WHERE name = N'TR_StockLedgerEntry_AppendOnly'"));
        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteAsync("UPDATE StockLedgerEntry SET BalanceAfter = 0"));
    }

    [Fact]
    public async Task The_migration_backfills_the_running_balance_and_goes_down_and_up_again()
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, Before);
            await SeedOldDataAsync(database);

            await MigrateAsync(database, Latest);
            await AssertBackfilledAsync(database);

            await MigrateAsync(database, Before); // down: the column goes, the rows stay
            Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE name = N'BalanceAfter'"));
            Assert.Equal(6, await database.ScalarAsync<int>("SELECT COUNT(*) FROM StockLedgerEntry"));

            await MigrateAsync(database, Latest); // and up again: the backfill gives the same answer
            await AssertBackfilledAsync(database);
        }
        finally
        {
            await database.DropAsync();
        }
    }
}
