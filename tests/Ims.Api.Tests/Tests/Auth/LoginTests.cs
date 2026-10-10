using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Auth;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Auth;

[Collection(ApiCollection.Name)]
public class LoginTests(ApiFixture fixture)
{
    private const string GenericFailure = "Invalid username or password.";

    [Fact]
    public async Task Login_with_username_returns_tokens_user_and_permissions()
    {
        var response = await fixture.Anonymous().Auth.LoginAsync("admin", fixture.Settings.AdminPassword);

        var auth = response.Data<AuthResult>();
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.True(auth.ExpiresAt > DateTime.UtcNow);
        Assert.Equal("admin", auth.User.Username);
        Assert.Equal(Roles.Admin, auth.User.Role);
        Assert.Equal(Permissions.For(Roles.Admin).Order(), auth.User.Permissions.Order());
    }

    [Theory]
    [InlineData(Roles.Clerk, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Owner, false)]
    public async Task The_permission_list_for_the_web_app_names_partners_create_only_for_the_roles_that_hold_it(string role, bool expected)
    {
        var client = await fixture.AsAsync(role);

        var me = (await client.Auth.MeAsync()).Data<UserInfo>();

        Assert.Equal(expected, me.Permissions.Contains("partners.create"));   // the literal is the wire contract the web app checks
        Assert.Equal(role is Roles.Supervisor or Roles.Admin, me.Permissions.Contains("masterdata.manage"));
    }

    [Fact]
    public async Task Login_works_with_the_email_address_and_ignores_case()
    {
        var response = await fixture.Anonymous().Auth.LoginAsync("Supervisor@IMS.local", fixture.Settings.DemoPassword);

        Assert.Equal(Roles.Supervisor, response.Data<AuthResult>().User.Role);
    }

    [Fact]
    public async Task Access_token_opens_a_protected_endpoint()
    {
        var anonymous = fixture.Anonymous();
        var auth = (await anonymous.Auth.LoginAsync("staff", fixture.Settings.DemoPassword)).Data<AuthResult>();
        anonymous.AccessToken = auth.AccessToken;

        var me = (await anonymous.Auth.MeAsync()).Data<UserInfo>();

        Assert.Equal("staff", me.Username);
        Assert.Equal([fixture.Reference.DefaultWarehouseId], me.WarehouseIds);
    }

    [Fact]
    public async Task Wrong_password_is_401_with_the_generic_message()
    {
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk);

        var response = await fixture.Anonymous().Auth.LoginAsync(account.Username, "definitely-wrong");

        var problem = response.ShouldBeProblem(HttpStatusCode.Unauthorized, "Unauthorized");
        Assert.Equal(GenericFailure, problem.Detail);
    }

    [Fact]
    public async Task Unknown_user_gets_the_same_response_as_a_wrong_password()
    {
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk);
        var wrongPassword = await fixture.Anonymous().Auth.LoginAsync(account.Username, "definitely-wrong");
        var unknownUser = await fixture.Anonymous().Auth.LoginAsync("nobody-" + Guid.NewGuid().ToString("N"), "definitely-wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.Status);
        Assert.Equal(wrongPassword.Problem.Detail, unknownUser.Problem.Detail);
        Assert.Equal(wrongPassword.Problem.Title, unknownUser.Problem.Title);
    }

    [Fact]
    public async Task A_deactivated_user_cannot_log_in_and_gets_the_generic_message()
    {
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Clerk, isActive: false);

        var response = await fixture.Anonymous().Auth.LoginAsync(account.Username, account.Password);

        Assert.Equal(GenericFailure, response.ShouldBeProblem(HttpStatusCode.Unauthorized).Detail);
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("someone", "")]
    public async Task Login_without_username_or_password_is_a_400_with_field_errors(string username, string password)
    {
        var response = await fixture.Anonymous().Auth.LoginAsync(username, password);

        var problem = response.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.NotEmpty(problem.Errors!);
    }

    [Fact]
    public async Task Login_attempts_from_one_address_are_rate_limited_with_429()
    {
        var client = fixture.Anonymous(); // one fake address for every attempt
        var unknownUser = "nobody-" + Guid.NewGuid().ToString("N"); // never a real account, so nothing gets locked
        var statuses = new List<HttpStatusCode>();
        ApiResponse? limited = null;

        for (var attempt = 0; attempt < 30; attempt++)
        {
            var response = await client.Auth.LoginAsync(unknownUser, "wrong");
            statuses.Add(response.Status);
            if (response.Status == HttpStatusCode.TooManyRequests) limited ??= response;
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);

        // The answer says how long to wait (EA-24), in the header and in the body.
        var problem = limited!.ShouldBeProblem(HttpStatusCode.TooManyRequests, "Too many requests");
        Assert.StartsWith("Too many requests. Try again in", problem.Detail);
        var seconds = int.Parse(limited.Header("Retry-After")!);
        Assert.InRange(seconds, 1, 60);
        var body = limited.Json().RootElement;
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
        Assert.Equal(seconds, body.GetProperty("retryAfterSeconds").GetInt32());
    }
}
