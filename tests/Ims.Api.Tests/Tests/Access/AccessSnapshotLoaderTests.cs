using Ims.Api.Tests.Support;
using Ims.Api.Tests.Tests.PublicIds;
using Ims.Application.Features.Auth;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>
/// The snapshot loader against a real migrated SQL Server database (the five seeded system roles): the permissions come from the role's rows, the scope from the user,
/// and a user or role that is not live gives no snapshot. A throw-away database, so rows may be added and changed freely.
/// </summary>
public class AccessSnapshotLoaderTests(PublicIdDatabase fixture) : IClassFixture<PublicIdDatabase>
{
    private async Task<AccessSnapshot?> LoadAsync(int userId)
    {
        await using var db = fixture.NewContext();
        return await new AccessSnapshotLoader(new Repository<User>(db), new Repository<Role>(db)).LoadAsync(userId, default);
    }

    private async Task<User> AddUserAsync(string roleCode, bool isActive = true, IEnumerable<int>? warehouseIds = null)
    {
        await using var db = fixture.NewContext();
        var role = await db.Set<Role>().SingleAsync(r => r.Code == roleCode);
        var username = Unique.Username("snap");
        var user = User.Create(username, Unique.Email(username), username, role, "hash", warehouseIds, isActive);
        db.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<Role> AddRoleAsync(params string[] permissions)
    {
        await using var db = fixture.NewContext();
        var role = Role.Create(Unique.Code("R"), Unique.Name("Role"), null, false, permissions);
        db.Add(role);
        await db.SaveChangesAsync();
        return role;
    }

    private async Task<int> AddWarehouseAsync()
    {
        await using var db = fixture.NewContext();
        var warehouse = Warehouse.Create(Unique.Code("W"), Unique.Name("Warehouse"));
        db.Add(warehouse);
        await db.SaveChangesAsync();
        return warehouse.Id;
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task A_system_role_gives_exactly_its_default_permissions_and_the_scope_of_the_old_rule(string roleCode)
    {
        var user = await AddUserAsync(roleCode);

        var snapshot = await LoadAsync(user.Id);

        Assert.NotNull(snapshot);
        Assert.Equal(user.Id, snapshot.UserId);
        Assert.Equal(roleCode, snapshot.RoleCode);
        Assert.Equal(SystemRoleDefaults.For(roleCode).Order(StringComparer.Ordinal), snapshot.Permissions.Codes.Order(StringComparer.Ordinal));
        Assert.Equal(Roles.SeesAllWarehouses(roleCode), snapshot.AllWarehouses);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.RoleName));
    }

    [Fact]
    public async Task The_warehouses_of_the_user_are_in_the_snapshot()
    {
        var first = await AddWarehouseAsync();
        var second = await AddWarehouseAsync();
        var user = await AddUserAsync(Roles.WarehouseStaff, warehouseIds: [first, second]);

        var snapshot = await LoadAsync(user.Id);

        Assert.Equal(new[] { first, second }.Order(), snapshot!.WarehouseIds.Order());
    }

    [Fact]
    public async Task A_custom_role_gives_its_grants_and_nothing_else()
    {
        var role = await AddRoleAsync(Permissions.StockRead, Permissions.ReportsRead);
        var user = await AddUserWithRoleAsync(role);

        var snapshot = await LoadAsync(user.Id);

        Assert.Equal(role.Code, snapshot!.RoleCode);
        Assert.Equal([Permissions.ReportsRead, Permissions.StockRead], snapshot.Permissions.Codes.Order(StringComparer.Ordinal));
        Assert.False(snapshot.Has(Permissions.DocumentApprove));
    }

    [Fact]
    public async Task A_grant_the_catalog_does_not_know_gives_nothing()
    {
        var role = await AddRoleAsync(Permissions.StockRead);
        await fixture.Database.ExecuteAsync("INSERT INTO RolePermission (RoleId, PermissionCode) VALUES (@r, N'retired.permission')", ("@r", role.Id));
        var user = await AddUserWithRoleAsync(role);

        var snapshot = await LoadAsync(user.Id);

        Assert.Equal([Permissions.StockRead], snapshot!.Permissions.Codes);
    }

    [Fact]
    public async Task A_revoked_grant_is_not_counted()
    {
        var role = await AddRoleAsync(Permissions.StockRead, Permissions.ReportsRead);
        await fixture.Database.ExecuteAsync("UPDATE RolePermission SET REC_DELETED = 1 WHERE RoleId = @r AND PermissionCode = @p", ("@r", role.Id), ("@p", Permissions.ReportsRead));
        var user = await AddUserWithRoleAsync(role);

        var snapshot = await LoadAsync(user.Id);

        Assert.Equal([Permissions.StockRead], snapshot!.Permissions.Codes);
    }

    [Fact]
    public async Task A_user_who_is_not_active_has_no_snapshot()
    {
        var user = await AddUserAsync(Roles.Admin, isActive: false);

        Assert.Null(await LoadAsync(user.Id));
    }

    [Fact]
    public async Task A_user_who_does_not_exist_has_no_snapshot()
    {
        Assert.Null(await LoadAsync(int.MaxValue));
    }

    [Fact]
    public async Task A_user_whose_role_is_gone_has_no_snapshot_rather_than_a_default_one()
    {
        var role = await AddRoleAsync(Permissions.StockRead);
        var user = await AddUserWithRoleAsync(role);
        await fixture.Database.ExecuteAsync("UPDATE [Role] SET REC_DELETED = 1 WHERE Id = @r", ("@r", role.Id));

        Assert.Null(await LoadAsync(user.Id));
    }

    [Fact]
    public async Task A_user_whose_role_is_inactive_has_no_snapshot()
    {
        var role = await AddRoleAsync(Permissions.StockRead);
        var user = await AddUserWithRoleAsync(role);
        await fixture.Database.ExecuteAsync("UPDATE [Role] SET IsActive = 0 WHERE Id = @r", ("@r", role.Id));

        Assert.Null(await LoadAsync(user.Id));
    }

    [Fact]
    public async Task A_change_to_the_role_shows_in_the_next_load()
    {
        var role = await AddRoleAsync(Permissions.StockRead);
        var user = await AddUserWithRoleAsync(role);
        Assert.False((await LoadAsync(user.Id))!.Has(Permissions.ReportsRead));

        await fixture.Database.ExecuteAsync("INSERT INTO RolePermission (RoleId, PermissionCode) VALUES (@r, @p)", ("@r", role.Id), ("@p", Permissions.ReportsRead));

        Assert.True((await LoadAsync(user.Id))!.Has(Permissions.ReportsRead));
    }

    /// <summary>The domain still refuses a role outside the five (RP-5 lifts that), so the user is made with a system role and pointed at the custom one in the database.</summary>
    private async Task<User> AddUserWithRoleAsync(Role role)
    {
        var user = await AddUserAsync(Roles.Clerk);
        await fixture.Database.ExecuteAsync("UPDATE [User] SET RoleId = @r WHERE Id = @u", ("@r", role.Id), ("@u", user.Id));
        return user;
    }
}
