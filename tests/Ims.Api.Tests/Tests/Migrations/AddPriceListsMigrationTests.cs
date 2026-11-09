using System.Text.RegularExpressions;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Ims.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// Migration <c>AddPriceLists</c> (PL3.11, PL3-M5): two NEW tables, empty, and the grant of <c>pricelist.manage</c> to the Administrator, the Owner and the Supervisor in the same migration. Run against a
/// database that already holds rows: Up creates the tables and the grant and touches no row of any other table, Down refuses while any price list exists (live or soft-deleted) and otherwise removes the
/// grant (whichever role holds it) and drops both tables, and Up, Down, Up works, as the runner and as the script a DBA generates. Each test has its own throw-away database.
/// </summary>
public class AddPriceListsMigrationTests : IAsyncLifetime
{
    private const string Before = "20261108061359_AddDeliverySalesOrderLinks";
    private const string Latest = "20261108132822_AddPriceLists";
    private const int Products = 5_000;
    private const int Partners = 2_000;

    private static readonly string[] Holders = [Roles.Admin, Roles.Owner, Roles.Supervisor];

    private readonly TestDatabase _db = TestDatabase.CreateNew();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _db.DropAsync();

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_db.ConnectionString).Options, new FrozenClock());

    private async Task MigrateAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>The script a DBA generates and runs by hand, one batch per GO, each batch on its own (so an error in one does not stop the next, as in SSMS). <paramref name="sessionPrefix"/> is put in front of every batch.</summary>
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
            INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive)
            SELECT CONCAT(N'SKU-', n.Number), CONCAT(N'Product ', n.Number), (SELECT TOP (1) Id FROM Uom), 0, 1
            FROM (SELECT TOP ({Products}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Number FROM sys.all_columns a CROSS JOIN sys.all_columns b) n;
            INSERT Partner (Type, Name, IsActive)
            SELECT 2, CONCAT(N'Partner ', n.Number), 1
            FROM (SELECT TOP ({Partners}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Number FROM sys.all_columns a CROSS JOIN sys.all_columns b) n;
            UPDATE Product SET REC_DELETED = 1 WHERE Sku = N'SKU-1';
            UPDATE Partner SET REC_DELETED = 1 WHERE Name = N'Partner 1';
            """);
    }

    private Task<int> InsertListAsync(string name, bool isDefault = false, bool softDeleted = false) => _db.ExecuteAsync(
        "INSERT PriceList (Name, IsDefault, REC_DELETED) VALUES (@n, @d, @x)", ("@n", name), ("@d", isDefault), ("@x", softDeleted));

    private Task<int> TableCountAsync() => _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name IN (N'PriceList', N'PriceListItem')");

    private Task<int> MigrationRowAsync() => _db.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest));

    private Task<int> GrantsAsync() => _db.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission WHERE PermissionCode = @c", ("@c", Permissions.PricelistManage));

    private async Task<(int Products, int Partners, long Versions)> OtherRowsAsync() =>
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Product"), await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Partner"),
         await _db.ScalarAsync<long>("SELECT SUM(CAST(CAST(ROW_VERSION AS bigint) % 1000003 AS bigint)) FROM (SELECT ROW_VERSION FROM Product UNION ALL SELECT ROW_VERSION FROM Partner) v"));

    private async Task<string> ColumnTypeAsync(string table, string column) => await _db.ScalarAsync<string>(
        "SELECT CASE WHEN ty.name IN (N'decimal') THEN CONCAT(ty.name, N'(', c.precision, N',', c.scale, N')') WHEN ty.name = N'nvarchar' THEN CONCAT(ty.name, N'(', c.max_length / 2, N')') ELSE ty.name END " +
        "FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c", ("@t", table), ("@c", column));

    private Task<int> IndexAsync(string table, string name, string filter) => _db.ScalarAsync<int>(
        "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(@t) AND name = @n AND is_unique = 1 AND filter_definition = @f", ("@t", table), ("@n", name), ("@f", filter));

    // ----- shape -----
    [Fact]
    public async Task A_fresh_database_has_the_two_tables_with_the_money_type_the_lengths_the_filtered_unique_indexes_and_the_checks()
    {
        await MigrateAsync(Latest);

        Assert.Equal(2, await TableCountAsync());
        Assert.Equal("decimal(18,2)", await ColumnTypeAsync("PriceListItem", "ListPrice"));
        Assert.Equal("nvarchar(200)", await ColumnTypeAsync("PriceList", "Name"));
        Assert.Equal("timestamp", await ColumnTypeAsync("PriceList", "ROW_VERSION"));
        Assert.Equal("timestamp", await ColumnTypeAsync("PriceListItem", "ROW_VERSION"));
        Assert.Equal(1, await IndexAsync("PriceList", "IX_PriceList_IsDefault", "([IsDefault]=(1) AND [REC_DELETED]=(0))"));
        Assert.Equal(1, await IndexAsync("PriceList", "IX_PriceList_CustomerId", "([CustomerId] IS NOT NULL AND [REC_DELETED]=(0))"));
        Assert.Equal(1, await IndexAsync("PriceList", "IX_PriceList_Name", "([REC_DELETED]=(0))"));
        Assert.Equal(1, await IndexAsync("PriceListItem", "IX_PriceListItem_PriceListId_ProductId_UomId", "([REC_DELETED]=(0))"));
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'PriceList') AND name = N'UX_PriceList_PublicId' AND is_unique = 1 AND filter_definition IS NULL"));
        Assert.Equal("ListPrice", await _db.ScalarAsync<string>( // the lookup index carries the price
            "SELECT c.name FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "WHERE i.object_id = OBJECT_ID(N'PriceListItem') AND i.name = N'IX_PriceListItem_PriceListId_ProductId_UomId' AND ic.is_included_column = 1"));
        Assert.Equal(["CK_PriceListItem_ListPrice", "CK_PriceList_DefaultHasNoCustomer"], (await _db.ScalarAsync<string>(
            "SELECT STRING_AGG(name, N',') FROM sys.check_constraints WHERE name LIKE N'CK[_]PriceList%'")).Split(',').Order(StringComparer.Ordinal));
        Assert.Equal((0, 0), (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList"), await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PriceListItem")));
    }

    [Fact]
    public async Task A_fresh_database_and_an_upgraded_one_have_the_same_tables_columns_indexes_and_grants()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        var fresh = TestDatabase.CreateNew();
        try
        {
            await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fresh.ConnectionString).Options, new FrozenClock()))
                await db.GetService<IMigrator>().MigrateAsync(Latest);

            const string columns = "SELECT STRING_AGG(CONCAT(t.name, N'.', c.name, N':', ty.name, N'(', c.max_length, N',', c.precision, N',', c.scale, N')', c.is_nullable), N';') WITHIN GROUP (ORDER BY t.name, c.name) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE t.name LIKE N'PriceList%'";
            const string indexes = "SELECT STRING_AGG(CONCAT(t.name, N'.', i.name, N':', i.is_unique, ISNULL(i.filter_definition, N'')), N';') WITHIN GROUP (ORDER BY t.name, i.name) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE t.name LIKE N'PriceList%'";
            const string grants = "SELECT STRING_AGG(CONCAT(r.Code, N':', p.PermissionCode), N';') WITHIN GROUP (ORDER BY r.Code, p.PermissionCode) FROM RolePermission p JOIN Role r ON r.Id = p.RoleId";
            Assert.Equal(await fresh.ScalarAsync<string>(columns), await _db.ScalarAsync<string>(columns));
            Assert.Equal(await fresh.ScalarAsync<string>(indexes), await _db.ScalarAsync<string>(indexes));
            Assert.Equal(await fresh.ScalarAsync<string>(grants), await _db.ScalarAsync<string>(grants));
        }
        finally
        {
            await fresh.DropAsync();
        }
    }

    // ----- new tables and the grant only -----
    [Fact]
    public void Up_creates_two_tables_and_their_indexes_adds_the_grant_and_alters_nothing_that_exists()
    {
        var up = new AddPriceLists().UpOperations;

        Assert.Equal(["PriceList", "PriceListItem"], up.OfType<CreateTableOperation>().Select(t => t.Name).Order());
        Assert.All(up, op => Assert.True(op is CreateTableOperation or CreateIndexOperation or SqlOperation, $"{op.GetType().Name} touches something that exists"));
        Assert.All(up.OfType<CreateIndexOperation>(), i => Assert.StartsWith("PriceList", i.Table));
        Assert.Single(up.OfType<SqlOperation>(), s => s.Sql.Contains("INSERT INTO [RolePermission]"));
    }

    [Fact]
    public async Task The_generated_up_script_touches_no_row_of_any_other_table_except_to_insert_the_grant()
    {
        await using var db = NewContext();

        var script = db.GetService<IMigrator>().GenerateScript(Before, Latest);

        Assert.Equal(2, Regex.Matches(script, @"CREATE TABLE \[PriceList(Item)?\]").Count);
        var touch = Regex.Match(script, @"(?i)\bUPDATE\b|\bDELETE FROM\b|ALTER TABLE \[(?!PriceList)|INSERT INTO \[(?!__EFMigrationsHistory|RolePermission)");
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
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList"));
    }

    // ----- the grant -----
    [Fact]
    public async Task The_administrator_the_owner_and_the_supervisor_gain_pricelist_manage_and_nobody_else_does()
    {
        await MigrateAsync(Before);
        Assert.Equal(0, await GrantsAsync());

        await MigrateAsync(Latest);

        Assert.Equal(Holders.Order(), (await _db.ScalarAsync<string>(
            "SELECT STRING_AGG(r.Code, N',') WITHIN GROUP (ORDER BY r.Code) FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE p.PermissionCode = @c", ("@c", Permissions.PricelistManage))).Split(','));
        foreach (var role in Roles.All)
        {
            var held = (await _db.ScalarAsync<string>("SELECT STRING_AGG(p.PermissionCode, ';') FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @c AND p.REC_DELETED = 0", ("@c", role)) ?? "").Split(';').ToHashSet();
            Assert.True(role == Roles.Admin ? held.SetEquals(PermissionCatalog.Codes) : held.SetEquals(SystemRoleDefaults.For(role)), $"{role} must hold what the defaults say, and nothing more.");
        }
    }

    [Theory]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.Supervisor, true)]
    public async Task A_role_without_master_data_read_does_not_get_it_because_the_permission_requires_it(string role, bool revokedNotDeleted)
    {
        await MigrateAsync(Before);
        await _db.ExecuteAsync(revokedNotDeleted
            ? "UPDATE p SET REC_DELETED = 1 FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @r AND p.PermissionCode = @c"
            : "DELETE p FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @r AND p.PermissionCode = @c", ("@r", role), ("@c", Permissions.MasterDataRead));

        await MigrateAsync(Latest);

        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @r AND p.PermissionCode = @c", ("@r", role), ("@c", Permissions.PricelistManage)));
        Assert.Equal(2, await GrantsAsync());
    }

    [Fact]
    public async Task A_grant_that_is_already_there_is_not_added_twice()
    {
        await MigrateAsync(Before);
        await _db.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code = @a", ("@c", Permissions.PricelistManage), ("@a", Roles.Owner));

        await MigrateAsync(Latest);

        Assert.Equal(3, await GrantsAsync());
    }

    // ----- down and up again -----
    [Fact]
    public async Task Up_down_up_on_a_database_with_rows_keeps_every_row_of_the_other_tables_and_removes_and_restores_the_grant()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        var rows = await OtherRowsAsync();

        await MigrateAsync(Latest);
        await _db.ExecuteAsync("INSERT Role (Code, Name, Description, IsSystem, IsActive) VALUES (N'Buyer', N'Buyer', N'Buys', 0, 1)");
        await _db.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code = N'Buyer'", ("@c", Permissions.PricelistManage));
        Assert.Equal(4, await GrantsAsync());

        await MigrateAsync(Before);
        Assert.Equal(0, await TableCountAsync());
        Assert.Equal(0, await MigrationRowAsync());
        Assert.Equal(0, await GrantsAsync()); // the previous release does not know the code, so no role keeps a row for it
        Assert.Equal(rows, await OtherRowsAsync());

        await MigrateAsync(Latest);
        Assert.Equal(2, await TableCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
        Assert.Equal(3, await GrantsAsync()); // only the system roles
        Assert.Equal(rows, await OtherRowsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // a soft-deleted list is still a row
    public async Task Down_refuses_while_any_price_list_exists_and_changes_nothing_not_even_the_grant(bool softDeleted)
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        await InsertListAsync("Retail", softDeleted: softDeleted);

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        Assert.Contains("Price lists exist", Messages(refused));
        Assert.Equal(2, await TableCountAsync());
        Assert.Equal(1, await MigrationRowAsync());
        Assert.Equal(3, await GrantsAsync());
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList"));
    }

    [Fact]
    public async Task Down_works_once_the_price_lists_are_deleted_and_up_gives_back_empty_tables_and_the_grant()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);
        await InsertListAsync("Retail");
        await _db.ExecuteAsync("INSERT PriceListItem (PriceListId, ProductId, UomId, ListPrice) SELECT (SELECT TOP (1) Id FROM PriceList), p.Id, p.BaseUomId, 1.5 FROM Product p WHERE p.Sku = N'SKU-2'");
        await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(Before));

        await _db.ExecuteAsync("DELETE FROM PriceListItem; DELETE FROM PriceList");
        await MigrateAsync(Before);

        Assert.Equal(0, await TableCountAsync());
        await MigrateAsync(Latest);
        Assert.Equal(2, await TableCountAsync());
        Assert.Equal(3, await GrantsAsync());
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList"));
    }

    [Fact]
    public async Task The_generated_script_does_what_the_runner_does_and_a_refused_down_drops_nothing_even_when_every_batch_runs()
    {
        await MigrateAsync(Before);
        await SeedAsync();

        await RunScriptAsync(Before, Latest);
        Assert.Equal((2, 3), (await TableCountAsync(), await GrantsAsync()));

        await InsertListAsync("Retail");
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => RunScriptAsync(Latest, Before));
        Assert.Contains("Price lists exist", Messages(refused));
        Assert.Equal(2, await TableCountAsync());   // the batches after the refusal did not drop them
        Assert.Equal(3, await GrantsAsync());       // nor did the grant go
        Assert.Equal(1, await MigrationRowAsync()); // nor the history row: the whole Down is one batch

        await _db.ExecuteAsync("DELETE FROM PriceList");
        await RunScriptAsync(Latest, Before);
        Assert.Equal((0, 0), (await TableCountAsync(), await GrantsAsync()));
        await RunScriptAsync(Before, Latest);
        Assert.Equal((2, 3), (await TableCountAsync(), await GrantsAsync()));
    }

    /// <summary>A filtered index needs QUOTED_IDENTIFIER ON and sqlcmd has it OFF by default: the script must still create all of them.</summary>
    [Fact]
    public async Task The_up_script_creates_the_filtered_indexes_even_when_the_session_has_quoted_identifier_off()
    {
        await MigrateAsync(Before);

        await RunScriptAsync(Before, Latest, sessionPrefix: "SET QUOTED_IDENTIFIER OFF;\n");

        Assert.Equal(4, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id IN (OBJECT_ID(N'PriceList'), OBJECT_ID(N'PriceListItem')) AND is_unique = 1 AND filter_definition IS NOT NULL"));
        Assert.Equal(1, await MigrationRowAsync());
    }

    private static string Messages(Exception e) => e is SqlException ? e.Message : string.Join(" | ", Chain(e).Select(x => x.Message));

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (; e is not null; e = e.InnerException) yield return e;
    }
}
