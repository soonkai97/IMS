using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>
/// A user whose role has been deleted or deactivated cannot be given a token: sign-in and refresh read the rights first and answer 401 before anything is written,
/// so there is no orphan refresh-token row, no rotation of the old one and no success in the audit trail.
/// </summary>
[Collection(ApiCollection.Name)]
public class NoLiveRoleTests(ApiFixture fixture)
{
    private const string SoftDeleted = "REC_DELETED = 1";
    private const string Inactive = "IsActive = 0";
    private const string Restored = "REC_DELETED = 0, IsActive = 1";

    private readonly AccountFactory _accounts = new(fixture);

    /// <summary>A user on a custom role. The domain still refuses a role outside the five (RP-5 lifts that), so the user is made with a system role and pointed at the custom one.</summary>
    private async Task<(TestAccount Account, int UserId, int RoleId)> UserOnCustomRoleAsync()
    {
        int roleId;
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = Role.Create(Unique.Code("R"), Unique.Name("Role"), null, false, [Permissions.StockRead]);
            db.Add(role);
            await db.SaveChangesAsync();
            roleId = role.Id;
        }
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var userId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = @u", ("@u", account.Username));
        await fixture.Database.ExecuteAsync("UPDATE [User] SET RoleId = @r WHERE Id = @id", ("@r", roleId), ("@id", userId));
        return (account, userId, roleId);
    }

    private Task SetRoleAsync(int roleId, string assignments) =>
        fixture.Database.ExecuteAsync($"UPDATE [Role] SET {assignments} WHERE Id = @r", ("@r", roleId));

    private Task<int> TokensAsync(int userId, bool onlyActive = false) =>
        fixture.Database.ScalarAsync<int>($"SELECT COUNT(*) FROM RefreshToken WHERE UserId = @id{(onlyActive ? " AND RevokedAt IS NULL" : "")}", ("@id", userId));

    private Task<int> FailedLoginsAsync(int userId) =>
        fixture.Database.ScalarAsync<int>("SELECT FailedLoginCount FROM [User] WHERE Id = @id", ("@id", userId));

    private Task<int> SuccessesAsync(string username) =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Actor = @u AND Action = @a", ("@u", username), ("@a", AuditActions.LoginSuccess));

    [Theory]
    [InlineData(SoftDeleted)]
    [InlineData(Inactive)]
    public async Task Refresh_without_a_live_role_is_401_writes_no_row_and_does_not_rotate_the_old_token(string breakTheRole)
    {
        var (account, userId, roleId) = await UserOnCustomRoleAsync();
        var (client, auth) = await _accounts.SignInAsync(account);
        var rows = await TokensAsync(userId);
        await SetRoleAsync(roleId, breakTheRole);

        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeProblem(HttpStatusCode.Unauthorized);

        Assert.Equal(rows, await TokensAsync(userId));
        Assert.Equal(1, await TokensAsync(userId, onlyActive: true));
        await SetRoleAsync(roleId, Restored);
        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeOk(); // the same refresh token was never used up
    }

    [Theory]
    [InlineData(SoftDeleted)]
    [InlineData(Inactive)]
    public async Task Sign_in_without_a_live_role_is_401_with_no_refresh_token_and_no_success_in_the_audit_trail(string breakTheRole)
    {
        var (account, userId, roleId) = await UserOnCustomRoleAsync();
        await SetRoleAsync(roleId, breakTheRole);
        var successes = await SuccessesAsync(account.Username);
        await fixture.Anonymous().Auth.LoginAsync(account.Username, "wrong"); // two failures on record: a refusal for a dead role must not reset them
        await fixture.Anonymous().Auth.LoginAsync(account.Username, "wrong");
        var failures = await FailedLoginsAsync(userId);

        (await fixture.Anonymous().Auth.LoginAsync(account.Username, account.Password)).ShouldBeProblem(HttpStatusCode.Unauthorized);

        Assert.Equal(0, await TokensAsync(userId));
        Assert.Equal(failures, await FailedLoginsAsync(userId));
        Assert.Equal(2, failures);
        Assert.Equal(successes, await SuccessesAsync(account.Username));
        await SetRoleAsync(roleId, Restored);
        (await fixture.Anonymous().Auth.LoginAsync(account.Username, account.Password)).ShouldBeOk();
    }
}
