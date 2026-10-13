using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Users;
using Ims.Domain;

namespace Ims.Api.Tests.Builders;

/// <summary>Creates throw-away users through the API (as the seeded admin) and signs them in.</summary>
public sealed class AccountFactory(ApiFixture fixture)
{
    public async Task<TestAccount> CreateAsync(string role, IEnumerable<Guid>? warehouseIds = null, bool isActive = true, string prefix = "user")
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var username = Unique.Username(prefix);
        var password = $"Pw-{Guid.NewGuid():N}-1!";
        var scope = Roles.SeesAllWarehouses(role) ? [] : (warehouseIds ?? [fixture.Reference.DefaultWarehouseId]).ToList();

        var request = new UserSaveRequest(username, Unique.Email(username), username, role, isActive, password, scope);
        var created = (await admin.Users.CreateAsync(request)).Data<UserDto>();
        return new TestAccount(created.Id, username, request.Email, password, role, scope);
    }

    /// <summary>Signs in on a fresh client and returns it together with the tokens.</summary>
    public async Task<(ApiClient Client, AuthResult Auth)> SignInAsync(TestAccount account)
    {
        var client = fixture.CreateClient();
        var auth = (await client.Auth.LoginAsync(account.Username, account.Password)).Data<AuthResult>();
        client.AccessToken = auth.AccessToken;
        return (client, auth);
    }

    /// <summary>Updates a user as the admin, keeping everything else as created.</summary>
    public async Task<ApiResponse> UpdateAsync(TestAccount account, bool? isActive = null, string? newPassword = null, IEnumerable<Guid>? warehouseIds = null)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var request = new UserSaveRequest(account.Username, account.Email, account.Username, account.Role, isActive ?? true, newPassword,
            (warehouseIds ?? account.WarehouseIds).ToList());
        return await admin.Users.UpdateAsync(account.Id, request);
    }
}
