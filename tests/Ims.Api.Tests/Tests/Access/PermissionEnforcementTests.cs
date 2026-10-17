using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>
/// RP-4a: the <c>perm</c> claims of the access token are the only input of an authorization decision (ADR roles-permissions 3.2). A policy passes when the token carries its
/// code and not otherwise, whatever role the token names; a token of another claims version is refused before any policy is asked.
/// </summary>
[Collection(ApiCollection.Name)]
public class PermissionEnforcementTests(ApiFixture fixture)
{
    private static readonly string[] AnyOfPolicies = [.. AuthPolicies.AnyOf.Keys];

    /// <summary>Every policy of the API: one per catalog code, then the any-of policies.</summary>
    public static IEnumerable<object[]> PolicyNames() => PermissionCatalog.Codes.Concat(AnyOfPolicies).Select(p => new object[] { p });

    public static IEnumerable<object[]> Endpoints() => EndpointCatalog.All.Select(e => new object[] { e.Name });

    private static IReadOnlyList<string> CodesThatOpen(string policy) => AuthPolicies.AnyOf.TryGetValue(policy, out var codes) ? codes : [policy];

    private static ClaimsPrincipal PrincipalWith(IEnumerable<string> permissions, string role = Roles.Admin) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, role), .. permissions.Select(p => new Claim(ClaimNames.Permission, p))],
            "test", ClaimTypes.Name, ClaimTypes.Role));

    private async Task<bool> PassesAsync(string policy, ClaimsPrincipal user)
    {
        using var scope = fixture.Host.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, policy)).Succeeded;
    }

    // ---------- the policies ----------

    [Theory]
    [MemberData(nameof(PolicyNames))]
    public async Task A_policy_passes_with_one_of_its_claims_and_fails_without(string policy)
    {
        var opens = CodesThatOpen(policy);

        foreach (var code in opens) Assert.True(await PassesAsync(policy, PrincipalWith([code], role: "Custom")), $"{policy} with {code}");
        // Everything else the catalog knows, under the name of the Administrator, is not enough.
        Assert.False(await PassesAsync(policy, PrincipalWith(PermissionCatalog.Codes.Except(opens))), $"{policy} without its claim");
        Assert.False(await PassesAsync(policy, PrincipalWith([])), $"{policy} with the role Admin and no claims");
    }

    [Theory]
    [MemberData(nameof(PolicyNames))]
    public async Task A_policy_needs_a_signed_in_caller(string policy)
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([.. CodesThatOpen(policy).Select(c => new Claim(ClaimNames.Permission, c))])); // claims but no authentication type

        Assert.False(await PassesAsync(policy, anonymous));
    }

    [Fact]
    public void Every_code_of_the_catalog_has_a_policy_of_its_name_and_the_any_of_policies_use_only_catalog_codes()
    {
        Assert.All(AuthPolicies.AnyOf, p =>
        {
            Assert.False(PermissionCatalog.IsKnown(p.Key), $"{p.Key} must not look like a permission code");
            Assert.All(p.Value, code => Assert.True(PermissionCatalog.IsKnown(code), code));
        });
        Assert.Equal(2, AuthPolicies.AnyOf.Count);
    }

    // ---------- what the system roles are allowed ----------

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task What_the_policies_let_a_system_role_do_is_what_its_defaults_say(string role)
    {
        // The token a sign-in writes from the seeded rows, asked of every policy.
        var (user, signedIn) = await SignInAsync(role);
        var defaults = SystemRoleDefaults.For(role);

        foreach (var code in PermissionCatalog.Codes) Assert.Equal(defaults.Contains(code), await PassesAsync(code, user));
        foreach (var (name, codes) in AuthPolicies.AnyOf) Assert.Equal(codes.Any(defaults.Contains), await PassesAsync(name, user));
        Assert.Equal(PermissionCatalog.Codes.Where(defaults.Contains), signedIn.User.Permissions); // and the list the web reads is the same
    }

    private async Task<(ClaimsPrincipal User, AuthResult Result)> SignInAsync(string role)
    {
        var result = (await fixture.CreateClient().Auth.LoginAsync(TestUsers.UsernameOf(role), fixture.PasswordOf(role))).Data<AuthResult>();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        return (new ClaimsPrincipal(new ClaimsIdentity(token.Claims, "test")), result);
    }

    [Fact]
    public async Task The_Owner_holds_only_the_large_tier_and_both_review_policies_still_open_for_it()
    {
        var (owner, _) = await SignInAsync(Roles.Owner);

        Assert.False(await PassesAsync(Permissions.DocumentApprove, owner));
        Assert.True(await PassesAsync(Permissions.DocumentApproveLarge, owner));
        Assert.True(await PassesAsync(AuthPolicies.DocumentReview, owner));
        Assert.True(await PassesAsync(AuthPolicies.DocumentCancel, owner)); // the Owner may cancel an Approved document; MayCancel decides which one
        Assert.False(await PassesAsync(Permissions.DocumentCreate, owner));
    }

    // ---------- the endpoints ----------

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task An_endpoint_is_403_for_a_valid_token_that_holds_everything_but_its_permission(string endpoint)
    {
        var @case = EndpointCatalog.Named(endpoint);
        var token = ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: PermissionCatalog.Codes.Except(CodesThatOpen(@case.Permission)));

        var response = await fixture.CreateClient(token).SendAsync(@case.Method, @case.Path(fixture.Reference), @case.Body?.Invoke(fixture.Reference));

        response.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task A_forged_role_claim_without_permission_claims_opens_nothing(string endpoint)
    {
        var @case = EndpointCatalog.Named(endpoint);
        var token = ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin); // validly signed, says Admin, carries no perm claim

        var response = await fixture.CreateClient(token).SendAsync(@case.Method, @case.Path(fixture.Reference), @case.Body?.Invoke(fixture.Reference));

        response.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_without_permission_claims_is_still_a_signed_in_caller_with_nothing_to_do()
    {
        var client = fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin));

        (await client.Auth.MeAsync()).ShouldBeOk(); // /me asks only for a token
        Assert.Empty((await client.Auth.MeAsync()).Data<UserInfo>().Permissions);
    }

    [Fact]
    public async Task A_permission_code_the_catalog_does_not_know_grants_nothing()
    {
        var client = fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, permissions: ["users.delete", "USERS.READ", " users.read"]));

        (await client.Users.ListAsync()).ShouldBe(HttpStatusCode.Forbidden);
        Assert.Empty((await client.Auth.MeAsync()).Data<UserInfo>().Permissions);
    }

    // ---------- the claims version ----------

    [Theory]
    [InlineData(null)] // a token issued before the claim existed
    [InlineData(0)]
    [InlineData(ClaimNames.CurrentClaimsVersion + 1)] // a shape this release does not know
    public async Task A_token_of_another_claims_version_is_401_whatever_it_claims(int? version)
    {
        var token = ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: PermissionCatalog.Codes, claimsVersion: version);

        var client = fixture.CreateClient(token);

        (await client.Users.ListAsync()).ShouldBe(HttpStatusCode.Unauthorized);
        (await client.Auth.MeAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_of_the_current_claims_version_with_the_claim_is_accepted()
    {
        var token = ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Clerk, permissions: [Permissions.UsersRead]);

        (await fixture.CreateClient(token).Users.ListAsync()).ShouldBeOk(); // the role says Clerk; the claim decides
    }
}
