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
/// Migration <c>GrantReturnsPermissions</c> (PL3.15, PL3-M6): data only. Admin, Supervisor, Warehouse staff and Clerk gain <c>returns.create</c>; the Owner (no <c>document.create</c>) does not; a role gets a row
/// only while it holds <c>document.create</c> and <c>stock.read</c>; nothing else changes (no schema). A fresh database and an upgraded one end up with the same rows, running it twice adds nothing, and it
/// goes down (every grant of the code goes, whichever role holds it) and up again, as the runner and as the script a DBA generates, also with <c>QUOTED_IDENTIFIER OFF</c>. Each test has its own throw-away database.
/// </summary>
public class GrantReturnsPermissionsMigrationTests
{
    private const string Before = "20261108132822_AddPriceLists";
    private const string Latest = "20261109035255_GrantReturnsPermissions";

    private static readonly string[] Holders = [Roles.Admin, Roles.Supervisor, Roles.WarehouseStaff, Roles.Clerk];

    private static async Task MigrateAsync(TestDatabase database, string target)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options, new FrozenClock());
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task RunScriptAsync(TestDatabase database, string from, string to, string sessionPrefix = "")
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options, new FrozenClock());
        foreach (var batch in Regex.Split(db.GetService<IMigrator>().GenerateScript(from, to), @"^\s*GO\s*$", RegexOptions.Multiline))
            if (!string.IsNullOrWhiteSpace(batch)) await database.ExecuteAsync(sessionPrefix + batch);
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
        var joined = await database.ScalarAsync<string>("SELECT STRING_AGG(p.PermissionCode, ';') FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @c AND p.REC_DELETED = 0", ("@c", roleCode));
        return (joined ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    private static Task<int> GrantsOfAsync(TestDatabase database) => database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission WHERE PermissionCode = @c", ("@c", Permissions.ReturnsCreate));

    private static Task<int> TotalGrantsAsync(TestDatabase database) => database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission");

    private static Task TakeAsync(TestDatabase database, string roleCode, string permission, bool revokedNotDeleted) => database.ExecuteAsync(
        revokedNotDeleted
            ? "UPDATE p SET REC_DELETED = 1 FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @r AND p.PermissionCode = @c"
            : "DELETE p FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @r AND p.PermissionCode = @c", ("@r", roleCode), ("@c", permission));

    [Fact]
    public Task The_four_roles_that_create_documents_gain_exactly_the_plan_defaults_and_every_other_row_is_as_it_was() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        var grantsBefore = await TotalGrantsAsync(database);
        Assert.Equal(0, await GrantsOfAsync(database));

        await MigrateAsync(database, Latest);

        Assert.Equal(Holders.Length, await GrantsOfAsync(database));
        Assert.Equal(grantsBefore + Holders.Length, await TotalGrantsAsync(database));
        Assert.True((await PermissionsOfAsync(database, Roles.Admin)).SetEquals(PermissionCatalog.Codes), "Admin must hold every permission of the catalog.");
        foreach (var role in Roles.All.Where(r => r != Roles.Admin))
            Assert.True((await PermissionsOfAsync(database, role)).SetEquals(SystemRoleDefaults.For(role)), $"{role} must hold what the defaults say, and nothing more.");
        Assert.DoesNotContain(Permissions.ReturnsCreate, await PermissionsOfAsync(database, Roles.Owner));
    });

    [Theory]
    [InlineData(Roles.Supervisor, Permissions.DocumentCreate, false)]
    [InlineData(Roles.Clerk, Permissions.DocumentCreate, true)]
    [InlineData(Roles.WarehouseStaff, Permissions.StockRead, false)]
    [InlineData(Roles.Clerk, Permissions.StockRead, true)]
    public Task A_role_without_document_create_or_stock_read_gets_nothing_because_the_permission_requires_both(string role, string missing, bool revokedNotDeleted) => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await TakeAsync(database, role, missing, revokedNotDeleted);

        await MigrateAsync(database, Latest);

        Assert.DoesNotContain(Permissions.ReturnsCreate, await PermissionsOfAsync(database, role));
        Assert.Equal(Holders.Length - 1, await GrantsOfAsync(database));
        Assert.Contains(Permissions.ReturnsCreate, await PermissionsOfAsync(database, Roles.Admin));
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
        await database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code IN (@a, @s)", ("@c", Permissions.ReturnsCreate), ("@a", Roles.Admin), ("@s", Roles.Supervisor));

        await MigrateAsync(database, Latest);

        Assert.Equal(Holders.Length, await GrantsOfAsync(database));
    });

    [Fact]
    public Task It_goes_down_and_up_again_and_down_takes_every_grant_of_the_code_whichever_role_holds_it() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);
        await database.ExecuteAsync("INSERT Role (Code, Name, Description, IsSystem, IsActive) VALUES (N'Returner', N'Returner', N'Returns', 0, 1)");
        await database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) SELECT Id, @c FROM Role WHERE Code = N'Returner'", ("@c", Permissions.ReturnsCreate));
        Assert.Equal(Holders.Length + 1, await GrantsOfAsync(database));
        var otherGrants = await TotalGrantsAsync(database) - Holders.Length - 1;

        await MigrateAsync(database, Before);

        Assert.Equal(0, await GrantsOfAsync(database));
        Assert.Equal(otherGrants, await TotalGrantsAsync(database));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));

        await MigrateAsync(database, Latest);
        Assert.Equal(Holders.Length, await GrantsOfAsync(database));
    });

    [Fact]
    public Task The_generated_script_does_what_the_runner_does_up_and_down_even_with_quoted_identifier_off() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);

        await RunScriptAsync(database, Before, Latest, "SET QUOTED_IDENTIFIER OFF;\n");
        Assert.Equal(Holders.Length, await GrantsOfAsync(database));

        await RunScriptAsync(database, Latest, Before, "SET QUOTED_IDENTIFIER OFF;\n");
        Assert.Equal(0, await GrantsOfAsync(database));

        await RunScriptAsync(database, Before, Latest);
        Assert.Equal(Holders.Length, await GrantsOfAsync(database));
    });
}
