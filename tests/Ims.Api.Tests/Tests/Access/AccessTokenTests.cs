using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Users;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>
/// What the access token a sign-in writes says (ADR roles-permissions 3.2, claims in the token): the role, one claim per permission, the scope and the shape version,
/// and that sign-in, refresh and /me tell the person the same thing. Endpoint checks still go by the role claim until RP-4a; the permission claims are not enforced yet.
/// </summary>
[Collection(ApiCollection.Name)]
public class AccessTokenTests(ApiFixture fixture)
{
    private readonly AccountFactory _accounts = new(fixture);

    private static JwtSecurityToken Read(string accessToken) => new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
    private static List<string> All(JwtSecurityToken token, string type) => token.Claims.Where(c => c.Type == type).Select(c => c.Value).ToList();
    private static string One(JwtSecurityToken token, string type) => Assert.Single(All(token, type));

    /// <summary>What a person is told about their rights, as a string that compares.</summary>
    private static string Shape(UserInfo u) =>
        $"{u.Role}|{u.RoleName}|{string.Join(',', u.Permissions)}|{u.SeesAllWarehouses}|{string.Join(',', u.WarehouseIds.Order())}";

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task The_token_of_a_system_role_carries_its_default_permissions_and_scope(string role)
    {
        var account = await _accounts.CreateAsync(role);
        var (_, auth) = await _accounts.SignInAsync(account);

        var token = Read(auth.AccessToken);

        Assert.Equal(SystemRoleDefaults.For(role).Order(StringComparer.Ordinal), All(token, ClaimNames.Permission).Order(StringComparer.Ordinal));
        Assert.Equal(role, One(token, ClaimTypes.Role));
        Assert.Equal(account.Username, One(token, ClaimTypes.Name));
        Assert.Equal(One(token, JwtRegisteredClaimNames.Sub), One(token, ClaimTypes.NameIdentifier));
        Assert.Equal(Roles.SeesAllWarehouses(role) ? ClaimNames.True : ClaimNames.False, One(token, ClaimNames.AllWarehouses));
        Assert.Equal(ClaimNames.CurrentClaimsVersion.ToString(), One(token, ClaimNames.ClaimsVersion));
        // The warehouse list is only for a user who does not see every warehouse.
        Assert.Equal(Roles.SeesAllWarehouses(role) ? 0 : account.WarehouseIds.Count, All(token, ClaimNames.Warehouse).Count);
        Assert.Equal(TimeSpan.FromMinutes(5), token.ValidTo - token.ValidFrom);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.WarehouseStaff)]
    public async Task Sign_in_refresh_and_me_tell_the_same_story_as_the_token(string role)
    {
        var account = await _accounts.CreateAsync(role);
        var (client, signedIn) = await _accounts.SignInAsync(account);

        var refreshed = (await client.Auth.RefreshAsync(signedIn.RefreshToken)).Data<AuthResult>();
        client.AccessToken = refreshed.AccessToken;
        var me = (await client.Auth.MeAsync()).Data<UserInfo>();

        Assert.Equal(Shape(signedIn.User), Shape(refreshed.User));
        Assert.Equal(Shape(signedIn.User), Shape(me));
        Assert.Equal(role, me.Role);
        Assert.False(string.IsNullOrWhiteSpace(me.RoleName));
        Assert.Equal(Permissions.For(role), me.Permissions); // the list the web reads is still the fixed one; enforcement is unchanged
        var token = Read(refreshed.AccessToken);
        Assert.Equal(me.SeesAllWarehouses, bool.Parse(One(token, ClaimNames.AllWarehouses)));
        Assert.Equal(me.WarehouseIds.Count, All(token, ClaimNames.Warehouse).Count);
        Assert.Equal(SystemRoleDefaults.For(role).Order(StringComparer.Ordinal), All(token, ClaimNames.Permission).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Me_reports_the_claims_of_the_token_it_is_given_not_the_database()
    {
        var account = await _accounts.CreateAsync(Roles.Admin);
        var (client, auth) = await _accounts.SignInAsync(account);
        var admin = await fixture.AsAsync(Roles.Admin);
        var demote = new UserSaveRequest(account.Username, account.Email, account.Username, Roles.Clerk, true, null, [fixture.Reference.DefaultWarehouseId]);
        (await admin.Users.UpdateAsync(account.Id, demote)).ShouldBeOk();

        var me = (await client.Auth.MeAsync()).Data<UserInfo>();

        Assert.Equal(Roles.Admin, me.Role); // the token still says Admin, and so does the screen: both change at the next refresh
        Assert.True(me.SeesAllWarehouses);
        Assert.Equal(Shape(auth.User), Shape(me));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Clerk, false)]
    public async Task A_token_from_before_the_claims_existed_falls_back_to_the_rule_of_its_role_for_the_scope(string role, bool seesAll)
    {
        var old = ForgedTokens.Create(fixture.Settings.JwtKey, role: role); // validly signed, no allwh claim, as a token issued before this release

        var me = (await fixture.CreateClient(old).Auth.MeAsync()).Data<UserInfo>();

        Assert.Equal(seesAll, me.SeesAllWarehouses);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_401_whatever_it_claims()
    {
        var otherKey = Convert.ToBase64String(new byte[48].Select((_, i) => (byte)(i + 11)).ToArray());
        var forged = ForgedTokens.Create(otherKey, role: Roles.Admin, permissions: PermissionCatalog.Codes);

        (await fixture.CreateClient(forged).Users.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }
}
