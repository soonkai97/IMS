using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Auth;

[Collection(ApiCollection.Name)]
public class TokenTests(ApiFixture fixture)
{
    private readonly AccountFactory _accounts = new(fixture);

    [Fact]
    public async Task Refresh_issues_a_new_pair_and_the_old_refresh_token_stops_working()
    {
        var account = await _accounts.CreateAsync(Roles.Supervisor);
        var (client, first) = await _accounts.SignInAsync(account);

        var second = (await client.Auth.RefreshAsync(first.RefreshToken)).Data<AuthResult>();

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        client.AccessToken = second.AccessToken;
        Assert.Equal(account.Username, (await client.Auth.MeAsync()).Data<UserInfo>().Username);

        var replay = await fixture.Anonymous().Auth.RefreshAsync(first.RefreshToken);
        replay.ShouldBeProblem(HttpStatusCode.Unauthorized);

        // The rotated token is the live one, and it rotates again.
        var third = (await fixture.Anonymous().Auth.RefreshAsync(second.RefreshToken)).Data<AuthResult>();
        Assert.NotEqual(second.RefreshToken, third.RefreshToken);
    }

    [Fact]
    public async Task Two_parallel_refreshes_of_the_same_token_cannot_both_succeed()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (_, auth) = await _accounts.SignInAsync(account);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => fixture.Anonymous().Auth.RefreshAsync(auth.RefreshToken)));

        Assert.True(results.Count(r => r.Status == HttpStatusCode.OK) <= 1,
            "A refresh token was accepted twice: " + string.Join(", ", results.Select(r => (int)r.Status)));
        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
    }

    [Fact]
    public async Task Refreshing_with_an_unknown_token_is_401_and_with_an_empty_token_is_400()
    {
        var client = fixture.Anonymous();

        (await client.Auth.RefreshAsync("not-a-real-token")).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await client.Auth.RefreshAsync("")).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await _accounts.SignInAsync(account);

        (await client.Auth.LogoutAsync(auth.RefreshToken)).ShouldBe(HttpStatusCode.NoContent);

        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeProblem(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_is_quiet_for_unknown_or_already_revoked_tokens()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await _accounts.SignInAsync(account);

        (await client.Auth.LogoutAsync("unknown-token")).ShouldBe(HttpStatusCode.NoContent);
        (await client.Auth.LogoutAsync(auth.RefreshToken)).ShouldBe(HttpStatusCode.NoContent);
        (await client.Auth.LogoutAsync(auth.RefreshToken)).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_deactivated_user_can_no_longer_refresh()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await _accounts.SignInAsync(account);

        (await _accounts.UpdateAsync(account, isActive: false)).ShouldBeOk();

        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeProblem(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_is_recorded_in_the_audit_log()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await _accounts.SignInAsync(account);
        await client.Auth.LogoutAsync(auth.RefreshToken);

        var admin = await fixture.AsAsync(Roles.Admin);
        var entries = (await admin.Audit.QueryAsync(entityType: AuditEntities.User)).Data<AuditPage>().Items;

        Assert.Contains(entries, e => e.Action == AuditActions.Logout && e.EntityId == account.Id.ToString());
        Assert.Contains(entries, e => e.Action == AuditActions.LoginSuccess && e.Actor == account.Username);
    }

    // ---------- access tokens ----------

    [Fact]
    public async Task A_well_formed_token_signed_with_the_servers_key_is_accepted()
    {
        // Control for the faulty-token tests below: it proves the forged token differs from a good one only by its fault.
        var client = fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, permissions: [Permissions.MasterDataRead]));

        (await client.Warehouses.ListAsync()).ShouldBeOk();
    }

    [Fact]
    public async Task An_expired_token_is_401()
    {
        var expired = ForgedTokens.Create(fixture.Settings.JwtKey, expiresAt: DateTime.UtcNow.AddMinutes(-5));

        var response = await fixture.CreateClient(expired).Warehouses.ListAsync();

        response.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    public async Task A_malformed_token_is_401(string token)
    {
        (await fixture.CreateClient(token).Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_401()
    {
        var token = ForgedTokens.Create(Convert.ToBase64String(new byte[48].Select((_, i) => (byte)(i + 7)).ToArray()));

        (await fixture.CreateClient(token).Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_for_another_audience_or_issuer_is_401()
    {
        var wrongAudience = ForgedTokens.Create(fixture.Settings.JwtKey, audience: "someone-else");
        var wrongIssuer = ForgedTokens.Create(fixture.Settings.JwtKey, issuer: "someone-else");

        (await fixture.CreateClient(wrongAudience).Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.CreateClient(wrongIssuer).Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_whose_signature_was_tampered_with_is_401()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var token = admin.AccessToken!;
        var tampered = token[..^3] + (token[^3] == 'A' ? "BBB" : "AAA");

        (await fixture.CreateClient(tampered).Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_request_without_a_token_is_401()
    {
        (await fixture.Anonymous().Warehouses.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.Anonymous().Auth.MeAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }
}
