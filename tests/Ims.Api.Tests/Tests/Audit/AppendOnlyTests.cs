using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Tests.Audit;

/// <summary>BR-01 / AUTH-03: even someone connected straight to the database cannot change or delete the ledger or the audit log.</summary>
[Collection(ApiCollection.Name)]
public class AppendOnlyTests(ApiFixture fixture)
{
    private const int LedgerTriggerError = 51000;
    private const int AuditTriggerError = 51001;

    private async Task<int> ProductWithLedgerEntryAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 5, 2m));
        return product.Id;
    }

    [Fact]
    public async Task Updating_a_ledger_entry_with_sql_is_refused_and_the_row_is_unchanged()
    {
        var productId = await ProductWithLedgerEntryAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() =>
            fixture.Database.ExecuteAsync("UPDATE StockLedgerEntry SET QtyDelta = 999 WHERE ProductId = @p", ("@p", productId)));

        Assert.Equal(LedgerTriggerError, error.Number);
        Assert.Contains("append-only", error.Message);
        Assert.Equal(5m, await fixture.Database.ScalarAsync<decimal>("SELECT QtyDelta FROM StockLedgerEntry WHERE ProductId = @p", ("@p", productId)));
    }

    [Fact]
    public async Task Deleting_a_ledger_entry_with_sql_is_refused_and_the_row_stays()
    {
        var productId = await ProductWithLedgerEntryAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() =>
            fixture.Database.ExecuteAsync("DELETE FROM StockLedgerEntry WHERE ProductId = @p", ("@p", productId)));

        Assert.Equal(LedgerTriggerError, error.Number);
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockLedgerEntry WHERE ProductId = @p", ("@p", productId)));
    }

    [Fact]
    public async Task A_bulk_update_or_delete_of_the_whole_ledger_is_refused_too()
    {
        await ProductWithLedgerEntryAsync();
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockLedgerEntry");

        await Assert.ThrowsAsync<SqlException>(() => fixture.Database.ExecuteAsync("UPDATE StockLedgerEntry SET UnitCost = 0"));
        await Assert.ThrowsAsync<SqlException>(() => fixture.Database.ExecuteAsync("DELETE FROM StockLedgerEntry"));

        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockLedgerEntry"));
    }

    [Fact]
    public async Task Updating_or_deleting_an_audit_entry_with_sql_is_refused()
    {
        await ProductWithLedgerEntryAsync(); // guarantees audit rows exist
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs");
        Assert.True(before > 0);

        var update = await Assert.ThrowsAsync<SqlException>(() => fixture.Database.ExecuteAsync("UPDATE AuditLogs SET Actor = 'someone-else'"));
        var delete = await Assert.ThrowsAsync<SqlException>(() => fixture.Database.ExecuteAsync("DELETE FROM AuditLogs"));

        Assert.Equal(AuditTriggerError, update.Number);
        Assert.Equal(AuditTriggerError, delete.Number);
        Assert.Contains("append-only", update.Message);
        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Actor = 'someone-else'"));
    }

    [Fact]
    public async Task Adding_rows_is_still_possible_so_the_triggers_only_block_changes()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs");

        await s.NewProductAsync(); // a master-data change writes audit rows

        Assert.True(await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs") > before);
    }

    [Fact]
    public async Task The_database_runs_with_read_committed_snapshot_so_readers_never_block_writers()
    {
        var enabled = await fixture.Database.ScalarAsync<int>("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()");

        Assert.Equal(1, enabled);
    }
}
