using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// Migration <c>AddReturnSelfLinkCheck</c> (PL3.17, PL3-M8): one CHECK, a document is never its own return. Added WITH CHECK over documents that all read NULL; Down drops it. Each test has its own throw-away database.
/// </summary>
public class AddReturnSelfLinkCheckMigrationTests : IAsyncLifetime
{
    private const string Before = "20261109044255_AddReturnLinks";
    private const string Latest = "20261109054504_AddReturnSelfLinkCheck";

    private readonly TestDatabase _db = TestDatabase.CreateNew();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _db.DropAsync();

    private async Task MigrateAsync(string target)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_db.ConnectionString).Options, new FrozenClock());
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task SeedAsync() => await _db.ExecuteAsync("""
        INSERT Warehouse (Code, Name, IsActive) VALUES (N'W1', N'One', 1);
        INSERT [User] (Username, Email, DisplayName, PasswordHash, RoleId, AllWarehouses, IsActive, FailedLoginCount)
        VALUES (N'first', N'first@example.test', N'First', N'x', (SELECT TOP (1) Id FROM Role), 1, 1, 0);
        INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval, IsLarge, RequiresOverride)
        SELECT 1, CONCAT(N'RCV-W1-', n.Number), 3, (SELECT TOP (1) Id FROM Warehouse), (SELECT TOP (1) Id FROM [User]), SYSUTCDATETIME(), 0, 0, 0
        FROM (SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Number FROM sys.all_columns a CROSS JOIN sys.all_columns b) n;
        """);

    private Task<int> RuleAsync() => _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_StockDocument_ReturnNotSelf' AND is_not_trusted = 0");

    [Fact]
    public async Task The_rule_is_added_trusted_refuses_a_document_that_returns_itself_and_allows_another_document_and_none()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        await MigrateAsync(Latest);

        Assert.Equal(1, await RuleAsync());
        Assert.Equal(1, await _db.ExecuteAsync("UPDATE StockDocument SET ReturnOfDocumentId = (SELECT MIN(Id) FROM StockDocument) WHERE Id = (SELECT MAX(Id) FROM StockDocument)"));
        var self = await Assert.ThrowsAsync<SqlException>(() => _db.ExecuteAsync("UPDATE StockDocument SET ReturnOfDocumentId = Id WHERE Id = (SELECT MIN(Id) + 1 FROM StockDocument)"));
        Assert.Equal(547, self.Number);
    }

    [Fact]
    public async Task Up_down_up_on_documents_changes_no_row_and_down_drops_only_the_rule()
    {
        await MigrateAsync(Before);
        await SeedAsync();
        var versions = await _db.ScalarAsync<long>("SELECT SUM(CAST(CAST(ROW_VERSION AS bigint) % 1000003 AS bigint)) FROM StockDocument");

        await MigrateAsync(Latest);
        await MigrateAsync(Before);
        Assert.Equal(0, await RuleAsync());
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_StockDocument_OneLink'")); // the rule of the previous migration stays
        await MigrateAsync(Latest);

        Assert.Equal(1, await RuleAsync());
        Assert.Equal(versions, await _db.ScalarAsync<long>("SELECT SUM(CAST(CAST(ROW_VERSION AS bigint) % 1000003 AS bigint)) FROM StockDocument")); // no row was rewritten
    }

    [Fact]
    public async Task A_fresh_database_has_the_same_rule_as_an_upgraded_one()
    {
        await MigrateAsync(Before);
        await MigrateAsync(Latest);
        var fresh = TestDatabase.CreateNew();
        try
        {
            await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fresh.ConnectionString).Options, new FrozenClock()))
                await db.GetService<IMigrator>().MigrateAsync(Latest);
            const string sql = "SELECT definition FROM sys.check_constraints WHERE name = N'CK_StockDocument_ReturnNotSelf'";

            Assert.Equal(await fresh.ScalarAsync<string>(sql), await _db.ScalarAsync<string>(sql));
        }
        finally
        {
            await fresh.DropAsync();
        }
    }
}
