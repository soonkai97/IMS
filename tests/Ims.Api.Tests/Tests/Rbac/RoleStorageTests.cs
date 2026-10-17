using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Api.Tests.Tests.PublicIds;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// How roles and their permissions are stored (RP-2): unique code, name and grant among live rows (free again after a delete), no row removed from under its users, and row-level
/// audit rows that name a role by its public id. A migrated throw-away database; no endpoint writes roles yet, so the tests write through the context like the API will.
/// </summary>
public class RoleStorageTests(PublicIdDatabase fixture) : IClassFixture<PublicIdDatabase>
{
    private static Role NewRole(string? code = null, string? name = null, params string[] permissions) =>
        Role.Create(code ?? Unique.Code("R"), name ?? Unique.Name("Role"), "A test role.", false, permissions);

    private async Task<Role> AddAsync(Role role)
    {
        await using var db = fixture.NewContext();
        db.Add(role);
        await db.SaveChangesAsync();
        return role;
    }

    [Fact]
    public async Task A_role_is_saved_with_its_grants_and_read_back()
    {
        var saved = await AddAsync(NewRole(null, null, Permissions.StockRead, Permissions.ReportsRead));

        await using var db = fixture.NewContext();
        var read = await db.Set<Role>().Include(r => r.Grants).SingleAsync(r => r.Id == saved.Id);

        Assert.Equal(saved.PublicId, read.PublicId);
        Assert.False(read.IsSystem);
        Assert.True(read.IsActive);
        Assert.Equal([Permissions.ReportsRead, Permissions.StockRead], read.Grants.Select(g => g.PermissionCode).Order(StringComparer.Ordinal));
        Assert.All(read.Grants, g => Assert.Equal(read.Id, g.RoleId));
    }

    [Fact]
    public async Task Saving_a_role_writes_audit_rows_that_name_the_role_by_its_public_id_and_each_grant_by_its_key()
    {
        var saved = await AddAsync(NewRole(null, null, Permissions.StockRead));
        var grantId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM RolePermission WHERE RoleId = @r", ("@r", saved.Id));

        Assert.Equal(1, await fixture.Database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM AuditLogs WHERE EntityType = N'Role' AND Action = N'created' AND EntityId = @id", ("@id", saved.PublicId.ToString())));
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE EntityType = N'Role' AND EntityId = @id", ("@id", saved.Id.ToString())));

        var grant = await fixture.Database.ScalarAsync<string>(
            "SELECT [After] FROM AuditLogs WHERE EntityType = N'RolePermission' AND EntityId = @id", ("@id", grantId.ToString()));
        Assert.Contains(Permissions.StockRead, grant);
        Assert.Contains(saved.Code, await fixture.Database.ScalarAsync<string>("SELECT [After] FROM AuditLogs WHERE EntityType = N'Role' AND EntityId = @id", ("@id", saved.PublicId.ToString())));
    }

    [Fact]
    public async Task Deleting_a_role_is_a_soft_delete_that_frees_its_code_and_name()
    {
        var role = await AddAsync(NewRole());
        await using (var db = fixture.NewContext())
        {
            db.Remove(await db.Set<Role>().SingleAsync(r => r.Id == role.Id));
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT REC_DELETED FROM Role WHERE Id = @i", ("@i", role.Id)));
        await using (var db = fixture.NewContext())
            Assert.Null(await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == role.Id)); // hidden from every query

        var again = await AddAsync(NewRole(role.Code, role.Name)); // same code and name, now free

        Assert.NotEqual(role.PublicId, again.PublicId);
    }

    [Fact]
    public async Task Two_live_roles_cannot_share_a_code_or_a_name_whatever_the_case()
    {
        var first = await AddAsync(NewRole());

        await using var sameCode = fixture.NewContext();
        sameCode.Add(NewRole(first.Code.ToLowerInvariant()));
        Assert.Contains("IX_Role_Code", (await Assert.ThrowsAsync<DbUpdateException>(() => sameCode.SaveChangesAsync())).InnerException!.Message);

        await using var sameName = fixture.NewContext();
        sameName.Add(NewRole(null, first.Name.ToUpperInvariant()));
        Assert.Contains("IX_Role_Name", (await Assert.ThrowsAsync<DbUpdateException>(() => sameName.SaveChangesAsync())).InnerException!.Message);
    }

    [Fact]
    public async Task A_role_holds_a_permission_once_among_live_rows()
    {
        var role = await AddAsync(NewRole(null, null, Permissions.StockRead));

        await fixture.Database.ExecuteAsync("UPDATE RolePermission SET REC_DELETED = 1 WHERE RoleId = @r", ("@r", role.Id)); // revoked ...
        await fixture.Database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) VALUES (@r, @p)", ("@r", role.Id), ("@p", Permissions.StockRead)); // ... and granted again

        var failure = await Assert.ThrowsAsync<SqlException>(() =>
            fixture.Database.ExecuteAsync("INSERT RolePermission (RoleId, PermissionCode) VALUES (@r, @p)", ("@r", role.Id), ("@p", Permissions.StockRead)));
        Assert.Contains("IX_RolePermission_RoleId_PermissionCode", failure.Message);
    }

    [Fact]
    public async Task A_role_that_users_or_grants_refer_to_cannot_be_removed_from_under_them()
    {
        // A role with grants keeps them ...
        var failure = await Assert.ThrowsAsync<SqlException>(() => fixture.Database.ExecuteAsync("DELETE FROM Role WHERE Code = @c", ("@c", Roles.Clerk)));
        Assert.Contains("FK_RolePermission_Role_RoleId", failure.Message);

        // ... and a role a user has stays too, even without grants.
        var held = await AddAsync(NewRole());
        await fixture.Database.ExecuteAsync(
            "INSERT [User] (Username, Email, DisplayName, PasswordHash, RoleId, AllWarehouses, IsActive, FailedLoginCount) VALUES (@u, @e, @u, N'x', @r, 0, 1, 0)",
            ("@u", Unique.Username()), ("@e", Unique.Email(Unique.Username())), ("@r", held.Id));
        var userFailure = await Assert.ThrowsAsync<SqlException>(() => fixture.Database.ExecuteAsync("DELETE FROM Role WHERE Id = @r", ("@r", held.Id)));
        Assert.Contains("FK_User_Role_RoleId", userFailure.Message);
    }

    [Fact]
    public async Task A_user_without_a_role_row_cannot_be_written()
    {
        var failure = await Assert.ThrowsAsync<SqlException>(() => fixture.Database.ExecuteAsync(
            "INSERT [User] (Username, Email, DisplayName, PasswordHash, RoleId, AllWarehouses, IsActive, FailedLoginCount) VALUES (N'orphan', N'orphan@example.test', N'orphan', N'x', -1, 0, 1, 0)"));

        Assert.Contains("FK_User_Role_RoleId", failure.Message);
    }
}
