using Ims.Api.Tests.Harness;
using Ims.Infrastructure.Persistence;
using Ims.Infrastructure.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// The backfill of <c>StoredLineAmount</c> runs on rows of every kind: it executes the real Up SQL on a database that holds open and posted documents, with
/// typed costs, a unit-of-measure conversion, a sub-cent cost and an issue, and checks what each line becomes.
/// </summary>
public class StoredLineAmountMigrationTests : IAsyncLifetime
{
    private const string Migration = "StoredLineAmount";
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

    private async Task DocumentAsync(string no, int type, int status) => await _db.ExecuteAsync(
        "INSERT [StockDocument] ([Type], [No], [Status], WarehouseId, CreatedBy, CreatedAt, RequiresApproval, REC_DELETED) VALUES (@type, @no, @status, 1, 1, SYSUTCDATETIME(), 0, 0)",
        ("@type", type), ("@no", no), ("@status", status));

    private async Task LineAsync(string no, string qty, string baseQty, string unitCost) => await _db.ExecuteAsync(
        $"INSERT [StockDocumentLine] (DocumentId, ProductId, Qty, UomId, BaseQty, UnitCost, REC_DELETED) VALUES ((SELECT Id FROM StockDocument WHERE [No] = @no), 1, {qty}, 1, {baseQty}, {unitCost}, 0)", ("@no", no));

    private async Task<(decimal Entered, decimal Amount, bool AtAverage)> ReadAsync(string no, string baseQty)
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT l.EnteredUnitCost, l.Amount, l.CostFromAverage FROM StockDocumentLine l JOIN StockDocument d ON d.Id = l.DocumentId WHERE d.[No] = @no AND l.BaseQty = " + baseQty;
        command.Parameters.AddWithValue("@no", no);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no line {no} / {baseQty}");
        return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetBoolean(2));
    }

    [Fact]
    public async Task The_backfill_gives_every_kind_of_existing_line_its_entered_cost_amount_and_flag()
    {
        string before;
        await using (var context = NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            before = all[all.FindIndex(m => m.EndsWith("_" + Migration, StringComparison.Ordinal)) - 1];
        }
        await MigrateToAsync(before);
        foreach (var table in new[] { "StockDocument", "StockDocumentLine" })
            await _db.ExecuteAsync($"ALTER TABLE [{table}] NOCHECK CONSTRAINT ALL");
        await DocumentAsync("T-1", type: 1, status: 0);   // an open receipt
        await DocumentAsync("T-2", type: 2, status: 0);   // an open issue
        await DocumentAsync("T-3", type: 1, status: 3);   // a posted receipt
        await LineAsync("T-1", "3", "3", "4.5");                         // a typed cost in the base unit
        await LineAsync("T-1", "100000", "700000", "1.428571");          // 100,000 cartons of 7 at 10.00: per piece 1.428571
        await LineAsync("T-1", "1000", "1001", "0.003");                 // a sub-cent cost per entered unit (base 1001 only to tell the line apart)
        await LineAsync("T-2", "2", "24", "1.255");                      // an issue of 2 cartons of 12, valued at the average
        await LineAsync("T-3", "100000", "700000", "1.428571");          // the same carton line, posted

        await MigrateToAsync("");

        Assert.Equal((4.50m, 13.50m, false), await ReadAsync("T-1", "3"));                // open, typed: round(3 x 4.50)
        Assert.Equal((10.00m, 1_000_000.00m, false), await ReadAsync("T-1", "700000"));   // open, typed: round(100,000 x 10.00), not 999,999.70
        var subCent = await ReadAsync("T-1", "1001");
        Assert.True(subCent.AtAverage);                                                    // 0.003 per entered unit cannot be a typed 2-decimal cost: kept at its exact price
        Assert.Equal(0.00m, subCent.Entered);
        Assert.Equal(3.00m, subCent.Amount);                                               // round(1,001 x 0.003): the exact price, not 0 x 1,000
        Assert.Equal((15.06m, 30.12m, true), await ReadAsync("T-2", "24"));               // an issue: always at the average, round(24 x 1.255)
        Assert.Equal((10.00m, 999_999.70m, false), await ReadAsync("T-3", "700000"));     // posted: keeps what it showed, no posted total moves
    }
}
