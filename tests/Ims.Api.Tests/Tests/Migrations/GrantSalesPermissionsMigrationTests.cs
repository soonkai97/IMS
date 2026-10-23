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
/// Migration <c>GrantSalesPermissions</c> (PL3.1): data only. Admin, Owner, Supervisor and Clerk gain <c>so.read</c> and <c>so.manage</c>; Warehouse staff gain <c>so.read</c> only; a role gets a row
/// only while it holds <c>masterdata.read</c> (<c>so.manage</c> needs <c>so.read</c> only, not <c>cost.view</c>); nothing else changes (no schema). A fresh database and an upgraded one end up with
/// the same rows, running it twice adds nothing, and it goes down (every grant of both codes goes, whichever role holds it) and up again, as the runner and as the script a DBA generates.
/// Each test has its own throw-away database.
/// </summary>
public class GrantSalesPermissionsMigrationTests
{
    private const string Before = "20261022141734_RemoveTaxReadyFields";
    private const string Latest = "20261023100000_GrantSalesPermissions";

    private static readonly string[] Codes = [Permissions.SoRead, Permissions.SoManage];
    private static readonly string[] Managers = [Roles.Admin, Roles.Owner, Roles.Supervisor, Roles.Clerk];
    private static readonly string[] Readers = [Roles.WarehouseStaff];

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
        database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission WHERE PermissionCode IN (@a, @b)", ("@a", Codes[0]), ("@b", Codes[1]));

    private static Task<int> TotalGrantsAsync(TestDatabase database) => database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission");

    /// <summary>Takes a permission from a system role the way an administrator did on a populated database: the row is deleted, or revoked (soft-deleted).</summary>
    private static Task TakeAsync(TestDatabase database, string roleCode, string permission, bool revokedNotDeleted) => database.ExecuteAsync(
        revokedNotDeleted
            ? "UPDATE p SET REC_DELETED = 1 FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @r AND p.PermissionCode = @c"
            : "DELETE p FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @r AND p.PermissionCode = @c", ("@r", roleCode), ("@c", permission));

    [Fact]
    public Task The_five_system_roles_gain_exactly_the_plan_defaults_and_every_other_row_is_as_it_was() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        var grantsBefore = await TotalGrantsAsync(database);
        Assert.Equal(0, await GrantsOfAsync(database));

        await MigrateAsync(database, Latest);

        var expected = Managers.Length * 2 + Readers.Length;
        Assert.Equal(9, expected); // 4 x 2 + 1 x 1
        Assert.Equal(expected, await GrantsOfAsync(database));
        Assert.Equal(grantsBefore + expected, await TotalGrantsAsync(database));
        Assert.True((await PermissionsOfAsync(database, Roles.Admin)).SetEquals(PermissionCatalog.Codes), "Admin must hold every permission of the catalog.");
        foreach (var role in Roles.All.Where(r => r != Roles.Admin))
            Assert.True((await PermissionsOfAsync(database, role)).SetEquals(SystemRoleDefaults.For(role)), $"{role} must hold what the defaults say, and nothing more.");
        foreach (var role in Managers)
            Assert.True((await PermissionsOfAsync(database, role)).IsSupersetOf(Codes), role);
        foreach (var role in Readers)
        {
            var held = await PermissionsOfAsync(database, role);
            Assert.Contains(Permissions.SoRead, held);
            Assert.DoesNotContain(Permissions.SoManage, held);
        }
    });

    [Theory]
    [InlineData(Roles.Supervisor, false)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Clerk, true)]
    [InlineData(Roles.Owner, false)]
    public Task A_role_without_cost_view_still_gets_so_manage_because_a_selling_price_is_not_cost(string role, bool revokedNotDeleted) => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await TakeAsync(database, role, Permissions.CostView, revokedNotDeleted);
        Assert.DoesNotContain(Permissions.CostView, await PermissionsOfAsync(database, role));

        await MigrateAsync(database, Latest);

        var held = await PermissionsOfAsync(database, role);
        Assert.Contains(Permissions.SoRead, held);
        Assert.Contains(Permissions.SoManage, held);
        Assert.DoesNotContain(Permissions.CostView, held); // nothing else is given back
    });

    [Theory]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, true)]
    public Task A_role_without_master_data_read_gets_neither_code_because_so_read_requires_it(string role, bool revokedNotDeleted) => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await TakeAsync(database, role, Permissions.MasterDataRead, revokedNotDeleted);

        await MigrateAsync(database, Latest);

        var held = await PermissionsOfAsync(database, role);
        Assert.DoesNotContain(Permissions.SoRead, held);
        Assert.DoesNotContain(Permissions.SoManage, held);
        Assert.Contains(Permissions.SoRead, await PermissionsOfAsync(database, Roles.Admin));
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
        await database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code IN (@a, @o)", ("@c", Permissions.SoRead), ("@a", Roles.Admin), ("@o", Roles.Owner));

        await MigrateAsync(database, Latest);

        Assert.Equal(Managers.Length * 2 + Readers.Length, await GrantsOfAsync(database));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @a AND p.PermissionCode = @c", ("@a", Roles.Admin), ("@c", Permissions.SoRead)));
    });

    [Fact]
    public Task It_goes_down_and_up_again_and_down_takes_every_grant_of_both_codes_whichever_role_holds_them() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);
        await database.ExecuteAsync("INSERT Role (Code, Name, Description, IsSystem, IsActive) VALUES (N'Buyer', N'Buyer', N'Buys', 0, 1)");
        await database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code = N'Buyer'", ("@c", Permissions.SoManage));
        var system = Managers.Length * 2 + Readers.Length;
        Assert.Equal(system + 1, await GrantsOfAsync(database));
        var otherGrants = await TotalGrantsAsync(database) - system - 1;

        await MigrateAsync(database, Before); // down: the previous release does not know the codes, so no role keeps a row for them

        Assert.Equal(0, await GrantsOfAsync(database));
        Assert.Equal(otherGrants, await TotalGrantsAsync(database));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));

        await MigrateAsync(database, Latest); // and up again: only the system roles

        Assert.Equal(system, await GrantsOfAsync(database));
        foreach (var role in Managers)
            Assert.True((await PermissionsOfAsync(database, role)).IsSupersetOf(Codes), role);
    });

    [Fact]
    public Task The_generated_script_does_what_the_runner_does_up_and_down() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        var system = Managers.Length * 2 + Readers.Length;

        await RunScriptAsync(database, Before, Latest);
        Assert.Equal(system, await GrantsOfAsync(database));

        await RunScriptAsync(database, Latest, Before);
        Assert.Equal(0, await GrantsOfAsync(database));

        await RunScriptAsync(database, Before, Latest);
        Assert.Equal(system, await GrantsOfAsync(database));
    });
}
