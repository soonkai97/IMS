using System.Text.RegularExpressions;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// Migration <c>AddReturnLinks</c> (PL3.16, PL3-M7): two nullable columns on the two big document tables (<c>StockDocument.ReturnOfDocumentId</c> with a self foreign key and a filtered index,
/// <c>StockDocumentLine.ReturnOfLineNo</c>) and <c>CK_StockDocument_OneLink</c> over three links instead of two. Metadata-only: no row is read or rewritten (a checksum of every row version proves it). Down refuses
/// while any document or line carries a return link (live or soft-deleted) and otherwise puts the two-link rule back. Each test has its own throw-away database; the timing at 1,000,000 documents is in
/// docs/dba/README.md section 16 (or why it was not taken).
/// </summary>
public class AddReturnLinksMigrationTests : IAsyncLifetime
{
    private const string Before = "20261109035255_GrantReturnsPermissions";
    private const string Latest = "20261109044255_AddReturnLinks";
    private const int Documents = 5_000;

    private readonly TestDatabase _db = TestDatabase.CreateNew();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _db.DropAsync();

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_db.ConnectionString).Options, new FrozenClock());

    private async Task MigrateAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>The script a DBA generates and runs by hand, one batch per GO, each batch on its own (so an error in one does not stop the next, as in SSMS). <paramref name="sessionPrefix"/> goes in front of every batch.</summary>
    private async Task RunScriptAsync(string from, string to, string sessionPrefix = "")
    {
        await using var db = NewContext();
        foreach (var batch in Regex.Split(db.GetService<IMigrator>().GenerateScript(from, to), @"^\s*GO\s*$", RegexOptions.Multiline))
            if (!string.IsNullOrWhiteSpace(batch)) await _db.ExecuteAsync(sessionPrefix + batch);
    }

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

    private Task<int> ColumnCountAsync() => _db.ScalarAsync<int>(
        "SELECT COUNT(*) FROM sys.columns WHERE (object_id = OBJECT_ID(N'StockDocument') AND name = N'ReturnOfDocumentId') OR (object_id = OBJECT_ID(N'StockDocumentLine') AND name = N'ReturnOfLineNo')");

    private Task<int> MigrationRowAsync() => _db.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest));

    private async Task<(int Documents, int Lines, long Versions)> RowsAsync() =>
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument"), await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocumentLine"),
         await _db.ScalarAsync<long>("SELECT SUM(CAST(CAST(ROW_VERSION AS bigint) % 1000003 AS bigint)) FROM (SELECT ROW_VERSION FROM StockDocument UNION ALL SELECT ROW_VERSION FROM StockDocumentLine) v"));

    private Task<int> LinkDocumentAsync(string no) => _db.ExecuteAsync("UPDATE StockDocument SET ReturnOfDocumentId = (SELECT TOP (1) Id FROM StockDocument WHERE No = N'RCV-W1-2') WHERE No = @no", ("@no", no));

    private static string Messages(Exception e) => e is SqlException ? e.Message : string.Join(" | ", Chain(e).Select(x => x.Message));

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (; e is not null; e = e.InnerException) yield return e;
    }

    // ----- shape -----
    [Fact]
    public async Task A_fresh_database_has_both_columns_nullable_int_with_no_default_the_filtered_index_the_restricting_self_foreign_key_and_the_three_link_rule()
    {
        await MigrateAsync(Latest);

        Assert.Equal(2, await ColumnCountAsync());
        const string column = "SELECT CONCAT(ty.name, N',', c.is_nullable, N',', c.default_object_id) FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c";
        Assert.Equal("int,1,0", await _db.ScalarAsync<string>(column, ("@t", "StockDocument"), ("@c", "ReturnOfDocumentId")));
        Assert.Equal("int,1,0", await _db.ScalarAsync<string>(column, ("@t", "StockDocumentLine"), ("@c", "ReturnOfLineNo")));
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'StockDocument') AND name = N'IX_StockDocument_ReturnOfDocumentId' AND filter_definition = N'([ReturnOfDocumentId] IS NOT NULL)'"));
        Assert.Equal("NO_ACTION", await _db.ScalarAsync<string>("SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = N'FK_StockDocument_StockDocument_ReturnOfDocumentId'"));
        Assert.Contains("ReturnOfDocumentId", await _db.ScalarAsync<string>("SELECT definition FROM sys.check_constraints WHERE name = N'CK_StockDocument_OneLink'"));
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT is_not_trusted FROM sys.check_constraints WHERE name = N'CK_StockDocument_OneLink'")); // added WITH CHECK
    }

    [Fact]
    public async Task A_fresh_database_and_an_upgraded_one_have_the_same_columns_indexes_and_constraints()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        var fresh = TestDatabase.CreateNew();
        try
        {
            await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fresh.ConnectionString).Options, new FrozenClock()))
                await db.GetService<IMigrator>().MigrateAsync(Latest);

            const string columns = "SELECT STRING_AGG(CONCAT(t.name, N'.', c.name, N':', ty.name, N'(', c.max_length, N',', c.precision, N',', c.scale, N')', c.is_nullable), N';') WITHIN GROUP (ORDER BY t.name, c.name) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE t.name IN (N'StockDocument', N'StockDocumentLine')";
            const string indexes = "SELECT STRING_AGG(CONCAT(t.name, N'.', i.name, N':', i.is_unique, ISNULL(i.filter_definition, N'')), N';') WITHIN GROUP (ORDER BY t.name, i.name) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE t.name IN (N'StockDocument', N'StockDocumentLine')";
            const string checks = "SELECT STRING_AGG(CONCAT(name, N':', definition), N';') WITHIN GROUP (ORDER BY name) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'StockDocument')";
            Assert.Equal(await fresh.ScalarAsync<string>(columns), await _db.ScalarAsync<string>(columns));
            Assert.Equal(await fresh.ScalarAsync<string>(indexes), await _db.ScalarAsync<string>(indexes));
            Assert.Equal(await fresh.ScalarAsync<string>(checks), await _db.ScalarAsync<string>(checks));
        }
        finally
        {
            await fresh.DropAsync();
        }
    }

    // ----- metadata only -----
    [Fact]
    public async Task On_a_database_with_many_documents_the_up_rewrites_no_row_and_every_row_reads_null()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        var rows = await RowsAsync();

        await MigrateAsync(Latest);

        Assert.Equal(rows, await RowsAsync()); // same counts and not one row version moved
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE ReturnOfDocumentId IS NOT NULL"));
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocumentLine WHERE ReturnOfLineNo IS NOT NULL"));
    }

    [Fact]
    public async Task Up_down_up_on_a_database_with_documents_keeps_every_row_and_puts_the_two_link_rule_back_while_down()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        var rows = await RowsAsync();

        await MigrateAsync(Latest);
        await MigrateAsync(Before);
        Assert.Equal(0, await ColumnCountAsync());
        Assert.Equal(0, await MigrationRowAsync());
        Assert.Equal(rows, await RowsAsync());
        Assert.DoesNotContain("ReturnOfDocumentId", await _db.ScalarAsync<string>("SELECT definition FROM sys.check_constraints WHERE name = N'CK_StockDocument_OneLink'"));

        await MigrateAsync(Latest);
        Assert.Equal(2, await ColumnCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
        Assert.Equal(rows, await RowsAsync());
    }

    // ----- the rule -----
    [Fact]
    public async Task A_document_may_carry_one_link_of_any_kind_and_not_two()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        await _db.ExecuteAsync("""
            INSERT SalesOrder (No, Status, WarehouseId, CustomerId, CreatedBy, CreatedAt, RequiresApproval, IsLarge)
            SELECT N'SO-W1-2610-0001', 2, (SELECT TOP (1) Id FROM Warehouse), (SELECT TOP (1) Id FROM Partner), (SELECT TOP (1) Id FROM [User]), SYSUTCDATETIME(), 0, 0;
            """);

        Assert.Equal(1, await LinkDocumentAsync("RCV-W1-3"));                                                                                   // a return link alone
        Assert.Equal(1, await _db.ExecuteAsync("UPDATE StockDocument SET SalesOrderId = (SELECT TOP (1) Id FROM SalesOrder) WHERE No = N'RCV-W1-4'")); // a sales link alone
        var two = await Assert.ThrowsAsync<SqlException>(() => _db.ExecuteAsync("UPDATE StockDocument SET SalesOrderId = (SELECT TOP (1) Id FROM SalesOrder) WHERE No = N'RCV-W1-3'"));
        var self = await Assert.ThrowsAsync<SqlException>(() => _db.ExecuteAsync("UPDATE StockDocument SET ReturnOfDocumentId = 999999999 WHERE No = N'RCV-W1-5'"));
        Assert.Equal((547, 547), (two.Number, self.Number)); // the CHECK, and the foreign key to a document that does not exist
    }

    // ----- down refuses -----
    [Theory]
    [InlineData("document", false)]
    [InlineData("document", true)] // a soft-deleted document still stores the link
    [InlineData("line", false)]
    public async Task Down_refuses_while_any_document_or_line_carries_a_return_link_and_changes_nothing(string where, bool softDeleted)
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        if (where == "document")
        {
            await LinkDocumentAsync("RCV-W1-3");
            if (softDeleted) await _db.ExecuteAsync("UPDATE StockDocument SET REC_DELETED = 1 WHERE No = N'RCV-W1-3'");
        }
        else await _db.ExecuteAsync("UPDATE StockDocumentLine SET ReturnOfLineNo = 1 WHERE DocumentId = (SELECT Id FROM StockDocument WHERE No = N'RCV-W1-3')");

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        Assert.Contains("Returns are linked", Messages(refused));
        Assert.Equal(2, await ColumnCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
        Assert.Contains("ReturnOfDocumentId", await _db.ScalarAsync<string>("SELECT definition FROM sys.check_constraints WHERE name = N'CK_StockDocument_OneLink'")); // not even the rule went back
    }

    [Fact]
    public async Task Down_works_once_the_links_are_cleared_and_up_gives_back_empty_columns()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        await LinkDocumentAsync("RCV-W1-3");
        await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        await _db.ExecuteAsync("UPDATE StockDocument SET ReturnOfDocumentId = NULL");
        await MigrateAsync(Before);

        Assert.Equal(0, await ColumnCountAsync());
        await MigrateAsync(Latest);
        Assert.Equal(2, await ColumnCountAsync());
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE ReturnOfDocumentId IS NOT NULL"));
    }

    // ----- the script -----
    [Fact]
    public async Task The_generated_script_does_what_the_runner_does_and_a_refused_down_drops_nothing_even_when_every_batch_runs()
    {
        await MigrateAsync(Before);
        await SeedAsync();

        await RunScriptAsync(Before, Latest);
        Assert.Equal(2, await ColumnCountAsync());

        await LinkDocumentAsync("RCV-W1-3");
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => RunScriptAsync(Latest, Before));
        Assert.Contains("Returns are linked", Messages(refused));
        Assert.Equal(2, await ColumnCountAsync());       // the batches after the refusal did not drop them
        Assert.Equal(1, await MigrationRowAsync());      // nor did the history row go: the whole Down is one batch

        await _db.ExecuteAsync("UPDATE StockDocument SET ReturnOfDocumentId = NULL");
        await RunScriptAsync(Latest, Before);
        Assert.Equal(0, await ColumnCountAsync());
        await RunScriptAsync(Before, Latest);
        Assert.Equal(2, await ColumnCountAsync());
    }

    /// <summary>The filtered index needs QUOTED_IDENTIFIER ON and sqlcmd has it OFF: the script sets it itself, so the index is there whatever the client setting is.</summary>
    [Fact]
    public async Task The_up_script_creates_the_filtered_index_and_the_rule_even_when_the_session_has_quoted_identifier_off()
    {
        await MigrateAsync(Before);
        await SeedAsync();

        await RunScriptAsync(Before, Latest, sessionPrefix: "SET QUOTED_IDENTIFIER OFF;\n");

        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'StockDocument') AND name = N'IX_StockDocument_ReturnOfDocumentId' AND filter_definition IS NOT NULL"));
        Assert.Contains("ReturnOfDocumentId", await _db.ScalarAsync<string>("SELECT definition FROM sys.check_constraints WHERE name = N'CK_StockDocument_OneLink'"));
        Assert.Equal(1, await MigrationRowAsync());
    }
}
