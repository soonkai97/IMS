using Ims.Api.Tests.Harness;
using Ims.Infrastructure.Persistence;
using Ims.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// The cost-scale migration goes up, down and up again on a real database that holds rows, without losing a value that fits and without leaving a column of the
/// wrong type. (How long it takes on a million ledger rows is measured by hand and recorded in docs/architecture/backend.md.)
/// </summary>
public class AverageCostMigrationTests : IAsyncLifetime
{
    private const string Migration = "AverageCostMarkersAndCostScale";
    private readonly TestDatabase _db = TestDatabase.CreateNew();

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _db.DropAsync();

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(_db.ConnectionString, o => o.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)).Options, new SystemClock());

    private async Task MigrateToAsync(string target)
    {
        await using var context = NewContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static string Id(AppDbContext context, int offset)
    {
        var all = context.Database.GetMigrations().ToList();
        return all[all.FindIndex(m => m.EndsWith("_" + Migration, StringComparison.Ordinal)) + offset];
    }

    private async Task<string> ColumnAsync(string table, string column) => await _db.ScalarAsync<string>(
        "SELECT CONCAT(DATA_TYPE, '(', NUMERIC_PRECISION, ',', NUMERIC_SCALE, ')') FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t AND COLUMN_NAME = @c", ("@t", table), ("@c", column));

    private async Task<bool> HasColumnAsync(string table, string column) =>
        await _db.ScalarAsync<int>("SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t AND COLUMN_NAME = @c", ("@t", table), ("@c", column)) == 1;

    private async Task InsertRowsAsync(string avgCost, string lineCost, string ledgerCost)
    {
        // The tables point at products, warehouses and documents that this test does not need: the constraints are switched off for the three tables.
        foreach (var table in new[] { "StockBalance", "StockDocumentLine", "StockLedgerEntry" })
            await _db.ExecuteAsync($"ALTER TABLE [{table}] NOCHECK CONSTRAINT ALL");
        await _db.ExecuteAsync($"INSERT [StockBalance] (ProductId, WarehouseId, QtyOnHand, QtyInTransit, AvgCost, REC_DELETED) VALUES (1, 1, 10, 0, {avgCost}, 0)");
        await _db.ExecuteAsync($"INSERT [StockDocumentLine] (DocumentId, ProductId, Qty, UomId, BaseQty, UnitCost, REC_DELETED) VALUES (1, 1, 10, 1, 10, {lineCost}, 0)");
        await _db.ExecuteAsync($"INSERT [StockLedgerEntry] (ProductId, WarehouseId, QtyDelta, UnitCost, SourceDocId, Reason, OccurredAt, CreatedBy, REC_DELETED) VALUES (1, 1, 10, {ledgerCost}, 1, 1, SYSUTCDATETIME(), 1, 0)");
    }

    [Fact]
    public async Task Up_down_and_up_again_keep_the_rows_and_leave_the_right_column_types()
    {
        string before;
        await using (var context = NewContext()) before = Id(context, -1);
        await MigrateToAsync(before);
        await InsertRowsAsync("123456789012.123456", "999999999999.999999", "0.000001"); // the largest and the smallest value the old columns hold
        Assert.Equal("decimal(18,6)", await ColumnAsync("StockBalance", "AvgCost"));
        Assert.False(await HasColumnAsync("StockBalance", "LastReceiptLineId"));

        await MigrateToAsync("");                                                        // up to the latest
        Assert.Equal("decimal(22,10)", await ColumnAsync("StockBalance", "AvgCost"));
        Assert.Equal("decimal(22,10)", await ColumnAsync("StockDocumentLine", "UnitCost"));
        Assert.Equal("decimal(22,10)", await ColumnAsync("StockLedgerEntry", "UnitCost"));
        Assert.Equal("decimal(22,10)", await ColumnAsync("StockDocumentLine", "AvgCostBefore"));
        Assert.Equal("decimal(22,10)", await ColumnAsync("StockDocumentLine", "AvgCostAfter"));
        Assert.True(await HasColumnAsync("StockDocumentLine", "PreviousLineId"));
        Assert.True(await HasColumnAsync("StockBalance", "LastReceiptLineId"));
        Assert.Equal(123456789012.123456m, await _db.ScalarAsync<decimal>("SELECT AvgCost FROM StockBalance"));
        Assert.Equal(999999999999.999999m, await _db.ScalarAsync<decimal>("SELECT UnitCost FROM StockDocumentLine"));
        Assert.Equal(0.000001m, await _db.ScalarAsync<decimal>("SELECT UnitCost FROM StockLedgerEntry"));

        await _db.ExecuteAsync("UPDATE StockBalance SET AvgCost = 1.1234567895, LastReceiptLineId = 7");   // ten places and a marker: only the first survives a Down
        await MigrateToAsync(before);                                                    // down
        Assert.Equal("decimal(18,6)", await ColumnAsync("StockBalance", "AvgCost"));
        Assert.False(await HasColumnAsync("StockDocumentLine", "PreviousLineId"));
        Assert.False(await HasColumnAsync("StockBalance", "LastReceiptLineId"));
        Assert.Equal(1.123457m, await _db.ScalarAsync<decimal>("SELECT AvgCost FROM StockBalance")); // rounded, not lost
        Assert.Equal(999999999999.999999m, await _db.ScalarAsync<decimal>("SELECT UnitCost FROM StockDocumentLine"));

        await MigrateToAsync("");                                                        // and up again
        Assert.Equal("decimal(22,10)", await ColumnAsync("StockLedgerEntry", "UnitCost"));
        Assert.Equal(1.123457m, await _db.ScalarAsync<decimal>("SELECT AvgCost FROM StockBalance"));
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockLedgerEntry"));
    }
}
