using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Users;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Auth;

/// <summary>
/// The last active administrator cannot be demoted or disabled by someone else either (the self case is in <c>SessionSecurityTests</c>). It needs a database in which the
/// seeded administrator is the only one, so the class owns a fresh throw-away database and host instead of sharing the collection's (nothing is parked or restored there).
/// </summary>
public class LastAdminTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task The_only_active_administrator_cannot_be_demoted_or_disabled_by_a_session_that_outlived_its_own_deactivation()
    {
        var accounts = new AccountFactory(_fixture);
        var survivor = await accounts.CreateAsync(Roles.Admin);
        var leaver = await accounts.CreateAsync(Roles.Admin);
        var (survivorClient, _) = await accounts.SignInAsync(survivor);
        var (leaverClient, _) = await accounts.SignInAsync(leaver);
        var seeded = await _fixture.AsAsync(Roles.Admin);

        // Take the seeded administrator out of the way (the survivor may: it is not the last one yet), then the leaver (the survivor may again).
        var seededUser = (await seeded.GetAsync($"/api/users?search={TestUsers.UsernameOf(Roles.Admin)}&role={Roles.Admin}")).Data<PagedResult<UserDto>>().Items.Single(u => u.Username == TestUsers.UsernameOf(Roles.Admin));
        var disableSeeded = new UserSaveRequest(seededUser.Username, seededUser.Email, seededUser.DisplayName, Roles.Admin, false, null, []);
        (await survivorClient.Users.UpdateAsync(seededUser.Id, disableSeeded)).ShouldBeOk();
        var disableLeaver = new UserSaveRequest(leaver.Username, leaver.Email, leaver.Username, Roles.Admin, false, null, []);
        (await survivorClient.Users.UpdateAsync(leaver.Id, disableLeaver)).ShouldBeOk();

        // The survivor is now the only active administrator; the leaver's access token is still valid (it is not looked up per request) and tries to remove it.
        var demote = new UserSaveRequest(survivor.Username, survivor.Email, survivor.Username, Roles.Supervisor, true, null, [_fixture.Reference.DefaultWarehouseId]);
        var disable = new UserSaveRequest(survivor.Username, survivor.Email, survivor.Username, Roles.Admin, false, null, []);
        Assert.Equal(Messages.LastAdminRequired, (await leaverClient.Users.UpdateAsync(survivor.Id, demote)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(Messages.LastAdminRequired, (await leaverClient.Users.UpdateAsync(survivor.Id, disable)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(Roles.Admin, (await accounts.SignInAsync(survivor)).Auth.User.Role);
    }
}
