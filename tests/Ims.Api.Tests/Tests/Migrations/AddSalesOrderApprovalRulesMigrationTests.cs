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
/// Migration <c>AddSalesOrderApprovalRules</c> (PL3.4): data only. A database with no sales-order rule gets the two placeholder rules (5,000.00 Standard, 20,000.00 Large); one that has any
/// rule of that type, active, switched off, edited or soft-deleted, gets nothing; running it again adds nothing; Down removes the two untouched rows it wrote and nothing else (an edited, added, switched-off or soft-deleted rule stays, and so does every sales order); the generated script does what the runner does; and the rows are the
/// ones the Seeder writes (the same values, no duplicates when either runs after the other). Each migration test has its own throw-away database; the last two use the shared API database.
/// </summary>
[Collection(ApiCollection.Name)]
public class AddSalesOrderApprovalRulesMigrationTests(ApiFixture fixture)
{
    private const string Before = "20261023110000_AddSalesOrders";
    private const string Latest = "20261023110329_AddSalesOrderApprovalRules";

    /// <summary>The two rows as text, in amount order: type, amount, quantity, tier, active, deleted, creator, revision date, reviser. The creation date and the public id vary by nature.</summary>
    private const string Pair = "6|5000.00|-|1|1|0|-|-|-;6|20000.00|-|2|1|0|-|-|-";

    private const string RowsSql = """
        SELECT STRING_AGG(CONCAT(DocType, '|', AmountThreshold, '|', ISNULL(CONVERT(varchar(20), QtyThreshold), '-'), '|', Tier, '|', IsActive, '|', REC_DELETED, '|',
                                 ISNULL(CONVERT(varchar(20), REC_CREATOR), '-'), '|', IIF(REC_REVDATE IS NULL, '-', 'x'), '|', IIF(REC_REVISOR IS NULL, '-', 'x')), ';') WITHIN GROUP (ORDER BY AmountThreshold)
        FROM ApprovalRule WHERE DocType = 6
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

    /// <summary>Takes the SHARED database one migration down (this one) and back up to the newest.</summary>
    private static async Task MigrateSharedDownAndUpAsync(TestDatabase database)
    {
        await MigrateAsync(database, Before);
        await MigrateAsync(database, null);
    }

    private static Task<int> CountAsync(TestDatabase database) => database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType = 6");

    [Fact]
    public Task A_database_with_no_sales_order_rule_gets_the_two_placeholder_rules_and_no_other_rule_changes() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await database.ExecuteAsync("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier) VALUES (2, 5000.00, 1, 1)"); // a document rule that was there
        Assert.Equal(0, await CountAsync(database));
        var others = await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType <> 6"); // the document rule above and the purchase-order pair of an earlier migration

        await MigrateAsync(database, Latest);

        Assert.Equal(Pair, await PairOfAsync(database));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(DISTINCT PublicId) FROM ApprovalRule WHERE DocType = 6 AND PublicId <> '00000000-0000-0000-0000-000000000000'"));
        Assert.Equal(others, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType <> 6"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType = 2 AND AmountThreshold = 5000.00 AND IsActive = 1 AND Tier = 1"));
    });

    [Theory]
    [InlineData("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier) VALUES (6, 100.00, 1, 1)")] // edited
    [InlineData("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier) VALUES (6, 5000.00, 0, 1)")] // switched off
    [InlineData("INSERT ApprovalRule (DocType, QtyThreshold, IsActive, Tier) VALUES (6, 10, 1, 2)")] // a quantity rule
    [InlineData("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier, REC_DELETED) VALUES (6, 5000.00, 1, 1, 1)")] // soft-deleted
    [InlineData("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier) VALUES (6, 5000.00, 1, 1), (5, 20000.00, 1, 2)")] // the pair already there
    public Task A_database_that_has_any_sales_order_rule_gets_nothing(string existing) => WithDatabaseAsync(async database =>
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
    public Task Going_down_removes_the_two_rows_it_wrote_and_going_up_again_writes_them_again() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);

        await MigrateAsync(database, Before);

        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));
        Assert.Equal(0, await CountAsync(database));
        await MigrateAsync(database, Latest);
        Assert.Equal(Pair, await PairOfAsync(database));
    });

    [Theory]
    [InlineData("UPDATE ApprovalRule SET AmountThreshold = 7500.00 WHERE DocType = 6 AND Tier = 1")] // edited amount
    [InlineData("UPDATE ApprovalRule SET IsActive = 0 WHERE DocType = 6 AND Tier = 2")] // switched off
    [InlineData("UPDATE ApprovalRule SET REC_REVDATE = SYSUTCDATETIME() WHERE DocType = 6 AND Tier = 1")] // revised
    [InlineData("UPDATE ApprovalRule SET REC_DELETED = 1 WHERE DocType = 6 AND Tier = 2")] // soft-deleted
    public Task Going_down_keeps_a_rule_an_administrator_changed(string change) => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);
        await database.ExecuteAsync(change);

        await MigrateAsync(database, Before);

        Assert.Equal(1, await CountAsync(database)); // the untouched one went, the changed one stayed
    });

    [Fact]
    public Task Going_down_with_sales_orders_present_removes_the_untouched_pair_and_leaves_the_orders_intact() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);
        await database.ExecuteAsync("""
            INSERT Uom (Code, Name) VALUES (N'PCS', N'Pieces');
            INSERT Partner (Type, Name, IsActive) VALUES (2, N'Customer', 1);
            INSERT Warehouse (Code, Name, IsActive) VALUES (N'W1', N'One', 1);
            INSERT [User] (Username, Email, DisplayName, PasswordHash, RoleId, AllWarehouses, IsActive, FailedLoginCount)
            VALUES (N'first', N'first@example.test', N'First', N'x', (SELECT TOP (1) Id FROM Role), 1, 1, 0);
            INSERT SalesOrder (No, Status, WarehouseId, CustomerId, CreatedBy, CreatedAt, RequiresApproval, IsLarge)
            SELECT N'SO-W1-2610-0001', 2, (SELECT TOP (1) Id FROM Warehouse), (SELECT TOP (1) Id FROM Partner), (SELECT TOP (1) Id FROM [User]), SYSUTCDATETIME(), 1, 1;
            """);
        await database.ExecuteAsync("UPDATE ApprovalRule SET AmountThreshold = 30000.00 WHERE DocType = 6 AND Tier = 2"); // the Large rule was edited after an order was routed to it

        await MigrateAsync(database, Before);

        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder WHERE No = N'SO-W1-2610-0001' AND IsLarge = 1 AND Status = 2")); // the order is untouched
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType = 6")); // the untouched Standard rule went
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType = 6 AND Tier = 2 AND AmountThreshold = 30000.00")); // the edited Large rule stayed
    });

    [Fact]
    public Task Going_down_keeps_rules_an_administrator_added() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);
        await database.ExecuteAsync("INSERT ApprovalRule (DocType, AmountThreshold, IsActive, Tier) VALUES (6, 1000.00, 1, 1)");

        await MigrateAsync(database, Before);

        Assert.Equal(1, await CountAsync(database));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType = 6 AND AmountThreshold = 1000.00"));
    });

    [Fact]
    public Task The_generated_script_does_what_the_runner_does() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);

        await RunScriptAsync(database, Before, Latest);
        Assert.Equal(Pair, await PairOfAsync(database));

        await database.ExecuteAsync("DELETE FROM ApprovalRule WHERE DocType = 6 AND Tier = 2");
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
        await fixture.Database.ExecuteAsync("DELETE FROM ApprovalRule WHERE DocType = 6");
        await Seeder.SeedAsync(fixture.Host.Services, development: true);
        Assert.Equal(Pair, await PairOfAsync(fixture.Database));

        // The Seeder again adds nothing, and the migration after the Seeder adds nothing.
        await Seeder.SeedAsync(fixture.Host.Services, development: true);
        Assert.Equal(2, await CountAsync(fixture.Database));
        await MigrateSharedDownAndUpAsync(fixture.Database);
        Assert.Equal(Pair, await PairOfAsync(fixture.Database));
        Assert.Equal(SeedDefaults.SalesOrderStandardAmount, 5000m); // the constants the Seeder uses are the ones the migration freezes
        Assert.Equal(SeedDefaults.SalesOrderLargeAmount, 20000m);
    }

    [Fact]
    public async Task The_seeder_still_writes_the_default_issue_rule_when_only_order_rules_exist()
    {
        // The order pairs are written by migrations, so a fresh database always has rules: the default for the stock documents must not be mistaken for "already there" (Seeder, PL3.4).
        await fixture.Database.ExecuteAsync("DELETE FROM ApprovalRule WHERE DocType IN (1, 2, 3, 4)");
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType IN (1, 2, 3, 4)"));
        Assert.True(await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType IN (5, 6)") >= 2);

        await Seeder.SeedAsync(fixture.Host.Services, development: true);
        await Seeder.SeedAsync(fixture.Host.Services, development: true);

        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType = 2 AND AmountThreshold = 5000.00 AND IsActive = 1"));
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType IN (1, 2, 3, 4)"));
    }

    [Fact]
    public async Task A_database_that_had_no_rules_and_got_the_migration_can_have_sales_switched_on_through_the_api()
    {
        await Ims.Api.Tests.Tests.Sales.SalesScenario.ResetAsync(fixture); // sales off
        await fixture.Database.ExecuteAsync("DELETE FROM ApprovalRule WHERE DocType = 6"); // a database a DBA deployed: the Seeder never ran, there is no rule
        var admin = await fixture.AsAsync(Roles.Admin);
        var why = $"Migration proof {Guid.NewGuid():N}"[..28];
        (await admin.Modules.SwitchAsync(ModuleCodes.Sales, true, why)).ShouldBeProblem(System.Net.HttpStatusCode.Conflict); // and the module cannot be switched on

        await MigrateSharedDownAndUpAsync(fixture.Database);

        Assert.Equal(Pair, await PairOfAsync(fixture.Database));
        (await admin.Modules.SwitchAsync(ModuleCodes.Sales, true, why)).ShouldBeOk();
        var rules = (await admin.Settings.ListRulesAsync()).Data<List<Ims.Application.Features.Settings.ApprovalRuleDto>>().Where(r => r.DocType == DocumentType.SalesOrder).ToList();
        Assert.Equal(2, rules.Count); // and the rules are there to edit in Settings
        await Ims.Api.Tests.Tests.Sales.SalesScenario.ResetAsync(fixture);
    }
}
