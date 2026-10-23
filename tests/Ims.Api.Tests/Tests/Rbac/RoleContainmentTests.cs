using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Roles;
using Ims.Application.Features.Users;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// Nobody uses the role writes to get more than they have (ADR roles-permissions 2.9). A delegated role manager (a custom role that holds <c>roles.read</c>, <c>roles.manage</c> and two
/// more permissions) cannot give a permission they lack, cannot change or delete a role that holds more than they do, and cannot change their own role. The administrator, who
/// holds everything, still does all of it. Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class RoleContainmentTests(ApiFixture fixture)
{
    private sealed record Delegate(ApiClient Client, RoleDto Role);

    private static readonly string[] DelegateHolds = [Permissions.RolesRead, Permissions.RolesManage, Permissions.StockRead, Permissions.ReportsRead];

    private async Task<Delegate> NewDelegateAsync(string[]? holds = null, bool allWarehouses = false)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var role = (await admin.CreateAsync(Unique.Name("Role manager"), holds ?? DelegateHolds)).Data<RoleDto>();
        var username = Unique.Username("rm");
        var password = $"Pw-{Guid.NewGuid():N}-1!";
        (await admin.Users.CreateAsync(new UserSaveRequest(username, Unique.Email(username), "Role manager", role.Code, true, password, [fixture.Reference.DefaultWarehouseId], allWarehouses))).ShouldBeOk();
        var client = fixture.CreateClient();
        client.AccessToken = (await client.Auth.LoginAsync(username, password)).Data<AuthResult>().AccessToken;
        return new Delegate(client, role);
    }

    private async Task<RoleDto> ReadAsync(Guid id) => (await (await fixture.AsAsync(Roles.Admin)).GetAsync($"/api/roles/{id}")).Data<RoleDto>();

    [Fact]
    public async Task A_delegate_cannot_create_a_role_with_a_permission_they_lack_by_a_list_or_by_a_copy()
    {
        var d = await NewDelegateAsync();
        var roles = (await d.Client.GetAsync("/api/roles")).Data<List<RoleDto>>();

        (await d.Client.CreateAsync(Unique.Name("Role"), Permissions.StockRead, Permissions.CostView)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await d.Client.CreateAsync(Unique.Name("Role"), Permissions.RolesRead, Permissions.UsersRead)).ShouldBeProblem(HttpStatusCode.Forbidden);
        foreach (var source in new[] { Roles.Admin, Roles.Supervisor, Roles.Clerk })   // each holds something the delegate does not
            (await d.Client.PostAsync("/api/roles", new RoleCreateRequest(Unique.Name("Copy"), null, roles.Single(r => r.Code == source).Id))).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_delegate_cannot_change_or_delete_a_role_that_holds_more_than_they_do()
    {
        var d = await NewDelegateAsync();
        var admin = await fixture.AsAsync(Roles.Admin);
        var richer = (await admin.CreateAsync(Unique.Name("Richer"), Permissions.StockRead, Permissions.CostView)).Data<RoleDto>();   // cost.view is not the delegate's
        var clerk = (await d.Client.GetAsync("/api/roles")).Data<List<RoleDto>>().Single(r => r.Code == Roles.Clerk);

        foreach (var target in new[] { richer, clerk })
        {
            (await d.Client.UpdateRoleAsync(target, name: Unique.Name("Taken over"))).ShouldBeProblem(HttpStatusCode.Forbidden);
            (await d.Client.UpdateRoleAsync(target, active: false)).ShouldBeProblem(HttpStatusCode.Forbidden);
            (await d.Client.ReplaceAsync(target, [Permissions.StockRead])).ShouldBeProblem(HttpStatusCode.Forbidden);                 // taking away is a change too
            (await d.Client.ReplaceAsync(target, [Permissions.StockRead, Permissions.CostView, Permissions.ReportsRead])).ShouldBeProblem(HttpStatusCode.Forbidden);
        }
        (await d.Client.DeleteRoleAsync(richer)).ShouldBeProblem(HttpStatusCode.Forbidden);

        var after = await ReadAsync(richer.Id);
        Assert.Equal(richer.Name, after.Name);
        Assert.Equal(richer.Permissions, after.Permissions);
        Assert.Equal(richer.ConcurrencyToken, after.ConcurrencyToken);
    }

    [Fact]
    public async Task A_delegate_cannot_change_or_delete_their_own_role_and_cannot_widen_it()
    {
        var d = await NewDelegateAsync();

        (await d.Client.UpdateRoleAsync(d.Role, name: Unique.Name("Mine now"))).ShouldBeProblem(HttpStatusCode.Conflict);
        (await d.Client.ReplaceAsync(d.Role, DelegateHolds)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await d.Client.ReplaceAsync(d.Role, [Permissions.RolesRead])).ShouldBeProblem(HttpStatusCode.Conflict);
        (await d.Client.DeleteRoleAsync(d.Role)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(d.Role.ConcurrencyToken, (await ReadAsync(d.Role.Id)).ConcurrencyToken);
    }

    [Fact]
    public async Task A_delegate_can_manage_a_role_that_holds_nothing_they_lack_but_cannot_add_what_they_lack()
    {
        var d = await NewDelegateAsync();

        var made = (await d.Client.CreateAsync(Unique.Name("Reporter"), Permissions.ReportsRead)).Data<RoleDto>();                   // inside their own rights
        var wider = (await d.Client.ReplaceAsync(made, [Permissions.ReportsRead, Permissions.StockRead])).Data<RolePermissionsResult>().Role;
        (await d.Client.ReplaceAsync(wider, [Permissions.ReportsRead, Permissions.CostView])).ShouldBeProblem(HttpStatusCode.Forbidden);
        var renamed = (await d.Client.UpdateRoleAsync(wider, name: Unique.Name("Reporter 2"))).Data<RoleDto>();
        (await d.Client.DeleteRoleAsync(renamed)).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_caller_without_roles_manage_can_read_roles_but_not_write_them_and_without_roles_read_sees_nothing()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var role = (await admin.CreateAsync(Unique.Name("Reader"), Permissions.RolesRead)).Data<RoleDto>();
        var username = Unique.Username("rr");
        var password = $"Pw-{Guid.NewGuid():N}-1!";
        (await admin.Users.CreateAsync(new UserSaveRequest(username, Unique.Email(username), "Reader", role.Code, true, password, [fixture.Reference.DefaultWarehouseId]))).ShouldBeOk();
        var reader = fixture.CreateClient();
        reader.AccessToken = (await reader.Auth.LoginAsync(username, password)).Data<AuthResult>().AccessToken;
        var clerk = await fixture.AsAsync(Roles.Clerk);

        (await reader.GetAsync("/api/roles")).ShouldBeOk();
        (await reader.GetAsync("/api/permissions")).ShouldBeOk();
        (await reader.CreateAsync(Unique.Name("Role"))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await reader.DeleteRoleAsync(role)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await clerk.GetAsync("/api/roles")).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await clerk.GetAsync($"/api/roles/{role.Id}")).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_delegate_who_sees_fewer_warehouses_cannot_change_a_role_held_by_someone_who_sees_more()
    {
        var d = await NewDelegateAsync();
        var admin = await fixture.AsAsync(Roles.Admin);
        var shared = (await admin.CreateAsync(Unique.Name("Shared"), Permissions.StockRead)).Data<RoleDto>();   // inside the delegate's rights
        var holder = Unique.Username("sh");
        (await admin.Users.CreateAsync(new UserSaveRequest(holder, Unique.Email(holder), "Holder", shared.Code, true, $"Pw-{Guid.NewGuid():N}-1!", [fixture.Reference.DefaultWarehouseId], true))).ShouldBeOk();

        (await d.Client.UpdateRoleAsync(shared, name: Unique.Name("Taken over"))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await d.Client.ReplaceAsync(shared, [Permissions.StockRead, Permissions.ReportsRead])).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await d.Client.DeleteRoleAsync(shared)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await admin.UpdateRoleAsync(shared, name: Unique.Name("Renamed"))).ShouldBeOk();   // the administrator sees every warehouse
    }

    [Fact]
    public async Task The_administrator_role_cannot_be_renamed_or_emptied_even_by_someone_who_holds_every_permission()
    {
        // "Every permission" includes the ones of the modules, which a role can only be given while the module is on; the modules are off again for the test itself (the token keeps the claim).
        var admin0 = await fixture.AsAsync(Roles.Admin);
        foreach (var module in ModuleCatalog.Codes) (await admin0.Modules.SwitchAsync(module, true, "Containment test setup")).ShouldBeOk();
        try
        {
            var everything = await NewDelegateAsync([.. PermissionCatalog.Codes], allWarehouses: true);
            foreach (var module in ModuleCatalog.Codes) (await admin0.Modules.SwitchAsync(module, false, "Containment test setup")).ShouldBeOk();
            var admin = (await everything.Client.GetAsync("/api/roles")).Data<List<RoleDto>>().Single(r => r.Code == Roles.Admin);

            Assert.Equal(Messages.SystemRoleProtected, (await everything.Client.UpdateRoleAsync(admin, name: Unique.Name("Not admin"))).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
            Assert.Equal(Messages.AdminRoleLocked, (await everything.Client.ReplaceAsync(admin, [Permissions.StockRead])).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
            (await everything.Client.UpdateRoleAsync(admin, active: false)).ShouldBeProblem(HttpStatusCode.Conflict);
            (await everything.Client.DeleteRoleAsync(admin)).ShouldBeProblem(HttpStatusCode.Conflict);
            Assert.Equal(admin.Name, (await ReadAsync(admin.Id)).Name);
        }
        finally
        {
            await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");
        }
    }
}
