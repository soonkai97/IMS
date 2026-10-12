using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Users;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Auth;

/// <summary>The last active administrator cannot be demoted or disabled by someone else either (the self case is in <c>SessionSecurityTests</c>).</summary>
[Collection(ApiCollection.Name)]
public class LastAdminTests(ApiFixture fixture)
{
    private const string OtherActiveAdmins = "SELECT ISNULL(STRING_AGG(CAST(Id AS varchar(12)), ','), '') FROM [User] WHERE Role = 'Admin' AND IsActive = 1 AND Username NOT IN (@a, @b)";
    private const string SetActive = "UPDATE [User] SET IsActive = @active WHERE Id IN (SELECT CAST(value AS int) FROM STRING_SPLIT(@ids, ',') WHERE value <> '')";

    [Fact]
    public async Task The_only_active_administrator_cannot_be_demoted_or_disabled_by_a_session_that_outlived_its_own_deactivation()
    {
        var accounts = new AccountFactory(fixture);
        var survivor = await accounts.CreateAsync(Roles.Admin);
        var leaver = await accounts.CreateAsync(Roles.Admin);
        var (survivorClient, _) = await accounts.SignInAsync(survivor);
        var (leaverClient, _) = await accounts.SignInAsync(leaver);

        // The shared test database holds other active administrators: park them, so the two accounts above are the only ones, and put them back afterwards.
        var parked = await fixture.Database.ScalarAsync<string>(OtherActiveAdmins, ("@a", survivor.Username), ("@b", leaver.Username));
        await fixture.Database.ExecuteAsync(SetActive, ("@active", false), ("@ids", parked));
        try
        {
            // Two administrators: the first may disable the second. The second's access token keeps working until it expires (it is not looked up per request).
            var disableLeaver = new UserSaveRequest(leaver.Username, leaver.Email, leaver.Username, Roles.Admin, false, null, []);
            (await survivorClient.Users.UpdateAsync(leaver.Id, disableLeaver)).ShouldBeOk();

            // The survivor is now the only active administrator; the leaver's still-valid session tries to remove it.
            var demote = new UserSaveRequest(survivor.Username, survivor.Email, survivor.Username, Roles.Supervisor, true, null, [fixture.Reference.DefaultWarehouseId]);
            var disable = new UserSaveRequest(survivor.Username, survivor.Email, survivor.Username, Roles.Admin, false, null, []);
            Assert.Equal(Messages.LastAdminRequired, (await leaverClient.Users.UpdateAsync(survivor.Id, demote)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
            Assert.Equal(Messages.LastAdminRequired, (await leaverClient.Users.UpdateAsync(survivor.Id, disable)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
            Assert.Equal(Roles.Admin, (await accounts.SignInAsync(survivor)).Auth.User.Role);
        }
        finally
        {
            await fixture.Database.ExecuteAsync(SetActive, ("@active", true), ("@ids", parked));
        }
    }
}
