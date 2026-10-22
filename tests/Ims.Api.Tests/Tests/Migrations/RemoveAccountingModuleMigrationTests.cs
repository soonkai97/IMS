using System.Text.RegularExpressions;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// Migration <c>RemoveAccountingModule</c> (part A of the removal of the accounting module): data only. Every <c>RolePermission</c> row of the code <c>accounting.export</c> (any role, live or
/// soft-deleted) and the module switch row <c>Module.accounting</c> are deleted in one batch, nothing else changes (no other permission, no other switch, no schema, not the tax columns),
/// running it again deletes nothing, and Down does nothing (the rows are not recorded anywhere, so they are not restored). A fresh database ends with no trace of the module. Each test has its
/// own throw-away database. The code strings are FROZEN here: neither is in the live catalog any more.
/// </summary>
public class RemoveAccountingModuleMigrationTests
{
    private const string Before = "20261021100000_AddReceiptPurchaseOrderLinks";
    private const string Latest = "20261022122230_RemoveAccountingModule";
    private const string AccountingExport = "accounting.export";
    private const string AccountingSwitch = "Module.accounting";
    private const string PurchasingSwitch = "Module.purchasing";

    private static async Task MigrateAsync(TestDatabase database, string target)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>The script a DBA generates and runs by hand, one batch per GO: it must do what the migration runner does.</summary>
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

    private static Task<int> ExportGrantsAsync(TestDatabase database) =>
        database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission WHERE PermissionCode = @c", ("@c", AccountingExport));

    private static Task<int> OtherGrantsAsync(TestDatabase database) =>
        database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission WHERE PermissionCode <> @c", ("@c", AccountingExport));

    private static Task<int> SwitchRowsAsync(TestDatabase database, string key) =>
        database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = @k", ("@k", key));

    /// <summary>What a module-era installation holds at the previous migration: the grants of both system roles (from GrantAccountingExport), a custom role with one, a soft-deleted one, and both switches.</summary>
    private static async Task SeedAsync(TestDatabase database)
    {
        await database.ExecuteAsync("INSERT Role (Code, Name, Description, IsSystem, IsActive) VALUES (N'Auditor', N'Auditor', N'Reads', 0, 1)");
        await database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code = N'Auditor'", ("@c", AccountingExport));
        await database.ExecuteAsync("INSERT Role (Code, Name, Description, IsSystem, IsActive) VALUES (N'Former', N'Former', N'Once had it', 0, 1)");
        await database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode, REC_DELETED) SELECT Id, @c, 1 FROM Role WHERE Code = N'Former'", ("@c", AccountingExport));
        await database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (@k, N'true'), (@p, N'true')", ("@k", AccountingSwitch), ("@p", PurchasingSwitch));
    }

    [Fact]
    public Task Every_grant_of_the_permission_and_the_module_switch_are_deleted_and_nothing_else_changes() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await SeedAsync(database);
        Assert.Equal(4, await ExportGrantsAsync(database)); // Admin and Owner (GrantAccountingExport), the custom role, and the soft-deleted row of another
        var others = await OtherGrantsAsync(database);
        var settings = await database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting");

        await MigrateAsync(database, Latest);

        Assert.Equal(0, await ExportGrantsAsync(database)); // live and soft-deleted, system and custom
        Assert.Equal(others, await OtherGrantsAsync(database));
        Assert.Equal(0, await SwitchRowsAsync(database, AccountingSwitch));
        Assert.Equal(1, await SwitchRowsAsync(database, PurchasingSwitch)); // another module's switch is not touched
        Assert.Equal(settings - 1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Role WHERE Code IN (N'Auditor', N'Former')")); // no role is removed
    });

    [Fact]
    public Task A_database_with_neither_the_grants_nor_the_switch_migrates_without_a_change() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await database.ExecuteAsync("DELETE FROM RolePermission WHERE PermissionCode = @c", ("@c", AccountingExport));
        var grants = await OtherGrantsAsync(database);

        await MigrateAsync(database, Latest);

        Assert.Equal(grants, await OtherGrantsAsync(database));
        Assert.Equal(0, await ExportGrantsAsync(database));
        Assert.Equal(0, await SwitchRowsAsync(database, AccountingSwitch));
    });

    [Fact]
    public Task The_four_tax_columns_are_not_touched_that_is_part_B() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        var columns = await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id IN (OBJECT_ID(N'Product'), OBJECT_ID(N'Partner')) AND name IN (N'HsCode', N'SstCode', N'Tin', N'SstRegNo')");

        await MigrateAsync(database, Latest);

        Assert.Equal(4, columns);
        Assert.Equal(4, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id IN (OBJECT_ID(N'Product'), OBJECT_ID(N'Partner')) AND name IN (N'HsCode', N'SstCode', N'Tin', N'SstRegNo')"));
    });

    [Fact]
    public Task Down_does_nothing_and_running_up_again_is_safe() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await SeedAsync(database);
        await MigrateAsync(database, Latest);
        var others = await OtherGrantsAsync(database);

        await MigrateAsync(database, Before); // down: the deleted rows are not restored

        Assert.Equal(0, await ExportGrantsAsync(database));
        Assert.Equal(0, await SwitchRowsAsync(database, AccountingSwitch));
        Assert.Equal(1, await SwitchRowsAsync(database, PurchasingSwitch));
        Assert.Equal(others, await OtherGrantsAsync(database));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));

        await database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code = N'Auditor'", ("@c", AccountingExport)); // rows that came back meanwhile
        await MigrateAsync(database, Latest); // and up again: it deletes them again, and fails on nothing

        Assert.Equal(0, await ExportGrantsAsync(database));
        Assert.Equal(others, await OtherGrantsAsync(database));
    });

    [Fact]
    public Task The_generated_script_does_what_the_runner_does_up_and_down() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await SeedAsync(database);

        await RunScriptAsync(database, Before, Latest);
        Assert.Equal(0, await ExportGrantsAsync(database));
        Assert.Equal(0, await SwitchRowsAsync(database, AccountingSwitch));
        Assert.Equal(1, await SwitchRowsAsync(database, PurchasingSwitch));

        await RunScriptAsync(database, Latest, Before);
        Assert.Equal(0, await ExportGrantsAsync(database)); // nothing comes back
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));
    });

    [Fact]
    public Task A_fresh_database_holds_no_trace_of_the_module_and_the_administrator_holds_the_whole_catalog() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);

        Assert.Equal(0, await ExportGrantsAsync(database));
        Assert.Equal(0, await SwitchRowsAsync(database, AccountingSwitch));
        var admin = await database.ScalarAsync<string>("SELECT STRING_AGG(p.PermissionCode, ';') FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @c AND p.REC_DELETED = 0", ("@c", Roles.Admin));
        Assert.True(admin.Split(';').ToHashSet().SetEquals(PermissionCatalog.Codes), "Admin must hold every permission of the catalog (24), and nothing else.");
    });
}
