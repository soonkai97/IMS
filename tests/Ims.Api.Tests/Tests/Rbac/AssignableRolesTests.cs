using System.Net;
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
/// The user form takes its role list from <c>GET /api/users/assignable-roles</c>: the active roles the caller could give in the user save (the caller holds every permission of the role),
/// for a caller who has <c>users.manage</c> but not <c>roles.read</c>. A custom role is in the list; the form shows the name, and the user list carries it as <c>roleName</c>. Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class AssignableRolesTests(ApiFixture fixture)
{
    private async Task<Role> RoleAsync(bool active, params string[] permissions)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = Role.Create(Unique.Code("R"), Unique.Name("Role"), null, false, permissions);
        role.SetActive(active);
        db.Add(role);
        await db.SaveChangesAsync();
        return role;
    }

    private async Task<IReadOnlyList<AssignableRoleDto>> ListAsync(ApiClient client) => (await client.Users.AssignableRolesAsync()).Data<IReadOnlyList<AssignableRoleDto>>();

    [Fact]
    public async Task An_administrator_is_offered_every_active_role_the_custom_ones_with_their_names_and_not_the_inactive_ones()
    {
        var custom = await RoleAsync(true, Permissions.StockRead);
        var inactive = await RoleAsync(false, Permissions.StockRead);
        var admin = await fixture.AsAsync(Roles.Admin);

        var roles = await ListAsync(admin);

        Assert.Contains(roles, r => r.Code == custom.Code && r.Name == custom.Name);
        Assert.DoesNotContain(roles, r => r.Code == inactive.Code);
        foreach (var code in new[] { Roles.Admin, Roles.Owner, Roles.Supervisor, Roles.WarehouseStaff, Roles.Clerk }) Assert.Contains(roles, r => r.Code == code);
    }

    [Fact]
    public async Task A_user_manager_without_roles_read_is_offered_only_the_roles_the_user_save_would_accept()
    {
        var own = await RoleAsync(true, Permissions.UsersManage, Permissions.UsersRead, Permissions.StockRead);
        var weaker = await RoleAsync(true, Permissions.StockRead);
        var stronger = await RoleAsync(true, Permissions.StockRead, Permissions.ReportsRead);
        var admin = await fixture.AsAsync(Roles.Admin);
        var username = Unique.Username("ar");
        var request = new UserSaveRequest(username, Unique.Email(username), username, own.Code, true, $"Pw-{Guid.NewGuid():N}-1!", [fixture.Reference.DefaultWarehouseId], false);
        Assert.NotNull((await admin.Users.CreateAsync(request)).Data<UserDto>());
        var manager = fixture.CreateClient();
        manager.AccessToken = (await manager.Auth.LoginAsync(request.Username, request.Password!)).Data<AuthResult>().AccessToken;
        (await manager.GetAsync("/api/roles")).ShouldBeProblem(HttpStatusCode.Forbidden); // the manager really has no roles.read

        var codes = (await ListAsync(manager)).Select(r => r.Code).ToList();

        Assert.Contains(own.Code, codes);
        Assert.Contains(weaker.Code, codes);
        Assert.DoesNotContain(stronger.Code, codes);
        Assert.DoesNotContain(Roles.Admin, codes);
        Assert.DoesNotContain(Roles.Clerk, codes);
        // and the save agrees with the list: what is offered is accepted, what is left out is refused
        var offered = new UserSaveRequest(Unique.Username("ar"), Unique.Email(Unique.Username("ar")), "Offered", weaker.Code, true, "Pw-Offered-1!", [fixture.Reference.DefaultWarehouseId], false);
        (await manager.Users.CreateAsync(offered)).ShouldBeOk();
        (await manager.Users.CreateAsync(offered with { Username = Unique.Username("ar"), Email = Unique.Email(Unique.Username("ar")), Role = stronger.Code })).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_user_list_carries_the_role_name_next_to_the_code()
    {
        var custom = await RoleAsync(true, Permissions.StockRead);
        var admin = await fixture.AsAsync(Roles.Admin);
        var username = Unique.Username("ar");
        var created = (await admin.Users.CreateAsync(new UserSaveRequest(username, Unique.Email(username), username, custom.Code, true, $"Pw-{Guid.NewGuid():N}-1!", [fixture.Reference.DefaultWarehouseId], false))).Data<UserDto>();

        var listed = (await admin.GetAsync($"/api/users?role={custom.Code}")).Data<PagedResult<UserDto>>().Items.Single(u => u.Id == created.Id);

        Assert.Equal(custom.Name, created.RoleName);
        Assert.Equal(custom.Name, listed.RoleName);
        Assert.Equal(custom.Code, listed.Role);
    }
}
