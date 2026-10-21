using System.Text.RegularExpressions;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Infrastructure;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// Migration <c>AddPurchaseOrderApprovalRules</c> (PL2.3c): data only. A database with no purchase-order rule gets the two placeholder rules (5,000.00 Standard, 20,000.00 Large); one that has any
/// rule of that type, active, switched off, edited or soft-deleted, gets nothing; running it again adds nothing; Down does nothing; the generated script does what the runner does; and the rows are the
/// ones the Seeder writes (the same values, no duplicates when either runs after the other). Each migration test has its own throw-away database; the last two use the shared API database.
/// </summary>
[Collection(ApiCollection.Name)]
public class AddPurchaseOrderApprovalRulesMigrationTests(ApiFixture fixture)
{
    private const string Before = "20261020125112_AddPurchaseOrders";
    private const string Latest = "20261021090000_AddPurchaseOrderApprovalRules";

    /// <summary>The two rows as text, in amount order: type, amount, quantity, tier, active, deleted, creator, revision date, reviser. The creation date and the public id vary by nature.</summary>
    private const string Pair = "5|5000.00|-|1|1|0|-|-|-;5|20000.00|-|2|1|0|-|-|-";

    private const string RowsSql = """
        SELECT STRING_AGG(CONCAT(DocType, '|', AmountThreshold, '|', ISNULL(CONVERT(varchar(20), QtyThreshold), '-'), '|', Tier, '|', IsActive, '|', REC_DELETED, '|',
                                 ISNULL(CONVERT(varchar(20), REC_CREATOR), '-'), '|', IIF(REC_REVDATE IS NULL, '-', 'x'), '|', IIF(REC_REVISOR IS NULL, '-', 'x')), ';') WITHIN GROUP (ORDER BY AmountThreshold)
        FROM ApprovalRule WHERE DocType = 5
        """;

    private static async Task MigrateAsync(TestDatabase database, string? target)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task RunScriptAsync(TestDatabase database, string from, string to)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());
        foreach (var batch in Regex.Split(db.GetService<IMigrator>().GenerateScript(from, to), @"^\s*GO\s*$", RegexOptions.Multiline))
            if (!string.IsNullOrWhiteSpace(batch)) await database.ExecuteAsync(batch);
    }

    private static async Task WithDatabaseAsync(Func<TestDatabase, Task> test)
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await test(database);
        }
        finally
        {
            await database.DropAsync();
        }
    }

    private static Task<string?> PairOfAsync(TestDatabase database) => database.ScalarAsync<string?>(RowsSql);

    /// <summary>
    /// Takes the SHARED database down to <see cref="Before"/> and back up to the newest migration. The newest migration's Down refuses while any document or line carries a link to a purchase
    /// order, and the purchasing test classes leave linked receipts behind (their reset cancels orders, it does not unlink receipts), so whether this passed would depend on the order xUnit runs
    /// the classes in. It therefore (1) first leaves a linked, soft-deleted receipt behind itself, the worst case, and (2) clears every link before going down. The receipts are test data, the
    /// links on them are not asserted by anything that runs afterwards.
    /// </summary>
    private static async Task MigrateSharedDownAndUpAsync(TestDatabase database)
    {
        await database.ExecuteAsync("""
            INSERT PurchaseOrder (No, Status, WarehouseId, SupplierId, CreatedBy, CreatedAt, RequiresApproval, IsLarge, REC_DELETED)
            SELECT CONCAT(N'PO-LEFT-', LEFT(CONVERT(nvarchar(36), NEWID()), 8)), 4, (SELECT TOP (1) Id FROM Warehouse), (SELECT TOP (1) Id FROM Partner), (SELECT TOP (1) Id FROM [User]), SYSUTCDATETIME(), 0, 0, 1;
            INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval, IsLarge, RequiresOverride, PurchaseOrderId, REC_DELETED)
            SELECT 1, CONCAT(N'RCV-LEFT-', LEFT(CONVERT(nvarchar(36), NEWID()), 8)), 0, (SELECT TOP (1) Id FROM Warehouse), (SELECT TOP (1) Id FROM [User]), SYSUTCDATETIME(), 0, 0, 0, (SELECT MAX(Id) FROM PurchaseOrder), 1;
            INSERT StockDocumentLine (DocumentId, ProductId, Qty, UomId, BaseQty, UnitCost, EnteredUnitCost, Amount, CostFromAverage, PurchaseOrderLineNo, REC_DELETED)
            SELECT (SELECT MAX(Id) FROM StockDocument), (SELECT TOP (1) Id FROM Product), 1, (SELECT TOP (1) Id FROM Uom), 1, 1, 1, 1, 0, 1, 1;
            """);
        await database.ExecuteAsync("UPDATE StockDocumentLine SET PurchaseOrderLineNo = NULL WHERE PurchaseOrderLineNo IS NOT NULL; UPDATE StockDocument SET PurchaseOrderId = NULL WHERE PurchaseOrderId IS NOT NULL");
        await MigrateAsync(database, Before);
        await MigrateAsync(database, null); // to the newest, not to Latest: a later migration adds columns the rest of the suite reads
    }

    private static Task<int> CountAsync(TestDatabase database) => database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType = 5");

    [Fact]
    public Task A_database_with_no_purchase_order_rule_gets_the_two_placeholder_rules_and_no_other_rule_changes() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await database.ExecuteAsync("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier) VALUES (2, 5000.00, 1, 1)"); // a document rule that was there
        Assert.Equal(0, await CountAsync(database));

        await MigrateAsync(database, Latest);

        Assert.Equal(Pair, await PairOfAsync(database));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(DISTINCT PublicId) FROM ApprovalRule WHERE DocType = 5 AND PublicId <> '00000000-0000-0000-0000-000000000000'"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType <> 5"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType = 2 AND AmountThreshold = 5000.00 AND IsActive = 1 AND Tier = 1"));
    });

    [Theory]
    [InlineData("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier) VALUES (5, 100.00, 1, 1)")] // edited
    [InlineData("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier) VALUES (5, 5000.00, 0, 1)")] // switched off
    [InlineData("INSERT ApprovalRule (DocType, QtyThreshold, IsActive, Tier) VALUES (5, 10, 1, 2)")] // a quantity rule
    [InlineData("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier, REC_DELETED) VALUES (5, 5000.00, 1, 1, 1)")] // soft-deleted
    [InlineData("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier) VALUES (5, 5000.00, 1, 1), (5, 20000.00, 1, 2)")] // the pair already there
    public Task A_database_that_has_any_purchase_order_rule_gets_nothing(string existing) => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await database.ExecuteAsync(existing);
        var rows = await PairOfAsync(database);
        var count = await CountAsync(database);

        await MigrateAsync(database, Latest);

        Assert.Equal(count, await CountAsync(database));
        Assert.Equal(rows, await PairOfAsync(database));
    });

    [Fact]
    public Task Going_down_keeps_the_rules_and_going_up_again_adds_nothing() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);
        await database.ExecuteAsync("UPDATE ApprovalRule SET AmountThreshold = 7500.00 WHERE DocType = 5 AND Tier = 1"); // an Administrator edited one
        var edited = await PairOfAsync(database);

        await MigrateAsync(database, Before);

        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));
        Assert.Equal(edited, await PairOfAsync(database)); // down does nothing: the rules may have been edited

        await MigrateAsync(database, Latest);

        Assert.Equal(edited, await PairOfAsync(database));
        Assert.Equal(2, await CountAsync(database));
    });

    [Fact]
    public Task The_generated_script_does_what_the_runner_does() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);

        await RunScriptAsync(database, Before, Latest);
        Assert.Equal(Pair, await PairOfAsync(database));

        await database.ExecuteAsync("DELETE FROM ApprovalRule WHERE DocType = 5 AND Tier = 2");
        await database.ExecuteAsync("DELETE FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest));
        await RunScriptAsync(database, Before, Latest); // the same script again, on a database that already holds a rule of the type
        Assert.Equal(1, await CountAsync(database));
    });

    [Fact]
    public Task A_fresh_database_and_an_upgraded_one_hold_the_same_rules() => WithDatabaseAsync(async upgraded =>
    {
        var fresh = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(upgraded, Before);
            await MigrateAsync(upgraded, Latest);
            await MigrateAsync(fresh, Latest);

            Assert.Equal(await PairOfAsync(upgraded), await PairOfAsync(fresh));
            Assert.Equal(Pair, await PairOfAsync(fresh));
        }
        finally
        {
            await fresh.DropAsync();
        }
    });

    // ----- the Seeder agrees, and the end-to-end proof -----
    [Fact]
    public async Task The_seeder_writes_the_same_rows_as_the_migration_and_neither_duplicates_the_other()
    {
        // The shared database was migrated before the Seeder ran, so its pair came from the migration.
        Assert.Equal(Pair, await PairOfAsync(fixture.Database));

        // Remove the pair by hand and let the Seeder write it: same values.
        await fixture.Database.ExecuteAsync("DELETE FROM ApprovalRule WHERE DocType = 5");
        await Seeder.SeedAsync(fixture.Host.Services, development: true);
        Assert.Equal(Pair, await PairOfAsync(fixture.Database));

        // The Seeder again adds nothing, and the migration after the Seeder adds nothing.
        await Seeder.SeedAsync(fixture.Host.Services, development: true);
        Assert.Equal(2, await CountAsync(fixture.Database));
        await MigrateSharedDownAndUpAsync(fixture.Database);
        Assert.Equal(Pair, await PairOfAsync(fixture.Database));
        Assert.Equal(SeedDefaults.PurchaseOrderStandardAmount, 5000m); // the constants the Seeder uses are the ones the migration freezes
        Assert.Equal(SeedDefaults.PurchaseOrderLargeAmount, 20000m);
    }

    [Fact]
    public async Task A_database_that_had_no_rules_and_got_the_migration_can_have_purchasing_switched_on_through_the_api()
    {
        await Ims.Api.Tests.Tests.Purchasing.PurchasingScenario.ResetAsync(fixture); // purchasing off
        await fixture.Database.ExecuteAsync("DELETE FROM ApprovalRule WHERE DocType = 5"); // a database a DBA deployed: the Seeder never ran, there is no rule
        var admin = await fixture.AsAsync(Roles.Admin);
        var why = $"Migration proof {Guid.NewGuid():N}"[..28];
        (await admin.Modules.SwitchAsync(ModuleCodes.Purchasing, true, why)).ShouldBeProblem(System.Net.HttpStatusCode.Conflict); // and the module cannot be switched on

        await MigrateSharedDownAndUpAsync(fixture.Database);

        Assert.Equal(Pair, await PairOfAsync(fixture.Database));
        (await admin.Modules.SwitchAsync(ModuleCodes.Purchasing, true, why)).ShouldBeOk();
        var rules = (await admin.Settings.ListRulesAsync()).Data<List<Ims.Application.Features.Settings.ApprovalRuleDto>>().Where(r => r.DocType == DocumentType.PurchaseOrder).ToList();
        Assert.Equal(2, rules.Count); // and the rules are there to edit in Settings
        await Ims.Api.Tests.Tests.Purchasing.PurchasingScenario.ResetAsync(fixture);
    }
}
