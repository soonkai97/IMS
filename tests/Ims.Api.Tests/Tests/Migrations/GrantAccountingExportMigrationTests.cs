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
/// Migration <c>GrantAccountingExport</c> (PL1.2): data only. The Administrator and Owner roles gain <c>accounting.export</c> and nothing else changes: no other role, no schema. A fresh
/// database and an upgraded one end up with the same rows, running it twice adds nothing, and it goes down (every grant of the code goes, whichever role holds it) and up again, as
/// the runner and as the script a DBA generates. Each test has its own throw-away database.
/// </summary>
public class GrantAccountingExportMigrationTests
{
    private const string Before = "20261019133035_GrantModulesManage";
    private const string Latest = "20261019145353_GrantAccountingExport";

    private static readonly string[] Holders = [Roles.Admin, Roles.Owner];

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

    private static async Task<IReadOnlySet<string>> PermissionsOfAsync(TestDatabase database, string roleCode)
    {
        var joined = await database.ScalarAsync<string>(
            "SELECT STRING_AGG(p.PermissionCode, ';') FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @c AND p.REC_DELETED = 0", ("@c", roleCode));
        return (joined ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    private static Task<int> GrantsOfAsync(TestDatabase database) =>
        database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission WHERE PermissionCode = @c", ("@c", Permissions.AccountingExport));

    private static Task<int> TotalGrantsAsync(TestDatabase database) => database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission");

    [Fact]
    public Task Only_the_Administrator_and_the_Owner_gain_the_permission_and_every_other_row_is_as_it_was() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        var grantsBefore = await TotalGrantsAsync(database);
        Assert.Equal(0, await GrantsOfAsync(database));
        foreach (var role in Holders)
            Assert.DoesNotContain(Permissions.AccountingExport, await PermissionsOfAsync(database, role));

        await MigrateAsync(database, Latest);

        Assert.Equal(2, await GrantsOfAsync(database));
        Assert.Equal(grantsBefore + 2, await TotalGrantsAsync(database));
        Assert.True((await PermissionsOfAsync(database, Roles.Admin)).SetEquals(PermissionCatalog.Codes), "Admin must hold every permission of the catalog.");
        foreach (var role in Roles.All.Where(r => r != Roles.Admin))
            Assert.True((await PermissionsOfAsync(database, role)).SetEquals(SystemRoleDefaults.For(role)), $"{role} must hold what the defaults say, and nothing more.");
        Assert.Contains(Permissions.AccountingExport, await PermissionsOfAsync(database, Roles.Owner));
        foreach (var role in new[] { Roles.Supervisor, Roles.WarehouseStaff, Roles.Clerk })
            Assert.DoesNotContain(Permissions.AccountingExport, await PermissionsOfAsync(database, role));
    });

    [Fact]
    public Task A_fresh_database_and_an_upgraded_one_hold_the_same_rows() => WithDatabaseAsync(async upgraded =>
    {
        var fresh = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(upgraded, Before);
            await MigrateAsync(upgraded, Latest);
            await MigrateAsync(fresh, Latest);

            foreach (var role in Roles.All)
                Assert.True((await PermissionsOfAsync(fresh, role)).SetEquals(await PermissionsOfAsync(upgraded, role)), role);
            Assert.Equal(await TotalGrantsAsync(upgraded), await TotalGrantsAsync(fresh));
        }
        finally
        {
            await fresh.DropAsync();
        }
    });

    [Fact]
    public Task A_grant_that_is_already_there_is_not_added_twice() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code IN (@a, @o)", ("@c", Permissions.AccountingExport), ("@a", Roles.Admin), ("@o", Roles.Owner));

        await MigrateAsync(database, Latest);

        Assert.Equal(2, await GrantsOfAsync(database));
    });

    [Fact]
    public Task It_goes_down_and_up_again_and_down_takes_every_grant_of_the_code_whichever_role_holds_it() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);
        await database.ExecuteAsync("INSERT Role (Code, Name, Description, IsSystem, IsActive) VALUES (N'Auditor', N'Auditor', N'Reads', 0, 1)");
        await database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code = N'Auditor'", ("@c", Permissions.AccountingExport));
        Assert.Equal(3, await GrantsOfAsync(database));
        var otherGrants = await TotalGrantsAsync(database) - 3;

        await MigrateAsync(database, Before); // down: the previous release does not know the code, so no role keeps a row for it

        Assert.Equal(0, await GrantsOfAsync(database));
        Assert.Equal(otherGrants, await TotalGrantsAsync(database));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));

        await MigrateAsync(database, Latest); // and up again: only the two system roles

        Assert.Equal(2, await GrantsOfAsync(database));
        foreach (var role in Holders)
            Assert.Contains(Permissions.AccountingExport, await PermissionsOfAsync(database, role));
    });

    [Fact]
    public Task The_generated_script_does_what_the_runner_does_up_and_down() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);

        await RunScriptAsync(database, Before, Latest);
        Assert.Equal(2, await GrantsOfAsync(database));

        await RunScriptAsync(database, Latest, Before);
        Assert.Equal(0, await GrantsOfAsync(database));

        await RunScriptAsync(database, Before, Latest);
        Assert.Equal(2, await GrantsOfAsync(database));
    });
}
