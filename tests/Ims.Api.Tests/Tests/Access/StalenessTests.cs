using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Users;
using Ims.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>
/// The freshness contract of rights that ride in the access token (ADR roles-permissions 3.2): a change of role or scope reaches the token at the next refresh, and a
/// token already issued runs until it expires plus the clock skew. The host reads one clock that the test moves (the token lifetime check is replaced by one that reads it),
/// so waiting for a token to expire takes no time.
/// </summary>
[Collection(ApiCollection.Name)]
public class StalenessTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(Limits.AccessTokenMinutes);
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(Limits.ClockSkewSeconds);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private readonly AccountFactory _accounts = new(fixture);
    private readonly MovableClock _clock = new();
    private WebApplicationFactory<Program> _host = null!;

    public Task InitializeAsync()
    {
        _host = fixture.CreateHost(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IClock>(_clock);
            // The bearer handler reads the real time; this makes it read the test's clock, with the configured skew.
            s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, p) =>
                (notBefore is null || notBefore.Value - p.ClockSkew <= _clock.UtcNow) && (expires is null || expires.Value + p.ClockSkew > _clock.UtcNow));
        }));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<(ApiClient Client, AuthResult Auth)> SignInAsync(TestAccount account)
    {
        var client = ApiFixture.CreateClient(_host);
        var auth = (await client.Auth.LoginAsync(account.Username, account.Password)).Data<AuthResult>();
        client.AccessToken = auth.AccessToken;
        return (client, auth);
    }

    private static List<string> Claims(string accessToken, string type) =>
        new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.Where(c => c.Type == type).Select(c => c.Value).ToList();

    private async Task<ApiResponse> DemoteAsync(TestAccount account, string role)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        return await admin.Users.UpdateAsync(account.Id, new UserSaveRequest(account.Username, account.Email, account.Username, role, true, null, [fixture.Reference.DefaultWarehouseId]));
    }

    [Fact]
    public async Task A_demotion_changes_the_claims_only_at_the_next_refresh()
    {
        var account = await _accounts.CreateAsync(Roles.Admin);
        var (client, first) = await SignInAsync(account);
        (await client.Users.ListAsync()).ShouldBeOk();

        (await DemoteAsync(account, Roles.Clerk)).ShouldBeOk();

        (await client.Users.ListAsync()).ShouldBeOk(); // stale: the token in hand still says Administrator
        Assert.Contains(Permissions.UsersManage, Claims(first.AccessToken, ClaimNames.Permission));
        var refreshed = (await client.Auth.RefreshAsync(first.RefreshToken)).Data<AuthResult>(); // the refresh token survived the demotion
        Assert.DoesNotContain(Permissions.UsersManage, Claims(refreshed.AccessToken, ClaimNames.Permission));
        Assert.Equal(SystemRoleDefaults.For(Roles.Clerk).Order(StringComparer.Ordinal), Claims(refreshed.AccessToken, ClaimNames.Permission).Order(StringComparer.Ordinal));
        Assert.Equal(Roles.Clerk, refreshed.User.Role);
        client.AccessToken = refreshed.AccessToken;
        (await client.Users.ListAsync()).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_demoted_token_that_was_not_refreshed_runs_to_its_expiry_and_the_skew_then_stops()
    {
        var account = await _accounts.CreateAsync(Roles.Admin);
        var (client, _) = await SignInAsync(account);
        (await DemoteAsync(account, Roles.Clerk)).ShouldBeOk();

        _clock.Advance(Lifetime + Skew - Second);
        (await client.Users.ListAsync()).ShouldBeOk();

        _clock.Advance(2 * Second);
        (await client.Users.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_deactivated_user_cannot_renew_but_the_token_in_hand_runs_to_its_expiry_and_the_skew()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await SignInAsync(account);

        (await _accounts.UpdateAsync(account, isActive: false)).ShouldBeOk();

        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await client.Warehouses.ListAsync()).ShouldBeOk();
        _clock.Advance(Lifetime + Skew - Second);
        (await client.Warehouses.ListAsync()).ShouldBeOk();
        _clock.Advance(2 * Second);
        (await client.Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_new_password_revokes_the_refresh_token_and_leaves_the_token_in_hand_until_it_expires()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await SignInAsync(account);

        (await _accounts.UpdateAsync(account, newPassword: "Reset-Passw0rd-9!")).ShouldBeOk();

        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await client.Warehouses.ListAsync()).ShouldBeOk();
        _clock.Advance(Lifetime + 2 * Skew);
        (await client.Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_scope_change_keeps_the_refresh_token_and_reaches_the_claims_at_the_next_refresh()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var account = await _accounts.CreateAsync(Roles.WarehouseStaff);
        var (client, auth) = await SignInAsync(account);

        (await _accounts.UpdateAsync(account, warehouseIds: [fixture.Reference.DefaultWarehouseId, other.Id])).ShouldBeOk();

        Assert.Single(Claims(auth.AccessToken, ClaimNames.Warehouse)); // the token in hand is unchanged
        var refreshed = (await client.Auth.RefreshAsync(auth.RefreshToken)).Data<AuthResult>();
        Assert.Equal(2, Claims(refreshed.AccessToken, ClaimNames.Warehouse).Count);
        Assert.Equal(2, refreshed.User.WarehouseIds.Count);
    }
}
