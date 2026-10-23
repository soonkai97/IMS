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
/// Migration <c>AddSalesOrders</c> (PL3.2): two NEW tables, empty. Run against a database that already holds rows: Up creates the tables and touches no row of any other table, Down refuses
/// while any order exists (live or soft-deleted) and drops both tables when none does, and Up, Down, Up works, as the runner and as the script a DBA generates. Each test has its own
/// throw-away database.
/// </summary>
public class AddSalesOrdersMigrationTests : IAsyncLifetime
{
    private const string Before = "20261023100000_GrantSalesPermissions";
    private const string Latest = "20261023110000_AddSalesOrders";
    private const int Products = 20_000;
    private const int Partners = 5_000;

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
            INSERT Warehouse (Code, Name, IsActive) VALUES (N'W1', N'One', 1);
            INSERT [User] (Username, Email, DisplayName, PasswordHash, RoleId, AllWarehouses, IsActive, FailedLoginCount)
            VALUES (N'first', N'first@example.test', N'First', N'x', (SELECT TOP (1) Id FROM Role), 1, 1, 0);
            UPDATE Product SET REC_DELETED = 1 WHERE Sku = N'SKU-1';
            UPDATE Partner SET REC_DELETED = 1 WHERE Name = N'Partner 1';
            """);
    }

    private Task<int> InsertOrderAsync(string no, bool softDeleted = false) => _db.ExecuteAsync("""
        INSERT SalesOrder (No, Status, WarehouseId, CustomerId, CreatedBy, CreatedAt, RequiresApproval, IsLarge, REC_DELETED)
        SELECT @no, 0, (SELECT TOP (1) Id FROM Warehouse), (SELECT TOP (1) Id FROM Partner WHERE REC_DELETED = 0), (SELECT TOP (1) Id FROM [User]), SYSUTCDATETIME(), 0, 0, @deleted
        """, ("@no", no), ("@deleted", softDeleted));

    private Task<int> TableCountAsync() => _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name IN (N'SalesOrder', N'SalesOrderLine')");

    private Task<int> MigrationRowAsync() => _db.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest));

    private async Task<(int Products, int Partners, long Versions)> OtherRowsAsync() =>
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Product"), await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Partner"),
         await _db.ScalarAsync<long>("SELECT SUM(CAST(CAST(ROW_VERSION AS bigint) % 1000003 AS bigint)) FROM (SELECT ROW_VERSION FROM Product UNION ALL SELECT ROW_VERSION FROM Partner) v"));

    // ----- shape -----
    [Fact]
    public async Task A_fresh_database_has_the_two_tables_with_the_money_and_quantity_types_the_lengths_and_the_filtered_unique_number()
    {
        await MigrateAsync(Latest);

        Assert.Equal(2, await TableCountAsync());
        Assert.Equal("decimal(18,2)", await ColumnTypeAsync("SalesOrderLine", "SellingPrice"));
        Assert.Equal("decimal(18,2)", await ColumnTypeAsync("SalesOrderLine", "SellingAmount"));
        Assert.Equal("decimal(18,4)", await ColumnTypeAsync("SalesOrderLine", "Qty"));
        Assert.Equal("decimal(18,4)", await ColumnTypeAsync("SalesOrderLine", "BaseQty"));
        Assert.Equal("nvarchar(40)", await ColumnTypeAsync("SalesOrder", "No"));
        Assert.Equal("nvarchar(500)", await ColumnTypeAsync("SalesOrder", "Remark"));
        Assert.Equal("nvarchar(500)", await ColumnTypeAsync("SalesOrder", "RejectionReason"));
        Assert.Equal("nvarchar(500)", await ColumnTypeAsync("SalesOrder", "CancelReason"));
        Assert.Equal("nvarchar(500)", await ColumnTypeAsync("SalesOrder", "CloseReason"));
        Assert.Equal("timestamp", await ColumnTypeAsync("SalesOrder", "ROW_VERSION"));
        Assert.Equal("timestamp", await ColumnTypeAsync("SalesOrderLine", "ROW_VERSION"));
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'SalesOrder') AND name = N'IX_SalesOrder_No' AND is_unique = 1 AND filter_definition = N'([REC_DELETED]=(0))'"));
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'SalesOrder') AND name = N'UX_SalesOrder_PublicId' AND is_unique = 1 AND filter_definition IS NULL"));
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder"));
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrderLine"));
    }

    private async Task<string> ColumnTypeAsync(string table, string column) => await _db.ScalarAsync<string>(
        "SELECT CASE WHEN ty.name IN (N'decimal') THEN CONCAT(ty.name, N'(', c.precision, N',', c.scale, N')') WHEN ty.name = N'nvarchar' THEN CONCAT(ty.name, N'(', c.max_length / 2, N')') ELSE ty.name END " +
        "FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c", ("@t", table), ("@c", column));

    [Fact]
    public async Task A_fresh_database_and_an_upgraded_one_have_the_same_tables_columns_and_indexes()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        var fresh = TestDatabase.CreateNew();
        try
        {
            await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fresh.ConnectionString).Options, new FrozenClock()))
                await db.GetService<IMigrator>().MigrateAsync(Latest);

            const string columns = "SELECT STRING_AGG(CONCAT(t.name, N'.', c.name, N':', ty.name, N'(', c.max_length, N',', c.precision, N',', c.scale, N')', c.is_nullable), N';') WITHIN GROUP (ORDER BY t.name, c.name) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE t.name IN (N'SalesOrder', N'SalesOrderLine')";
            const string indexes = "SELECT STRING_AGG(CONCAT(t.name, N'.', i.name, N':', i.is_unique, ISNULL(i.filter_definition, N'')), N';') WITHIN GROUP (ORDER BY t.name, i.name) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE t.name IN (N'SalesOrder', N'SalesOrderLine') AND i.name IS NOT NULL";
            Assert.Equal(await fresh.ScalarAsync<string>(columns), await _db.ScalarAsync<string>(columns));
            Assert.Equal(await fresh.ScalarAsync<string>(indexes), await _db.ScalarAsync<string>(indexes));
        }
        finally
        {
            await fresh.DropAsync();
        }
    }

    // ----- new tables only -----
    [Fact]
    public void Up_creates_two_tables_and_their_indexes_and_alters_nothing_that_exists()
    {
        var up = new AddSalesOrders().UpOperations;

        Assert.Equal(["SalesOrder", "SalesOrderLine"], up.OfType<CreateTableOperation>().Select(t => t.Name).Order());
        Assert.All(up, op => Assert.True(op is CreateTableOperation or CreateIndexOperation, $"{op.GetType().Name} touches something that exists"));
        Assert.All(up.OfType<CreateIndexOperation>(), i => Assert.StartsWith("SalesOrder", i.Table));
    }

    [Fact]
    public async Task The_generated_up_script_creates_the_tables_and_touches_no_row_of_any_other_table()
    {
        await using var db = NewContext();

        var script = db.GetService<IMigrator>().GenerateScript(Before, Latest);

        Assert.Equal(2, Regex.Matches(script, @"CREATE TABLE \[SalesOrder(Line)?\]").Count);
        var touch = Regex.Match(script, @"(?i)\bUPDATE\b|\bDELETE FROM\b|ALTER TABLE \[(?!SalesOrder)|INSERT INTO \[(?!__EFMigrationsHistory)");
        Assert.False(touch.Success, $"The script touches existing data: {touch.Value}");
    }

    [Fact]
    public async Task On_a_database_with_many_rows_the_up_touches_none_of_them_and_the_new_tables_are_empty()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        var rows = await OtherRowsAsync();

        await MigrateAsync(Latest);

        Assert.Equal(rows, await OtherRowsAsync()); // same counts and not one row version moved
        Assert.Equal(2, await TableCountAsync());
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder"));
    }

    // ----- down and up again -----
    [Fact]
    public async Task Up_down_up_on_a_database_with_rows_keeps_every_row_of_the_other_tables()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        var rows = await OtherRowsAsync();

        await MigrateAsync(Latest);
        await MigrateAsync(Before);
        Assert.Equal(0, await TableCountAsync());
        Assert.Equal(0, await MigrationRowAsync());
        Assert.Equal(rows, await OtherRowsAsync());

        await MigrateAsync(Latest);
        Assert.Equal(2, await TableCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
        Assert.Equal(rows, await OtherRowsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // a soft-deleted order is still a row
    public async Task Down_refuses_while_any_order_exists_and_changes_nothing(bool softDeleted)
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        await InsertOrderAsync("SO-W1-2610-0001", softDeleted);

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        Assert.Contains("Sales orders exist", Messages(refused));
        Assert.Equal(2, await TableCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder"));
    }

    [Fact]
    public async Task Down_works_once_the_orders_are_deleted_and_up_gives_back_empty_tables()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        await InsertOrderAsync("SO-W1-2610-0001");
        await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        await _db.ExecuteAsync("DELETE FROM SalesOrder");
        await MigrateAsync(Before);

        Assert.Equal(0, await TableCountAsync());
        await MigrateAsync(Latest);
        Assert.Equal(2, await TableCountAsync());
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder"));
    }

    [Fact]
    public async Task The_generated_script_does_what_the_runner_does_and_a_refused_down_drops_nothing_even_when_every_batch_runs()
    {
        await MigrateAsync(Before);
        await SeedAsync();

        await RunScriptAsync(Before, Latest);
        Assert.Equal(2, await TableCountAsync());

        await InsertOrderAsync("SO-W1-2610-0001");
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => RunScriptAsync(Latest, Before));
        Assert.Contains("Sales orders exist", Messages(refused));
        Assert.Equal(2, await TableCountAsync()); // the batches after the refusal did not drop them
        Assert.Equal(1, await MigrationRowAsync());   // nor did the history row go: the whole Down is one batch

        await _db.ExecuteAsync("DELETE FROM SalesOrder");
        await RunScriptAsync(Latest, Before);
        Assert.Equal(0, await TableCountAsync());
        await RunScriptAsync(Before, Latest);
        Assert.Equal(2, await TableCountAsync());
    }

    private static string Messages(Exception e) => e is SqlException ? e.Message : string.Join(" | ", Chain(e).Select(x => x.Message));

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (; e is not null; e = e.InnerException) yield return e;
    }
}
