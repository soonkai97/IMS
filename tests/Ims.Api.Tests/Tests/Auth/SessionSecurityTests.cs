using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Users;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Auth;

/// <summary>Hardening around sign-in: input limits, what a locked account reveals, ending sessions when access changes, and not locking everyone out.</summary>
[Collection(ApiCollection.Name)]
public class SessionSecurityTests(ApiFixture fixture)
{
    private const string GenericFailure = "Invalid username or password.";
    private readonly AccountFactory _accounts = new(fixture);

    // ---------- input limits ----------

    [Theory]
    [InlineData(257, 5)]
    [InlineData(5, 129)]
    [InlineData(100_000, 100_000)]
    public async Task Over_long_username_or_password_is_400_with_the_generic_message(int usernameLength, int passwordLength)
    {
        var response = await fixture.Anonymous().Auth.LoginAsync(new string('u', usernameLength), new string('p', passwordLength));

        var problem = response.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(GenericFailure, problem.Detail);
    }

    [Fact]
    public async Task A_username_at_the_limit_is_an_ordinary_failed_login()
    {
        var response = await fixture.Anonymous().Auth.LoginAsync(new string('u', 256), new string('p', 128));

        response.ShouldBeProblem(HttpStatusCode.Unauthorized);
    }

    // ---------- lockout wording ----------

    [Fact]
    public async Task A_locked_account_gives_the_generic_message_unless_the_correct_password_is_supplied()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var client = fixture.Anonymous();
        for (var attempt = 0; attempt < 5; attempt++) await client.Auth.LoginAsync(account.Username, "wrong");

        var wrongWhileLocked = await client.Auth.LoginAsync(account.Username, "still wrong");
        var rightWhileLocked = await client.Auth.LoginAsync(account.Username, account.Password);
        var unknown = await client.Auth.LoginAsync("nobody-" + Guid.NewGuid().ToString("N"), "whatever");

        Assert.Equal(GenericFailure, wrongWhileLocked.ShouldBeProblem(HttpStatusCode.Unauthorized).Detail);
        Assert.Equal(unknown.Problem.Detail, wrongWhileLocked.Problem.Detail); // a locked account looks like any other failure
        Assert.Equal(Messages.AccountLockedFor(Limits.LockoutMinutes), rightWhileLocked.ShouldBeProblem(HttpStatusCode.Unauthorized).Detail);
    }

    // ---------- sessions end when access changes ----------

    [Fact]
    public async Task Deactivating_a_user_revokes_the_refresh_token_while_the_current_access_token_runs_out_on_its_own()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await _accounts.SignInAsync(account);

        (await _accounts.UpdateAsync(account, isActive: false)).ShouldBeOk();

        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await fixture.Anonymous().Auth.LoginAsync(account.Username, account.Password)).ShouldBeProblem(HttpStatusCode.Unauthorized);
        // Known limit of stateless tokens: the access token already issued is honoured until it expires (5 minutes plus the clock skew at most; StalenessTests moves the clock to prove it).
        (await client.Warehouses.ListAsync()).ShouldBeOk();
    }

    [Fact]
    public async Task A_role_change_keeps_the_refresh_token_and_the_new_rights_ride_on_the_next_one()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await _accounts.SignInAsync(account);
        var admin = await fixture.AsAsync(Roles.Admin);

        var promote = new UserSaveRequest(account.Username, account.Email, account.Username, Roles.Supervisor, true, null, account.WarehouseIds.ToList());
        (await admin.Users.UpdateAsync(account.Id, promote)).ShouldBeOk();

        var refreshed = (await client.Auth.RefreshAsync(auth.RefreshToken)).Data<AuthResult>(); // nobody is signed out for being promoted
        Assert.Equal(Roles.Supervisor, refreshed.User.Role);
        var relogin = (await fixture.Anonymous().Auth.LoginAsync(account.Username, account.Password)).Data<AuthResult>();
        Assert.Equal(Roles.Supervisor, relogin.User.Role);
    }

    [Fact]
    public async Task A_warehouse_scope_change_keeps_the_refresh_token()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var account = await _accounts.CreateAsync(Roles.WarehouseStaff);
        var (client, auth) = await _accounts.SignInAsync(account);

        (await _accounts.UpdateAsync(account, warehouseIds: [fixture.Reference.DefaultWarehouseId, other.Id])).ShouldBeOk();

        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeOk();
    }

    [Fact]
    public async Task A_password_reset_revokes_the_refresh_token_and_the_old_password_stops_working()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await _accounts.SignInAsync(account);
        const string newPassword = "Reset-Passw0rd-9!";

        (await _accounts.UpdateAsync(account, newPassword: newPassword)).ShouldBeOk();

        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await fixture.Anonymous().Auth.LoginAsync(account.Username, account.Password)).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await fixture.Anonymous().Auth.LoginAsync(account.Username, newPassword)).ShouldBeOk();
    }

    [Fact]
    public async Task An_update_that_changes_nothing_about_access_keeps_the_session()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, auth) = await _accounts.SignInAsync(account);
        var admin = await fixture.AsAsync(Roles.Admin);

        var rename = new UserSaveRequest(account.Username, account.Email, "A new display name", account.Role, true, null, account.WarehouseIds.ToList());
        (await admin.Users.UpdateAsync(account.Id, rename)).ShouldBeOk();

        (await client.Auth.RefreshAsync(auth.RefreshToken)).ShouldBeOk();
    }

    // ---------- never lock everyone out ----------

    [Fact]
    public async Task An_administrator_cannot_deactivate_or_demote_themselves()
    {
        var account = await _accounts.CreateAsync(Roles.Admin);
        var (self, _) = await _accounts.SignInAsync(account);

        var deactivate = new UserSaveRequest(account.Username, account.Email, account.Username, Roles.Admin, false, null, []);
        var demote = new UserSaveRequest(account.Username, account.Email, account.Username, Roles.Supervisor, true, null, [fixture.Reference.DefaultWarehouseId]);

        (await self.Users.UpdateAsync(account.Id, deactivate)).ShouldBeProblem(HttpStatusCode.Conflict, "Conflict");
        (await self.Users.UpdateAsync(account.Id, demote)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await self.Auth.MeAsync()).Data<UserInfo>(); // still an administrator, still active
        (await fixture.Anonymous().Auth.LoginAsync(account.Username, account.Password)).Data<AuthResult>();
        Assert.Equal(Roles.Admin, (await self.Auth.MeAsync()).Data<UserInfo>().Role);
    }

    [Fact]
    public async Task Another_administrator_may_still_demote_an_admin_while_others_remain()
    {
        var first = await _accounts.CreateAsync(Roles.Admin);
        var (firstClient, _) = await _accounts.SignInAsync(first);
        var second = await _accounts.CreateAsync(Roles.Admin);

        var demote = new UserSaveRequest(second.Username, second.Email, second.Username, Roles.Clerk, true, null, [fixture.Reference.DefaultWarehouseId]);

        (await firstClient.Users.UpdateAsync(second.Id, demote)).ShouldBeOk();
    }
}
