using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Users;
using Ims.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>
/// The access snapshot at the door (ADR roles-permissions 3.2, lean scope): the token says who, the snapshot says what. A role change applies to the token already issued,
/// a deactivated user is a 401, a token cannot carry rights, the database is read once per user and time, and a failed read is an error, never an allow.
/// Enforcement itself is unchanged: the endpoint policies still decide by the fixed map of the system roles.
/// </summary>
[Collection(ApiCollection.Name)]
public class AccessSnapshotRequestTests(ApiFixture fixture)
{
    private readonly AccountFactory _accounts = new(fixture);

    private async Task<ApiResponse> ChangeRoleAsync(TestAccount account, string role)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var scope = Roles.SeesAllWarehouses(role) ? [] : account.WarehouseIds.ToList();
        return await admin.Users.UpdateAsync(account.Id, new UserSaveRequest(account.Username, account.Email, account.Username, role, true, null, scope));
    }

    private Task<int> UserIdAsync(TestAccount account) =>
        fixture.Database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = @u", ("@u", account.Username));

    // ---------- a role change applies to the token already issued ----------

    [Fact]
    public async Task A_demotion_applies_to_the_access_token_already_issued_at_once()
    {
        var account = await _accounts.CreateAsync(Roles.Admin);
        var (client, _) = await _accounts.SignInAsync(account);
        (await client.Users.ListAsync()).ShouldBeOk();

        (await ChangeRoleAsync(account, Roles.Clerk)).ShouldBeOk();

        (await client.Users.ListAsync()).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_promotion_applies_to_the_access_token_already_issued_at_once()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, _) = await _accounts.SignInAsync(account);
        (await client.Users.ListAsync()).ShouldBe(HttpStatusCode.Forbidden);

        (await ChangeRoleAsync(account, Roles.Admin)).ShouldBeOk();

        (await client.Users.ListAsync()).ShouldBeOk();
    }

    [Fact]
    public async Task Reactivating_a_user_lets_the_same_token_in_again()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, _) = await _accounts.SignInAsync(account);
        (await _accounts.UpdateAsync(account, isActive: false)).ShouldBeOk();
        (await client.Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);

        (await _accounts.UpdateAsync(account, isActive: true)).ShouldBeOk();

        (await client.Warehouses.ListAsync()).ShouldBeOk();
    }

    // ---------- the token carries identity, nothing else ----------

    [Fact]
    public async Task A_role_claim_inside_a_valid_token_gains_nothing()
    {
        var clerk = await _accounts.CreateAsync(Roles.Clerk);
        var forged = ForgedTokens.Create(fixture.Settings.JwtKey, role: Roles.Admin, userId: await UserIdAsync(clerk));

        var client = fixture.CreateClient(forged);

        (await client.Warehouses.ListAsync()).ShouldBeOk(); // the token itself is genuine, and the person is who it says
        (await client.Users.ListAsync()).ShouldBe(HttpStatusCode.Forbidden); // but the role is the clerk's, not the one the token claims
    }

    [Fact]
    public async Task A_token_for_a_user_who_does_not_exist_is_401()
    {
        var token = ForgedTokens.Create(fixture.Settings.JwtKey, userId: int.MaxValue);

        (await fixture.CreateClient(token).Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_access_token_a_login_issues_names_the_person_and_carries_no_rights()
    {
        var account = await _accounts.CreateAsync(Roles.Supervisor);
        var (_, auth) = await _accounts.SignInAsync(account);

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken).Claims.Select(c => c.Type).ToList();

        Assert.Contains(JwtRegisteredClaimNames.Sub, claims);
        Assert.DoesNotContain(claims, type => type is "role" or "roles" or ClaimTypes.Role or "wh");
    }

    // ---------- what the person sees of themselves ----------

    [Fact]
    public async Task Me_names_the_role_and_says_whether_every_warehouse_is_seen()
    {
        var admin = (await (await fixture.AsAsync(Roles.Admin)).Auth.MeAsync()).Data<UserInfo>();
        var clerk = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await _accounts.SignInAsync(clerk);
        var me = (await client.Auth.MeAsync()).Data<UserInfo>();

        Assert.Equal(Roles.Admin, admin.Role);
        Assert.False(string.IsNullOrWhiteSpace(admin.RoleName));
        Assert.True(admin.SeesAllWarehouses);
        Assert.Equal(Roles.Clerk, me.Role);
        Assert.False(me.SeesAllWarehouses);
        Assert.Equal(me.RoleName, auth.User.RoleName); // sign-in and /me say the same
        Assert.Equal(SystemRoleDefaults.For(Roles.Clerk).Order(StringComparer.Ordinal), me.Permissions.Intersect(SystemRoleDefaults.For(Roles.Clerk)).Order(StringComparer.Ordinal));
    }

    // ---------- cost, freshness and failing closed (second hosts on the same database) ----------

    private sealed class LoaderSpy
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public void Count() => Interlocked.Increment(ref _calls);
    }

    private sealed class SpiedLoader(IAccessSnapshotLoader inner, LoaderSpy spy) : IAccessSnapshotLoader
    {
        public Task<AccessSnapshot?> LoadAsync(int userId, CancellationToken ct)
        {
            spy.Count();
            return inner.LoadAsync(userId, ct);
        }
    }

    private sealed class BrokenLoader : IAccessSnapshotLoader
    {
        public Task<AccessSnapshot?> LoadAsync(int userId, CancellationToken ct) => throw new InvalidOperationException("The database is down.");
    }

    [Fact]
    public async Task Many_requests_of_one_user_read_the_database_once()
    {
        var spy = new LoaderSpy();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(s => s.AddScoped<IAccessSnapshotLoader>(sp =>
            new SpiedLoader(ActivatorUtilities.CreateInstance<AccessSnapshotLoader>(sp), spy))));
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (signedIn, _) = await _accounts.SignInAsync(account);
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = signedIn.AccessToken;

        for (var i = 0; i < 10; i++) (await client.Warehouses.ListAsync()).ShouldBeOk();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ => (await client.Warehouses.ListAsync()).ShouldBeOk()));

        Assert.Equal(1, spy.Calls);
    }

    [Fact]
    public async Task A_failed_read_of_the_snapshot_is_a_server_error_never_an_allow()
    {
        await using var host = fixture.CreateHost(b => b.ConfigureServices(s => s.AddScoped<IAccessSnapshotLoader, BrokenLoader>()));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await fixture.AsAsync(Roles.Admin)).AccessToken;

        var response = await client.Warehouses.ListAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
    }

    [Fact]
    public async Task A_change_made_elsewhere_applies_within_the_time_to_live()
    {
        // A second instance has its own cache and is not told about the change; it must catch up on its own within Access:SnapshotTtlSeconds (1 second here, 15 by default).
        await using var host = fixture.CreateHost(b => b.UseSetting("Access:SnapshotTtlSeconds", "1"));
        var account = await _accounts.CreateAsync(Roles.Admin);
        var (signedIn, _) = await _accounts.SignInAsync(account);
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = signedIn.AccessToken;
        (await client.Users.ListAsync()).ShouldBeOk();

        await fixture.Database.ExecuteAsync(
            "UPDATE [User] SET Role = @code, RoleId = (SELECT Id FROM [Role] WHERE Code = @code), AllWarehouses = 0 WHERE Username = @u", ("@code", Roles.Clerk), ("@u", account.Username));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpStatusCode status;
        do
        {
            await Task.Delay(200);
            status = (await client.Users.ListAsync()).Status;
        } while (status == HttpStatusCode.OK && DateTime.UtcNow < deadline);

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }
}
