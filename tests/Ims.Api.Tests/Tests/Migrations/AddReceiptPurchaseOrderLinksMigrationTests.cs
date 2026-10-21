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
/// Migration <c>AddReceiptPurchaseOrderLinks</c> (PL2.4, MG13): two nullable columns with no default on the two BIG document tables, one filtered index and one foreign key. Run against a database that
/// already holds documents and lines: Up adds the columns without writing a row (no row version moves, every row reads NULL), Down refuses while any document or line carries a link (live or
/// soft-deleted) and drops everything when none does, and Up, Down, Up works, as the runner and as the script a DBA generates. Each test has its own throw-away database.
/// </summary>
public class AddReceiptPurchaseOrderLinksMigrationTests : IAsyncLifetime
{
    private const string Before = "20261021090000_AddPurchaseOrderApprovalRules";
    private const string Latest = "20261021100000_AddReceiptPurchaseOrderLinks";
    private const int Documents = 20_000;
    private const string LinkedNo = "RCV-W1-7";

    private readonly TestDatabase _db = TestDatabase.CreateNew();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _db.DropAsync();

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_db.ConnectionString).Options, new FrozenClock());

    private async Task MigrateAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>
    /// The script a DBA generates and runs by hand, one batch per GO, each batch on its own connection state. By default it STOPS at the first error (sqlcmd -b). With
    /// <paramref name="continueAfterError"/> it goes on with the next batch as SSMS and sqlcmd without -b do, and returns the errors it met. <paramref name="sessionPrefix"/> is put in front of
    /// every batch (a client whose session settings differ, for example <c>SET QUOTED_IDENTIFIER OFF</c> as sqlcmd has them).
    /// </summary>
    private async Task<List<Exception>> RunScriptAsync(string from, string to, bool continueAfterError = false, string sessionPrefix = "")
    {
        var errors = new List<Exception>();
        await using var db = NewContext();
        foreach (var batch in Regex.Split(db.GetService<IMigrator>().GenerateScript(from, to), @"^\s*GO\s*$", RegexOptions.Multiline))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            try
            {
                await _db.ExecuteAsync(sessionPrefix + batch);
            }
            catch (SqlException e) when (continueAfterError)
            {
                errors.Add(e);
            }
        }
        return errors;
    }

    /// <summary>Documents and lines in the schema BEFORE the migration (set-based), one soft-deleted document among them.</summary>
    private async Task SeedAsync()
    {
        await _db.ExecuteAsync("INSERT Uom (Code, Name) VALUES (N'PCS', N'Pieces')");
        await _db.ExecuteAsync($"""
            INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive) VALUES (N'SKU-1', N'Product 1', (SELECT TOP (1) Id FROM Uom), 0, 1);
            INSERT Partner (Type, Name, IsActive) VALUES (1, N'Supplier', 1);
            INSERT Warehouse (Code, Name, IsActive) VALUES (N'W1', N'One', 1);
            INSERT [User] (Username, Email, DisplayName, PasswordHash, RoleId, AllWarehouses, IsActive, FailedLoginCount)
            VALUES (N'first', N'first@example.test', N'First', N'x', (SELECT TOP (1) Id FROM Role), 1, 1, 0);
            DECLARE @u int = (SELECT TOP (1) Id FROM [User]), @w int = (SELECT TOP (1) Id FROM Warehouse);
            INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval, IsLarge, RequiresOverride)
            SELECT 1, CONCAT(N'RCV-W1-', n.Number), 3, @w, @u, SYSUTCDATETIME(), 0, 0, 0
            FROM (SELECT TOP ({Documents}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Number FROM sys.all_columns a CROSS JOIN sys.all_columns b) n;
            INSERT StockDocumentLine (DocumentId, ProductId, Qty, UomId, BaseQty, UnitCost, EnteredUnitCost, Amount, CostFromAverage)
            SELECT d.Id, (SELECT TOP (1) Id FROM Product), 1, (SELECT TOP (1) Id FROM Uom), 1, 1, 1, 1, 0 FROM StockDocument d;
            UPDATE StockDocument SET REC_DELETED = 1 WHERE No = N'RCV-W1-1';
            """);
    }

    private Task<int> InsertOrderAsync() => _db.ExecuteAsync("""
        INSERT PurchaseOrder (No, Status, WarehouseId, SupplierId, CreatedBy, CreatedAt, RequiresApproval, IsLarge)
        SELECT N'PO-W1-2610-0001', 2, (SELECT TOP (1) Id FROM Warehouse), (SELECT TOP (1) Id FROM Partner), (SELECT TOP (1) Id FROM [User]), SYSUTCDATETIME(), 0, 0
        """);

    private Task<int> LinkDocumentAsync(string no) => _db.ExecuteAsync("UPDATE StockDocument SET PurchaseOrderId = (SELECT TOP (1) Id FROM PurchaseOrder) WHERE No = @no", ("@no", no));

    private Task<int> LinkLineOfAsync(string no) => _db.ExecuteAsync("UPDATE StockDocumentLine SET PurchaseOrderLineNo = 1 WHERE DocumentId = (SELECT Id FROM StockDocument WHERE No = @no)", ("@no", no));

    private Task<int> ColumnCountAsync() => _db.ScalarAsync<int>(
        "SELECT COUNT(*) FROM sys.columns WHERE (object_id = OBJECT_ID(N'StockDocument') AND name = N'PurchaseOrderId') OR (object_id = OBJECT_ID(N'StockDocumentLine') AND name = N'PurchaseOrderLineNo')");

    private Task<int> MigrationRowAsync() => _db.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest));

    /// <summary>The number of rows and a checksum of every row version of the two big tables: a rewrite of any row would move it.</summary>
    private async Task<(int Documents, int Lines, long Versions)> RowsAsync() =>
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument"), await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocumentLine"),
         await _db.ScalarAsync<long>("SELECT SUM(CAST(CAST(ROW_VERSION AS bigint) % 1000003 AS bigint)) FROM (SELECT ROW_VERSION FROM StockDocument UNION ALL SELECT ROW_VERSION FROM StockDocumentLine) v"));

    // ----- shape -----
    [Fact]
    public async Task A_fresh_database_has_both_columns_nullable_int_with_no_default_the_filtered_index_and_a_restricting_foreign_key()
    {
        await MigrateAsync(Latest);

        Assert.Equal(2, await ColumnCountAsync());
        const string column = "SELECT CONCAT(ty.name, N',', c.is_nullable, N',', c.default_object_id) FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c";
        Assert.Equal("int,1,0", await _db.ScalarAsync<string>(column, ("@t", "StockDocument"), ("@c", "PurchaseOrderId"))); // nullable, and default_object_id 0: no default
        Assert.Equal("int,1,0", await _db.ScalarAsync<string>(column, ("@t", "StockDocumentLine"), ("@c", "PurchaseOrderLineNo")));
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'StockDocument') AND name = N'IX_StockDocument_PurchaseOrderId' AND filter_definition = N'([PurchaseOrderId] IS NOT NULL)'"));
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'StockDocument') AND referenced_object_id = OBJECT_ID(N'PurchaseOrder') AND delete_referential_action = 0"));
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'StockDocumentLine') AND name LIKE N'%PurchaseOrder%'")); // the line is reached through its document
    }

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

            const string columns = "SELECT STRING_AGG(CAST(CONCAT(t.name, N'.', c.name, N':', ty.name, N'(', c.max_length, N',', c.precision, N',', c.scale, N')', c.is_nullable) AS nvarchar(max)), N';') WITHIN GROUP (ORDER BY t.name, c.name) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id";
            const string indexes = "SELECT STRING_AGG(CAST(CONCAT(t.name, N'.', i.name, N':', i.is_unique, ISNULL(i.filter_definition, N'')) AS nvarchar(max)), N';') WITHIN GROUP (ORDER BY t.name, i.name) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE i.name IS NOT NULL";
            const string keys = "SELECT STRING_AGG(CAST(CONCAT(f.name, N':', f.delete_referential_action) AS nvarchar(max)), N';') WITHIN GROUP (ORDER BY f.name) FROM sys.foreign_keys f";
            Assert.Equal(await fresh.ScalarAsync<string>(columns), await _db.ScalarAsync<string>(columns));
            Assert.Equal(await fresh.ScalarAsync<string>(indexes), await _db.ScalarAsync<string>(indexes));
            Assert.Equal(await fresh.ScalarAsync<string>(keys), await _db.ScalarAsync<string>(keys));
        }
        finally
        {
            await fresh.DropAsync();
        }
    }

    // ----- metadata only -----
    [Fact]
    public void Up_adds_two_nullable_columns_with_no_default_a_filtered_index_and_a_foreign_key_and_nothing_else()
    {
        var up = new AddReceiptPurchaseOrderLinks().UpOperations;

        var columns = up.OfType<AddColumnOperation>().ToList();
        Assert.Equal(["PurchaseOrderId", "PurchaseOrderLineNo"], columns.Select(c => c.Name).Order());
        Assert.All(columns, c => Assert.True(c.IsNullable && c.DefaultValue is null && c.DefaultValueSql is null && c.ComputedColumnSql is null, $"{c.Name} must be nullable with no default"));
        Assert.Contains("WHERE [PurchaseOrderId] IS NOT NULL", Assert.Single(up.OfType<SqlOperation>()).Sql); // the filtered index (dynamic SQL: see the migration)
        Assert.Equal(ReferentialAction.Restrict, Assert.Single(up.OfType<AddForeignKeyOperation>()).OnDelete);
        Assert.Equal(4, up.Count);
    }

    [Fact]
    public async Task The_generated_up_script_changes_the_schema_of_the_two_tables_and_touches_no_row()
    {
        await using var db = NewContext();

        var script = db.GetService<IMigrator>().GenerateScript(Before, Latest);

        var touch = Regex.Match(script, @"(?i)\bUPDATE\b|\bDELETE FROM\b|INSERT INTO \[(?!__EFMigrationsHistory)|ALTER TABLE \[(?!StockDocument(Line)?\])");
        Assert.False(touch.Success, $"The script touches something else: {touch.Value}");
    }

    [Fact]
    public async Task On_a_database_with_many_documents_the_up_writes_no_row_and_every_row_reads_null()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        var rows = await RowsAsync();

        await MigrateAsync(Latest);

        Assert.Equal(rows, await RowsAsync()); // same counts and not one row version moved
        Assert.Equal(2, await ColumnCountAsync());
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE PurchaseOrderId IS NOT NULL"));
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocumentLine WHERE PurchaseOrderLineNo IS NOT NULL"));
    }

    // ----- down and up again -----
    [Fact]
    public async Task Up_down_up_on_a_database_with_rows_keeps_every_row()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        var rows = await RowsAsync();

        await MigrateAsync(Latest);
        await MigrateAsync(Before);
        Assert.Equal(0, await ColumnCountAsync());
        Assert.Equal(0, await MigrationRowAsync());
        Assert.Equal(rows, await RowsAsync());

        await MigrateAsync(Latest);
        Assert.Equal(2, await ColumnCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
        Assert.Equal(rows, await RowsAsync());
    }

    [Theory]
    [InlineData("document", false)]
    [InlineData("document", true)] // a soft-deleted document still stores its link
    [InlineData("line", false)]
    public async Task Down_refuses_while_any_document_or_line_carries_a_link_and_changes_nothing(string carrier, bool softDeleted)
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        await InsertOrderAsync();
        if (softDeleted) await _db.ExecuteAsync("UPDATE StockDocument SET REC_DELETED = 1 WHERE No = @no", ("@no", LinkedNo));
        if (carrier == "document") await LinkDocumentAsync(LinkedNo); else await LinkLineOfAsync(LinkedNo);

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        Assert.Contains("Receipts are linked to purchase orders", Messages(refused));
        Assert.Equal(2, await ColumnCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
    }

    [Fact]
    public async Task Down_works_once_the_links_are_cleared_and_up_gives_back_empty_columns()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        await InsertOrderAsync();
        await LinkDocumentAsync(LinkedNo);
        await LinkLineOfAsync(LinkedNo);
        await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        await _db.ExecuteAsync("UPDATE StockDocumentLine SET PurchaseOrderLineNo = NULL; UPDATE StockDocument SET PurchaseOrderId = NULL");
        await MigrateAsync(Before);

        Assert.Equal(0, await ColumnCountAsync());
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'FK_StockDocument_PurchaseOrder_PurchaseOrderId'"));
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_StockDocument_PurchaseOrderId'"));
        await MigrateAsync(Latest);
        Assert.Equal(2, await ColumnCountAsync());
    }


    [Fact]
    public async Task The_generated_script_does_what_the_runner_does_and_a_refused_down_stops_at_the_first_error_when_the_client_stops_on_errors()
    {
        await MigrateAsync(Before);
        await SeedAsync();

        await RunScriptAsync(Before, Latest);
        Assert.Equal(2, await ColumnCountAsync());

        await InsertOrderAsync();
        await LinkDocumentAsync(LinkedNo);
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => RunScriptAsync(Latest, Before)); // stops at the first error, as sqlcmd -b does
        Assert.Contains("Receipts are linked to purchase orders", Messages(refused));
        Assert.Equal(2, await ColumnCountAsync()); // nothing was dropped
        Assert.Equal(1, await MigrationRowAsync()); // and the history row is still there: the batch that deletes it never ran

        await _db.ExecuteAsync("UPDATE StockDocument SET PurchaseOrderId = NULL");
        await RunScriptAsync(Latest, Before);
        Assert.Equal(0, await ColumnCountAsync());
        await RunScriptAsync(Before, Latest);
        Assert.Equal(2, await ColumnCountAsync());
    }

    /// <summary>
    /// The Down script is ONE batch (EF puts no GO inside a migration's transaction): the refusal is a THROW with XACT_ABORT ON, which ends the batch and rolls the transaction back, so the
    /// drops, the DELETE of the history row and the COMMIT never run, whether the client stops at an error (sqlcmd -b) or goes on (SSMS, sqlcmd without -b): there is no later batch to run.
    /// </summary>
    [Fact]
    public async Task A_refused_down_run_by_a_client_that_goes_on_after_errors_drops_nothing_and_keeps_the_history_row()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await RunScriptAsync(Before, Latest);
        await InsertOrderAsync();
        await LinkDocumentAsync(LinkedNo);

        var errors = await RunScriptAsync(Latest, Before, continueAfterError: true);

        Assert.Contains(errors, e => e.Message.Contains("Receipts are linked to purchase orders"));
        Assert.Equal(2, await ColumnCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
    }

    /// <summary>The filtered index needs QUOTED_IDENTIFIER ON and sqlcmd has it OFF: the script sets it itself, so the index is there whatever the client setting is.</summary>
    [Fact]
    public async Task The_up_script_creates_the_filtered_index_even_when_the_session_has_quoted_identifier_off()
    {
        await MigrateAsync(Before);
        await SeedAsync();

        var errors = await RunScriptAsync(Before, Latest, continueAfterError: true, sessionPrefix: "SET QUOTED_IDENTIFIER OFF;\n");

        Assert.Empty(errors);
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'StockDocument') AND name = N'IX_StockDocument_PurchaseOrderId' AND filter_definition IS NOT NULL"));
        Assert.Equal(1, await MigrationRowAsync());
    }

    private static string Messages(Exception e) => e is SqlException ? e.Message : string.Join(" | ", Chain(e).Select(x => x.Message));

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (; e is not null; e = e.InnerException) yield return e;
    }
}
