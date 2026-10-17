using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Users;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// Nobody uses the user save to get more than they have. A delegated user manager (a custom role that holds <c>users.manage</c> and is limited to one warehouse) cannot give
/// themselves or anyone more warehouses, cannot take over the all-warehouse administrator by resetting its password, deactivating it or changing their own role to Admin,
/// and cannot give a role that holds permissions they lack. The administrator who sees every warehouse and holds every permission can still do all of it. Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class UserContainmentTests(ApiFixture fixture)
{
    private sealed record Delegate(ApiClient Client, UserDto User, Role Role);

    private async Task<Role> RoleAsync(params string[] permissions)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = Role.Create(Unique.Code("R"), Unique.Name("Role"), null, false, permissions);
        db.Add(role);
        await db.SaveChangesAsync();
        return role;
    }

    private UserSaveRequest NewUser(string role, IEnumerable<Guid>? warehouses = null, bool? all = null)
    {
        var username = Unique.Username("uc");
        return new UserSaveRequest(username, Unique.Email(username), username, role, true, $"Pw-{Guid.NewGuid():N}-1!", (warehouses ?? [fixture.Reference.DefaultWarehouseId]).ToList(), all);
    }

    private static UserSaveRequest Save(UserDto user, string? role = null, IEnumerable<Guid>? warehouses = null, string? password = null, bool? active = null, bool? all = null) =>
        new(user.Username, user.Email, user.DisplayName, role ?? user.Role, active ?? user.IsActive, password, (warehouses ?? user.WarehouseIds).ToList(), all);

    private async Task<Delegate> NewDelegateAsync()
    {
        var role = await RoleAsync(Permissions.UsersManage, Permissions.UsersRead);
        var admin = await fixture.AsAsync(Roles.Admin);
        var request = NewUser(role.Code, all: false);
        var user = (await admin.Users.CreateAsync(request)).Data<UserDto>();
        var client = fixture.CreateClient();
        client.AccessToken = (await client.Auth.LoginAsync(request.Username, request.Password!)).Data<AuthResult>().AccessToken;
        return new Delegate(client, user, role);
    }

    private async Task<UserDto> SeededAdminAsync()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        return (await admin.GetAsync("/api/users?role=Admin&pageSize=100")).Data<PagedResult<UserDto>>().Items.Single(u => u.Username == "admin");
    }

    private Task<string> HashAsync(string username) => fixture.Database.ScalarAsync<string>("SELECT PasswordHash FROM [User] WHERE Username = @u", ("@u", username));

    private async Task<Guid> OtherWarehouseAsync() =>
        (await (await fixture.AsAsync(Roles.Admin)).Warehouses.CreateAsync(new Ims.Application.Features.Warehouses.WarehouseSaveRequest(Unique.Code("W"), Unique.Name("Warehouse"), null, null, true)))
        .Data<Ims.Application.Features.Warehouses.WarehouseDto>().Id;

    [Fact]
    public async Task A_delegated_manager_cannot_assign_warehouses_outside_their_own_scope_to_themselves_or_to_anyone()
    {
        var d = await NewDelegateAsync();
        var other = await OtherWarehouseAsync();

        (await d.Client.Users.UpdateAsync(d.User.Id, Save(d.User, warehouses: [fixture.Reference.DefaultWarehouseId, other]))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await d.Client.Users.CreateAsync(NewUser(d.Role.Code, [other]))).ShouldBeProblem(HttpStatusCode.Forbidden);

        var admin = await fixture.AsAsync(Roles.Admin);
        var current = (await admin.GetAsync($"/api/users?role={d.Role.Code}")).Data<PagedResult<UserDto>>().Items.Single(u => u.Id == d.User.Id);
        Assert.Equal([fixture.Reference.DefaultWarehouseId], current.WarehouseIds);
    }

    [Fact]
    public async Task A_delegated_manager_cannot_edit_reset_the_password_of_or_deactivate_the_all_warehouse_administrator()
    {
        var d = await NewDelegateAsync();
        var seeded = await SeededAdminAsync();
        var hash = await HashAsync("admin");

        (await d.Client.Users.UpdateAsync(seeded.Id, Save(seeded, password: "Takeover-Passw0rd-1!"))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await d.Client.Users.UpdateAsync(seeded.Id, Save(seeded, active: false))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await d.Client.Users.UpdateAsync(seeded.Id, Save(seeded, warehouses: [fixture.Reference.DefaultWarehouseId]))).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(hash, await HashAsync("admin"));
        Assert.True(await fixture.Database.ScalarAsync<bool>("SELECT IsActive FROM [User] WHERE Username = N'admin'"));
    }

    [Fact]
    public async Task A_delegated_manager_cannot_take_over_a_peer_who_sees_more_or_holds_more_even_when_the_save_keeps_the_delegates_own_role()
    {
        var d = await NewDelegateAsync();
        var admin = await fixture.AsAsync(Roles.Admin);
        var other = await OtherWarehouseAsync();
        var sameRoleSeesAll = (await admin.Users.CreateAsync(NewUser(d.Role.Code, [], all: true))).Data<UserDto>();                       // same role, every warehouse
        var sameRoleElsewhere = (await admin.Users.CreateAsync(NewUser(d.Role.Code, [other], all: false))).Data<UserDto>();               // same role, a warehouse the delegate cannot reach
        var holdsMore = (await admin.Users.CreateAsync(NewUser(Roles.Admin, all: false))).Data<UserDto>();                                // same warehouse, a role with more permissions
        var seeded = await SeededAdminAsync();

        foreach (var target in new[] { sameRoleSeesAll, sameRoleElsewhere, holdsMore, seeded })
        {
            var hash = await HashAsync(target.Username);
            // The save keeps the delegate's own role (which CheckAssignment accepts), so only the check on the target can stop it.
            (await d.Client.Users.UpdateAsync(target.Id, Save(target, role: d.Role.Code, password: "Takeover-Passw0rd-1!", warehouses: [fixture.Reference.DefaultWarehouseId]))).ShouldBeProblem(HttpStatusCode.Forbidden);
            (await d.Client.Users.UpdateAsync(target.Id, Save(target, role: d.Role.Code, active: false, warehouses: [fixture.Reference.DefaultWarehouseId]))).ShouldBeProblem(HttpStatusCode.Forbidden);
            Assert.Equal(hash, await HashAsync(target.Username));
            Assert.True(await fixture.Database.ScalarAsync<bool>("SELECT IsActive FROM [User] WHERE Username = @u", ("@u", target.Username)));
        }
    }

    [Fact]
    public async Task A_delegated_manager_cannot_give_a_role_that_holds_permissions_they_lack_to_themselves_or_to_anyone()
    {
        var d = await NewDelegateAsync();

        (await d.Client.Users.UpdateAsync(d.User.Id, Save(d.User, role: Roles.Admin, all: false))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await d.Client.Users.CreateAsync(NewUser(Roles.Admin))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await d.Client.Users.CreateAsync(NewUser(Roles.Clerk))).ShouldBeProblem(HttpStatusCode.Forbidden); // the Clerk can do things the manager cannot

        Assert.Equal(d.Role.Code, await fixture.Database.ScalarAsync<string>("SELECT r.Code FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.Username = @u", ("@u", d.User.Username)));
    }

    [Fact]
    public async Task A_delegated_manager_can_still_manage_users_inside_their_own_access()
    {
        var d = await NewDelegateAsync();

        var created = (await d.Client.Users.CreateAsync(NewUser(d.Role.Code))).Data<UserDto>();                      // their own role, their own warehouse
        (await d.Client.Users.UpdateAsync(created.Id, Save(created, password: "Reset-Passw0rd-1!"))).ShouldBeOk();       // a peer with the same access
        (await d.Client.Users.UpdateAsync(created.Id, Save(created, active: false))).ShouldBeOk();
        (await d.Client.Users.UpdateAsync(d.User.Id, Save(d.User) with { DisplayName = "Renamed" })).ShouldBeOk();       // themselves, nothing widened
    }

    [Fact]
    public async Task The_administrator_who_sees_every_warehouse_can_still_do_all_of_it()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var d = await NewDelegateAsync();
        var other = await OtherWarehouseAsync();

        var peer = (await admin.Users.CreateAsync(NewUser(Roles.Admin, [other, fixture.Reference.DefaultWarehouseId], all: true))).Data<UserDto>();
        (await admin.Users.UpdateAsync(peer.Id, Save(peer, role: Roles.Owner, password: "Reset-Passw0rd-1!"))).ShouldBeOk();
        (await admin.Users.UpdateAsync(d.User.Id, Save(d.User, warehouses: [other], role: Roles.Clerk))).ShouldBeOk();
        (await admin.Users.UpdateAsync(d.User.Id, Save(d.User, role: d.Role.Code, active: false))).ShouldBeOk();
    }

    public static TheoryData<string> LookAlikes => new() { "supervisor", "SUPERVISOR", "Supervisor ", "Ｓupervisor", "Supervisor\0" };

    [Theory]
    [MemberData(nameof(LookAlikes))]
    public async Task A_role_code_is_matched_exactly_on_create_and_update(string code)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk);
        var seeded = await SeededAdminAsync();

        (await admin.Users.CreateAsync(NewUser(code))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.Users.UpdateAsync(account.Id, new UserSaveRequest(account.Username, account.Email, account.Username, code, true, null, account.WarehouseIds.ToList()))).ShouldBeProblem(HttpStatusCode.BadRequest);
        // Saving the only administrator under a look-alike of Admin is a bad role, not a spurious "last administrator" conflict.
        (await admin.Users.UpdateAsync(seeded.Id, Save(seeded, role: code == "Supervisor " ? "Admin " : "admin"))).ShouldBeProblem(HttpStatusCode.BadRequest);
    }
}
