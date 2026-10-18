using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Roles;
using Ims.Application.Features.Users;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// The system always keeps one active person whose role holds the whole administration core (ADR roles-permissions 2.9), by permission and not by the name Administrator, and the
/// rule holds when two writes race. The caller of every write is a third administrator who has been deactivated: his token still works (as in <c>LastAdminTests</c>), so the
/// writes come from someone who is not one of the two people at stake. It needs a database in which the seeded administrator can be taken out of the way, so each fact owns a
/// fresh throw-away database and host.
/// </summary>
public class AdministrabilityTests : IAsyncLifetime
{
    private const int Rounds = 25;

    private readonly ApiFixture _fixture = new();
    private AccountFactory _accounts = null!;
    private TestAccount _a = null!, _b = null!, _holder = null!;
    private ApiClient _caller = null!;
    private RoleDto _core = null!;

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>
    /// Two administrators A and B by the role Administrator, a user "holder" of a custom role C with every permission (so the whole core), and the caller. Only A and B are active,
    /// apart from the caller (deactivated at the end, token in hand).
    /// </summary>
    private async Task SetUpAsync()
    {
        _accounts = new AccountFactory(_fixture);
        var seeded = await _fixture.AsAsync(Roles.Admin);
        _core = (await seeded.CreateAsync(Unique.Name("Everything"), [.. PermissionCatalog.Codes])).Data<RoleDto>();
        _a = await _accounts.CreateAsync(Roles.Admin);
        _b = await _accounts.CreateAsync(Roles.Admin);
        var caller = await _accounts.CreateAsync(Roles.Admin);
        var holderName = Unique.Username("holder");
        var holderRequest = new UserSaveRequest(holderName, Unique.Email(holderName), "Holder", _core.Code, false, $"Pw-{Guid.NewGuid():N}-1!", [], true);
        var holder = (await seeded.Users.CreateAsync(holderRequest)).Data<UserDto>();
        _holder = new TestAccount(holder.Id, holder.Username, holder.Email, holderRequest.Password!, _core.Code, []);
        _caller = (await _accounts.SignInAsync(caller)).Client;

        var (a, _) = await _accounts.SignInAsync(_a);
        var seededUser = (await seeded.GetAsync($"/api/users?search={TestUsers.UsernameOf(Roles.Admin)}&role={Roles.Admin}")).Data<PagedResult<UserDto>>().Items.Single(u => u.Username == TestUsers.UsernameOf(Roles.Admin));
        (await a.Users.UpdateAsync(seededUser.Id, Save(seededUser.Username, seededUser.Email, Roles.Admin, false))).ShouldBeOk();
        (await a.Users.UpdateAsync(caller.Id, Save(caller.Username, caller.Email, Roles.Admin, false))).ShouldBeOk();
    }

    private UserSaveRequest Save(string username, string email, string role, bool active) =>
        new(username, email, username, role, active, null, [_fixture.Reference.DefaultWarehouseId]);

    private Task<ApiResponse> SetAsync(TestAccount who, string role, bool active) => _caller.Users.UpdateAsync(who.Id, Save(who.Username, who.Email, role, active));

    private Task<int> AdministratorsAsync() => _fixture.Database.ScalarAsync<int>($"""
        SELECT COUNT(*) FROM [User] u JOIN Role r ON r.Id = u.RoleId AND r.REC_DELETED = 0 AND r.IsActive = 1
        WHERE u.IsActive = 1 AND u.REC_DELETED = 0 AND (SELECT COUNT(*) FROM RolePermission p WHERE p.RoleId = r.Id AND p.REC_DELETED = 0
            AND p.PermissionCode IN (N'{Permissions.UsersRead}', N'{Permissions.UsersManage}', N'{Permissions.RolesRead}', N'{Permissions.RolesManage}')) = 4
        """);

    private async Task<ApiResponse> ReplaceCoreRoleAsync(string[] permissions)
    {
        var current = (await _caller.GetAsync($"/api/roles/{_core.Id}")).Data<RoleDto>();
        return await _caller.ReplaceAsync(current, permissions);
    }

    [Fact]
    public async Task The_last_administrator_is_whoever_holds_the_whole_core_by_any_role_and_every_write_that_would_remove_them_is_refused()
    {
        await SetUpAsync();
        (await SetAsync(_holder, _core.Code, true)).ShouldBeOk();                  // the holder of the custom role is an administrator too
        Assert.Equal(3, await AdministratorsAsync());

        (await SetAsync(_a, Roles.Supervisor, true)).ShouldBeOk();                 // A is not the last: the holder remains (the old guard counted the name Administrator only)
        (await SetAsync(_b, Roles.Supervisor, true)).ShouldBeOk();
        Assert.Equal(1, await AdministratorsAsync());

        // The holder is the last: not deactivated, not given another role, and the role they hold cannot lose a core permission.
        Assert.Equal(Messages.LastAdminRequired, (await SetAsync(_holder, _core.Code, false)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(Messages.LastAdminRequired, (await SetAsync(_holder, Roles.Clerk, true)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(Messages.LastAdminRequired, (await ReplaceCoreRoleAsync([Permissions.StockRead])).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        string[] withoutOneCorePermission = [.. PermissionCatalog.Codes.Where(c => c != Permissions.RolesManage)];
        Assert.Equal(Messages.LastAdminRequired, (await ReplaceCoreRoleAsync(withoutOneCorePermission)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(1, await AdministratorsAsync());

        // With another administrator the same writes pass: A is back, so the role may drop the core and the holder may go.
        (await SetAsync(_a, Roles.Admin, true)).ShouldBeOk();
        (await ReplaceCoreRoleAsync([Permissions.StockRead])).ShouldBeOk();
        Assert.Equal(1, await AdministratorsAsync());
        Assert.Equal(Messages.LastAdminRequired, (await SetAsync(_a, Roles.Admin, false)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
    }

    [Fact]
    public async Task Two_writes_that_each_remove_the_other_administrator_never_leave_the_system_without_one()
    {
        await SetUpAsync();

        // Two user saves: A is demoted while B is deactivated.
        (await SetAsync(_b, Roles.Admin, true)).ShouldBeOk();
        for (var round = 0; round < Rounds; round++)
        {
            await AssertOneOfTwoSucceedsAsync(SetAsync(_a, Roles.Supervisor, true), SetAsync(_b, Roles.Admin, false));
            (await SetAsync(_a, Roles.Admin, true)).ShouldBeOk();
            (await SetAsync(_b, Roles.Admin, true)).ShouldBeOk();
        }

        // A user save and a role edit: A is demoted while the role of the holder loses the core.
        (await SetAsync(_b, Roles.Admin, false)).ShouldBeOk();
        (await SetAsync(_holder, _core.Code, true)).ShouldBeOk();
        for (var round = 0; round < Rounds; round++)
        {
            await AssertOneOfTwoSucceedsAsync(SetAsync(_a, Roles.Supervisor, true), ReplaceCoreRoleAsync([Permissions.StockRead]));
            (await SetAsync(_a, Roles.Admin, true)).ShouldBeOk();
            (await ReplaceCoreRoleAsync([.. PermissionCatalog.Codes])).ShouldBeOk();
        }
    }

    private async Task AssertOneOfTwoSucceedsAsync(Task<ApiResponse> first, Task<ApiResponse> second)
    {
        var answers = await Task.WhenAll(first, second);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], answers.Select(r => r.Status).Order());
        Assert.Equal(Messages.LastAdminRequired, answers.Single(r => r.Status == HttpStatusCode.Conflict).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(1, await AdministratorsAsync());
    }
}
