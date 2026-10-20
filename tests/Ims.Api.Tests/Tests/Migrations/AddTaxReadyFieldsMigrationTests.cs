using System.Text.RegularExpressions;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Infrastructure.Persistence;
using Ims.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// Migration <c>AddTaxReadyFields</c> (PL1.5): four nullable columns with no default and no index, so the add is metadata-only. Run against a database that already holds many rows:
/// the add reads and writes no row (counted in the transaction log), the old rows read NULL, Down refuses while any row (live or soft-deleted) holds a value and drops the columns when
/// none does, and Up, Down, Up works, as the runner and as the script a DBA generates. Each test has its own throw-away database.
/// </summary>
public class AddTaxReadyFieldsMigrationTests : IAsyncLifetime
{
    private const string Before = "20261019145353_GrantAccountingExport";
    private const string Latest = "20261020022457_AddTaxReadyFields";
    private const int Products = 20_000;
    private const int Partners = 5_000;

    private static readonly (string Table, string Column, int Length)[] Columns =
        [("Product", "HsCode", 16), ("Product", "SstCode", 20), ("Partner", "Tin", 20), ("Partner", "SstRegNo", 30)];

    private readonly TestDatabase _db = TestDatabase.CreateNew();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _db.DropAsync();

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_db.ConnectionString).Options, new FrozenClock());

    private async Task MigrateAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>The script a DBA generates and runs by hand, one batch per GO, each batch on its own (so an error in one does not stop the next, as in SSMS).</summary>
    private async Task RunScriptAsync(string from, string to)
    {
        await using var db = NewContext();
        foreach (var batch in Regex.Split(db.GetService<IMigrator>().GenerateScript(from, to), @"^\s*GO\s*$", RegexOptions.Multiline))
            if (!string.IsNullOrWhiteSpace(batch)) await _db.ExecuteAsync(batch);
    }

    /// <summary>Many rows in the schema BEFORE the migration (set-based, so it takes a second), including one soft-deleted row of each table.</summary>
    private async Task SeedAsync()
    {
        await _db.ExecuteAsync("INSERT Uom (Code, Name) VALUES (N'PCS', N'Pieces')");
        await _db.ExecuteAsync($"""
            INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive)
            SELECT CONCAT(N'SKU-', n.Number), CONCAT(N'Product ', n.Number), (SELECT TOP (1) Id FROM Uom), 0, 1
            FROM (SELECT TOP ({Products}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Number FROM sys.all_columns a CROSS JOIN sys.all_columns b) n;
            INSERT Partner (Type, Name, IsActive)
            SELECT 1, CONCAT(N'Partner ', n.Number), 1
            FROM (SELECT TOP ({Partners}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Number FROM sys.all_columns a CROSS JOIN sys.all_columns b) n;
            UPDATE Product SET REC_DELETED = 1 WHERE Sku = N'SKU-1';
            UPDATE Partner SET REC_DELETED = 1 WHERE Name = N'Partner 1';
            """);
    }

    private Task<int> ColumnCountAsync() => _db.ScalarAsync<int>(
        "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE (t.name = N'Product' AND c.name IN (N'HsCode', N'SstCode')) OR (t.name = N'Partner' AND c.name IN (N'Tin', N'SstRegNo'))");

    private Task<int> MigrationRowAsync() => _db.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest));

    private async Task<string> LastLsnAsync() => await _db.ScalarAsync<string>("SELECT ISNULL(MAX([Current LSN]), N'') FROM sys.fn_dblog(NULL, NULL)");

    private Task<int> LogRecordsSinceAsync(string lsn) => _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.fn_dblog(NULL, NULL) WHERE [Current LSN] > @lsn", ("@lsn", lsn));

    // ----- shape -----
    [Fact]
    public async Task A_fresh_database_has_the_four_columns_nullable_without_a_default_and_without_an_index_on_them()
    {
        await MigrateAsync(Latest);

        foreach (var (table, column, length) in Columns)
        {
            Assert.Equal(1, await _db.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c AND c.is_nullable = 1 AND c.default_object_id = 0 AND ty.name = N'nvarchar' AND c.max_length = @bytes",
                ("@t", table), ("@c", column), ("@bytes", length * 2)));
            Assert.Equal(0, await _db.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = OBJECT_ID(@t) AND c.name = @c", ("@t", table), ("@c", column)));
        }
    }

    [Fact]
    public async Task A_fresh_database_and_an_upgraded_one_have_the_same_columns()
    {
        await MigrateAsync(Before);
        await MigrateAsync(Latest);
        var fresh = TestDatabase.CreateNew();
        try
        {
            await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fresh.ConnectionString).Options, new FrozenClock()))
                await db.GetService<IMigrator>().MigrateAsync(Latest);

            const string describe = "SELECT STRING_AGG(CONCAT(t.name, N'.', c.name, N':', ty.name, N'(', c.max_length, N')', c.is_nullable), N';') WITHIN GROUP (ORDER BY t.name, c.name) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE t.name IN (N'Product', N'Partner')";
            Assert.Equal(await fresh.ScalarAsync<string>(describe), await _db.ScalarAsync<string>(describe));
        }
        finally
        {
            await fresh.DropAsync();
        }
    }

    // ----- metadata only -----
    [Fact]
    public void Up_is_four_nullable_columns_and_nothing_else()
    {
        var up = new AddTaxReadyFields().UpOperations;

        Assert.Equal(4, up.Count);
        Assert.All(up, op =>
        {
            var add = Assert.IsType<AddColumnOperation>(op);
            Assert.True(add.IsNullable);
            Assert.Null(add.DefaultValue);
            Assert.Null(add.DefaultValueSql);
            Assert.Null(add.ComputedColumnSql);
            Assert.Equal("nvarchar", add.ColumnType![..8]);
        });
        Assert.Equal(Columns.Select(c => c.Column).Order(), up.Cast<AddColumnOperation>().Select(a => a.Name).Order());
    }

    [Fact]
    public async Task The_generated_up_script_adds_the_columns_and_touches_no_row()
    {
        await using var db = NewContext();

        var script = db.GetService<IMigrator>().GenerateScript(Before, Latest);

        Assert.Equal(4, Regex.Matches(script, @"ALTER TABLE \[(Product|Partner)\] ADD \[\w+\] nvarchar\(\d+\) NULL;").Count);
        Assert.DoesNotMatch(@"(?i)\bUPDATE\b|\bDEFAULT\b|\bINSERT INTO \[(Product|Partner)\]|CREATE\s+(UNIQUE\s+)?(NONCLUSTERED\s+)?INDEX|NOT NULL", script.Replace("INSERT INTO [__EFMigrationsHistory]", ""));
    }

    [Fact]
    public async Task On_a_database_with_many_rows_the_add_writes_a_handful_of_log_records_not_one_per_row()
    {
        await MigrateAsync(Before);
        await SeedAsync();

        // The control: touching every row of the two tables logs (at least) one record per row, so the counter would see a rewrite.
        var controlFrom = await LastLsnAsync();
        await _db.ExecuteAsync("UPDATE Product SET SafetyStock = 0; UPDATE Partner SET IsActive = IsActive;");
        var rewrite = await LogRecordsSinceAsync(controlFrom);
        Assert.True(rewrite >= Products + Partners, $"The control wrote only {rewrite} log records; the instrument cannot see a rewrite.");

        var from = await LastLsnAsync();
        await MigrateAsync(Latest);
        var added = await LogRecordsSinceAsync(from);

        Assert.True(added < 1_000, $"Adding the columns wrote {added} log records for {Products + Partners} rows: it is not metadata-only.");
        Assert.Equal(4, await ColumnCountAsync());
        Assert.Equal(0, await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM Product WHERE HsCode IS NOT NULL OR SstCode IS NOT NULL"));
        Assert.Equal(0, await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM Partner WHERE Tin IS NOT NULL OR SstRegNo IS NOT NULL"));
    }

    // ----- down and up again -----
    [Fact]
    public async Task Up_down_up_on_a_database_with_rows_keeps_every_row_and_leaves_the_columns_empty()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        var products = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Product");
        var partners = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Partner");

        await MigrateAsync(Latest);
        await MigrateAsync(Before);
        Assert.Equal(0, await ColumnCountAsync());
        Assert.Equal(0, await MigrationRowAsync());
        Assert.Equal((products, partners), (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Product"), await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Partner")));

        await MigrateAsync(Latest);
        Assert.Equal(4, await ColumnCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
        Assert.Equal((products, partners), (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Product"), await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Partner")));
    }

    [Theory]
    [InlineData("Product", "HsCode", false)]
    [InlineData("Product", "SstCode", false)]
    [InlineData("Partner", "Tin", false)]
    [InlineData("Partner", "SstRegNo", false)]
    [InlineData("Product", "HsCode", true)]  // a soft-deleted row still holds the value
    [InlineData("Partner", "Tin", true)]
    public async Task Down_refuses_while_any_row_holds_a_value_and_changes_nothing(string table, string column, bool onSoftDeletedRow)
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        var key = table == "Product" ? "Sku" : "Name";
        var row = table == "Product" ? "SKU-1" : "Partner 1";
        if (!onSoftDeletedRow) row = table == "Product" ? "SKU-2" : "Partner 2";
        await _db.ExecuteAsync($"UPDATE {table} SET {column} = N'123' WHERE {key} = @row", ("@row", row));

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        Assert.Contains("Tax-ready fields hold values", Messages(refused));
        Assert.Equal(4, await ColumnCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
        Assert.Equal("123", await _db.ScalarAsync<string>($"SELECT {column} FROM {table} WHERE {key} = @row", ("@row", row)));
    }

    [Fact]
    public async Task Down_works_once_the_values_are_cleared_and_up_gives_back_empty_columns()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        await _db.ExecuteAsync("UPDATE Product SET HsCode = N'8471.30', SstCode = N'ST-01' WHERE Sku = N'SKU-2'; UPDATE Partner SET Tin = N'C25', SstRegNo = N'W10' WHERE Name = N'Partner 2'");
        await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        await _db.ExecuteAsync("UPDATE Product SET HsCode = NULL, SstCode = NULL; UPDATE Partner SET Tin = NULL, SstRegNo = NULL");
        await MigrateAsync(Before);

        Assert.Equal(0, await ColumnCountAsync());
        await MigrateAsync(Latest);
        Assert.Equal(4, await ColumnCountAsync());
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Product WHERE HsCode IS NOT NULL OR SstCode IS NOT NULL"));
    }

    [Fact]
    public async Task The_generated_script_does_what_the_runner_does_and_a_refused_down_drops_nothing_even_when_every_batch_runs()
    {
        await MigrateAsync(Before);
        await SeedAsync();

        await RunScriptAsync(Before, Latest);
        Assert.Equal(4, await ColumnCountAsync());

        await _db.ExecuteAsync("UPDATE Partner SET Tin = N'C25' WHERE Name = N'Partner 2'");
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => RunScriptAsync(Latest, Before));
        Assert.Contains("Tax-ready fields hold values", Messages(refused));
        Assert.Equal(4, await ColumnCountAsync()); // the batches after the refusal did not drop them
        Assert.Equal(1, await MigrationRowAsync());   // nor did the history row go: the whole Down is one batch, so the refusal undoes all of it

        await _db.ExecuteAsync("UPDATE Partner SET Tin = NULL");
        await RunScriptAsync(Latest, Before);
        Assert.Equal(0, await ColumnCountAsync());
        await RunScriptAsync(Before, Latest);
        Assert.Equal(4, await ColumnCountAsync());
    }

    private static string Messages(Exception e) => e is SqlException ? e.Message : string.Join(" | ", Chain(e).Select(x => x.Message));

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (; e is not null; e = e.InnerException) yield return e;
    }
}
