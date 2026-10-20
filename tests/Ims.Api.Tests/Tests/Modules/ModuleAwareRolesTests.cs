using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Roles;
using Ims.Application.Features.Users;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// PL1.2 acceptance 1 to 6: the role pages while a module is off (ADR modules-and-tracking-dimensions 4.3). The permissions of an off module are not listed and not accepted, a role that already
/// holds one keeps it through every save by a client that never saw it (never named as added or removed, never audited), the <c>Requires</c> closure is checked on the merged set, turning
/// the module on says how many roles get grants back, and containment counts hidden grants like any other. Real SQL Server; every test starts and ends with no module row, and the roles it
/// makes are deleted again.
/// </summary>
[Collection(ApiCollection.Name)]
public class ModuleAwareRolesTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Export = Permissions.AccountingExport;
    private const string Accounting = ModuleCodes.Accounting;

    private readonly List<RoleDto> _made = [];
    private ApiClient _admin = null!;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _admin = await fixture.AsAsync(Roles.Admin);
    }

    public async Task DisposeAsync()
    {
        foreach (var role in _made) await _admin.DeleteRoleAsync(role);
        await ResetAsync();
    }

    private Task ResetAsync() => fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    private static string Why() => $"Roles test {Guid.NewGuid():N}"[..24];

    private async Task<ModuleSwitchResult> SwitchAsync(bool enabled) => (await _admin.Modules.SwitchAsync(Accounting, enabled, Why())).Data<ModuleSwitchResult>();

    /// <summary>A role made while the module is on (the only time a module permission can be named), so it holds the hidden grant when the module is turned off afterwards.</summary>
    private async Task<RoleDto> RoleHoldingExportAsync(params string[] permissions)
    {
        await SwitchAsync(true);
        var role = (await _admin.CreateAsync(Unique.Name("Accountant"), permissions)).Data<RoleDto>();
        _made.Add(role);
        Assert.Contains(Export, role.Permissions);
        return role;
    }

    private async Task<RoleDto> ReadAsync(Guid id) => (await _admin.GetAsync($"/api/roles/{id}")).Data<RoleDto>();

    private Task<int> StoredGrantsAsync(Guid roleId) =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission rp JOIN Role r ON r.Id = rp.RoleId WHERE r.PublicId = @id AND rp.PermissionCode = @c AND rp.REC_DELETED = 0", ("@id", roleId), ("@c", Export));

    private async Task<List<AuditLog>> AuditOfAsync(string action, Guid roleId)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<AuditLog>().Where(a => a.Action == action && a.EntityType == nameof(Role) && a.EntityId == roleId.ToString()).ToListAsync();
    }

    // ----- the list of permissions (acceptance 1) -----
    [Fact]
    public async Task The_permission_list_omits_the_module_permission_while_the_module_is_off_and_lists_it_with_its_module_and_its_requirement_when_on()
    {
        var off = (await _admin.GetAsync("/api/permissions")).Data<List<PermissionDto>>();

        Assert.Equal(PermissionCatalog.Codes.Except([Export]), off.Select(p => p.Code));
        Assert.All(off, p => Assert.Null(p.Module));

        await SwitchAsync(true);
        var on = (await _admin.GetAsync("/api/permissions")).Data<List<PermissionDto>>();

        Assert.Equal(PermissionCatalog.Codes, on.Select(p => p.Code));
        var entry = on.Single(p => p.Code == Export);
        Assert.Equal(Accounting, entry.Module);
        Assert.Equal([Permissions.CostView], entry.Requires);
        Assert.False(entry.IsDangerous);

        await SwitchAsync(false);

        Assert.DoesNotContain((await _admin.GetAsync("/api/permissions")).Data<List<PermissionDto>>(), p => p.Code == Export); // and off again at once
    }

    // ----- a save that names it while off (acceptance 2) -----
    [Fact]
    public async Task Naming_the_module_permission_while_the_module_is_off_is_a_400_on_create_and_on_replace_and_nothing_is_saved()
    {
        var plain = (await _admin.CreateAsync(Unique.Name("Plain"), Permissions.StockRead, Permissions.CostView)).Data<RoleDto>();
        _made.Add(plain);

        var created = (await _admin.CreateAsync(Unique.Name("Nope"), Permissions.CostView, Export)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var replaced = (await _admin.ReplaceAsync(plain, [Permissions.StockRead, Permissions.CostView, Export])).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("not available", created.Detail);
        Assert.Contains(Export, created.Detail);
        Assert.Contains("not available", replaced.Detail);
        Assert.Equal(plain.Permissions, (await ReadAsync(plain.Id)).Permissions);
        Assert.Equal(plain.ConcurrencyToken, (await ReadAsync(plain.Id)).ConcurrencyToken);
        Assert.Empty(await AuditOfAsync(AuditActions.RolePermissionsChanged, plain.Id));
        Assert.Equal(0, await StoredGrantsAsync(plain.Id));
    }

    // ----- the hidden grant survives a save (acceptance 3) -----
    [Fact]
    public async Task A_role_that_holds_the_module_permission_keeps_it_when_another_permission_changes_while_the_module_is_off_and_nothing_names_it()
    {
        var role = await RoleHoldingExportAsync(Permissions.StockRead, Permissions.CostView, Export);
        await SwitchAsync(false);
        var seen = await ReadAsync(role.Id);
        Assert.Equal([Permissions.StockRead, Permissions.CostView], seen.Permissions); // the editor does not see the grant, so it cannot send it back

        var added = (await _admin.ReplaceAsync(seen, [Permissions.StockRead, Permissions.CostView, Permissions.ReportsRead])).Data<RolePermissionsResult>();
        var removedOne = (await _admin.ReplaceAsync(added.Role, [Permissions.CostView, Permissions.ReportsRead])).Data<RolePermissionsResult>();

        Assert.Equal([Permissions.ReportsRead], added.Added);
        Assert.Empty(added.Removed);
        Assert.Equal([Permissions.StockRead], removedOne.Removed);
        Assert.Empty(removedOne.Added);
        Assert.DoesNotContain(Export, removedOne.Role.Permissions);
        Assert.Equal(1, await StoredGrantsAsync(role.Id)); // still there, and the row was never touched
        var audit = await AuditOfAsync(AuditActions.RolePermissionsChanged, role.Id);
        Assert.Equal(2, audit.Count);
        Assert.All(audit, a => Assert.DoesNotContain(Export, (a.Before ?? "") + (a.After ?? ""), StringComparison.Ordinal));

        await SwitchAsync(true); // back on: the role has it again, and the list shows it

        Assert.Equal([Permissions.ReportsRead, Export, Permissions.CostView], (await ReadAsync(role.Id)).Permissions); // catalog order
        Assert.Contains((await _admin.GetAsync("/api/roles")).Data<List<RoleDto>>(), r => r.Id == role.Id && r.Permissions.Contains(Export));
    }

    [Fact]
    public async Task Saving_the_same_visible_set_again_while_the_module_is_off_changes_nothing_and_writes_no_audit_event()
    {
        var role = await RoleHoldingExportAsync(Permissions.StockRead, Permissions.CostView, Export);
        await SwitchAsync(false);
        var seen = await ReadAsync(role.Id);

        var same = (await _admin.ReplaceAsync(seen, [Permissions.StockRead, Permissions.CostView])).Data<RolePermissionsResult>();

        Assert.Empty(same.Added);
        Assert.Empty(same.Removed);
        Assert.Equal(seen.ConcurrencyToken, same.Role.ConcurrencyToken);
        Assert.Empty(await AuditOfAsync(AuditActions.RolePermissionsChanged, role.Id));
        Assert.Equal(1, await StoredGrantsAsync(role.Id));
    }

    [Fact]
    public async Task While_the_module_is_on_the_editor_sees_the_grant_so_leaving_it_out_removes_it()
    {
        var role = await RoleHoldingExportAsync(Permissions.StockRead, Permissions.CostView, Export);

        var result = (await _admin.ReplaceAsync(role, [Permissions.StockRead, Permissions.CostView])).Data<RolePermissionsResult>();

        Assert.Equal([Export], result.Removed);
        Assert.Equal(0, await StoredGrantsAsync(role.Id));
        Assert.Single(await AuditOfAsync(AuditActions.RolePermissionsChanged, role.Id));
    }

    // ----- the closure on the merged set (acceptance 4) -----
    [Fact]
    public async Task Removing_the_permission_the_hidden_grant_needs_is_a_400_that_names_the_dependency_and_nothing_changes()
    {
        var role = await RoleHoldingExportAsync(Permissions.StockRead, Permissions.CostView, Export);
        await SwitchAsync(false);
        var seen = await ReadAsync(role.Id);

        var problem = (await _admin.ReplaceAsync(seen, [Permissions.StockRead])).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains(Export, problem.Detail);
        Assert.Contains(Permissions.CostView, problem.Detail);
        var after = await ReadAsync(role.Id);
        Assert.Equal(seen.Permissions, after.Permissions);
        Assert.Equal(seen.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(1, await StoredGrantsAsync(role.Id));
        Assert.Empty(await AuditOfAsync(AuditActions.RolePermissionsChanged, role.Id));
    }

    // ----- turning the module on (acceptance 5) -----
    [Fact]
    public async Task Turning_the_module_on_says_how_many_roles_get_the_permission_back_and_the_audit_event_records_it()
    {
        var first = await RoleHoldingExportAsync(Permissions.CostView, Export);
        var second = (await _admin.CreateAsync(Unique.Name("Second accountant"), Permissions.CostView, Export)).Data<RoleDto>();
        _made.Add(second);
        var without = (await _admin.CreateAsync(Unique.Name("Without"), Permissions.CostView)).Data<RoleDto>();
        _made.Add(without);
        await SwitchAsync(false);
        // Independent of the code under test: the live roles that hold the grant, counted in the database (the Administrator and the Owner roles, and the two made here).
        var holders = await fixture.Database.ScalarAsync<int>(
            "SELECT COUNT(DISTINCT r.Id) FROM Role r JOIN RolePermission rp ON rp.RoleId = r.Id WHERE r.REC_DELETED = 0 AND rp.REC_DELETED = 0 AND rp.PermissionCode = @c", ("@c", Export));
        Assert.True(holders >= 4);
        var reason = Why();

        var on = (await _admin.Modules.SwitchAsync(Accounting, true, reason)).Data<ModuleSwitchResult>();

        Assert.True(on.Enabled);
        Assert.Equal(holders, on.RolesRegainingGrants);
        var page = (await _admin.Audit.QueryAsync(action: AuditActions.ModuleEnabled, entityId: "Module.accounting")).Data<AuditPage>();
        var enabled = Assert.Single(page.Items, e => e.After!.Contains(reason, StringComparison.Ordinal));
        Assert.Contains($"\"RolesRegainingGrants\":{holders}", enabled.After);

        // Nothing regains anything when the module is turned off, or when it is already in the state asked for.
        Assert.Equal(0, (await _admin.Modules.SwitchAsync(Accounting, true, Why())).Data<ModuleSwitchResult>().RolesRegainingGrants);
        var off = (await _admin.Modules.SwitchAsync(Accounting, false, Why())).Data<ModuleSwitchResult>();
        Assert.False(off.Enabled);
        Assert.Equal(0, off.RolesRegainingGrants);
        Assert.Equal(1, await StoredGrantsAsync(first.Id)); // switching never touches a grant
    }

    [Fact]
    public async Task A_deleted_role_does_not_count_as_regaining_the_permission()
    {
        var role = await RoleHoldingExportAsync(Permissions.CostView, Export);
        await SwitchAsync(false);
        var baseline = (await SwitchAsync(true)).RolesRegainingGrants;
        await SwitchAsync(false);
        (await _admin.DeleteRoleAsync(role)).ShouldBe(HttpStatusCode.NoContent);
        _made.Remove(role);

        Assert.Equal(baseline - 1, (await SwitchAsync(true)).RolesRegainingGrants);
    }

    // ----- containment (acceptance 6) -----
    [Fact]
    public async Task A_role_manager_who_does_not_hold_the_permission_cannot_edit_a_role_that_holds_it_off_or_on_nor_grant_it()
    {
        var role = await RoleHoldingExportAsync(Permissions.StockRead, Permissions.CostView, Export);
        var manager = (await _admin.CreateAsync(Unique.Name("Role manager"), Permissions.RolesRead, Permissions.RolesManage, Permissions.StockRead, Permissions.CostView)).Data<RoleDto>();
        _made.Add(manager);
        var username = Unique.Username("rm");
        var password = $"Pw-{Guid.NewGuid():N}-1!";
        (await _admin.Users.CreateAsync(new UserSaveRequest(username, Unique.Email(username), "Role manager", manager.Code, true, password, [fixture.Reference.DefaultWarehouseId]))).ShouldBeOk();
        var client = fixture.CreateClient();
        client.AccessToken = (await client.Auth.LoginAsync(username, password)).Data<AuthResult>().AccessToken;

        // Module on: the manager sees the role and the permission, and is refused like for any permission they lack.
        (await client.ReplaceAsync(await ReadAsync(role.Id), [Permissions.StockRead, Permissions.CostView])).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await client.CreateAsync(Unique.Name("Nope"), Permissions.CostView, Export)).ShouldBeProblem(HttpStatusCode.Forbidden);
        await SwitchAsync(false);

        // Module off: the grant is hidden but still counts, so the role is still beyond the manager.
        var seen = await ReadAsync(role.Id);
        Assert.DoesNotContain(Export, seen.Permissions);
        (await client.ReplaceAsync(seen, [Permissions.StockRead])).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await client.UpdateRoleAsync(seen, name: Unique.Name("Taken over"))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await client.DeleteRoleAsync(seen)).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(1, await StoredGrantsAsync(role.Id));
    }

    // ----- a copy, the token -----
    [Fact]
    public async Task A_copy_of_a_role_takes_what_the_source_shows_so_a_hidden_grant_is_not_copied_unseen()
    {
        var source = await RoleHoldingExportAsync(Permissions.StockRead, Permissions.CostView, Export);
        await SwitchAsync(false);

        var copy = (await _admin.PostAsync("/api/roles", new RoleCreateRequest(Unique.Name("Copy"), null, source.Id))).Data<RoleDto>();
        _made.Add(copy);

        Assert.Equal([Permissions.StockRead, Permissions.CostView], copy.Permissions);
        Assert.Equal(0, await StoredGrantsAsync(copy.Id));
        Assert.Equal(1, await StoredGrantsAsync(source.Id)); // the source keeps its own
        await SwitchAsync(true);
        Assert.DoesNotContain(Export, (await ReadAsync(copy.Id)).Permissions);
    }

    [Fact]
    public async Task A_person_with_a_hidden_grant_still_gets_it_in_the_token_and_in_their_own_permission_list_while_the_module_is_off()
    {
        var role = await RoleHoldingExportAsync(Permissions.StockRead, Permissions.CostView, Export);
        await SwitchAsync(false);
        var username = Unique.Username("acct");
        var password = $"Pw-{Guid.NewGuid():N}-1!";
        (await _admin.Users.CreateAsync(new UserSaveRequest(username, Unique.Email(username), "Accountant", role.Code, true, password, [fixture.Reference.DefaultWarehouseId]))).ShouldBeOk();

        var login = (await fixture.CreateClient().Auth.LoginAsync(username, password)).Data<AuthResult>();

        Assert.Contains(Export, login.User.Permissions); // the gate, not the claim, hides the feature (the web shows a menu item by module AND permission)
        Assert.Empty(login.User.Modules);
    }
}
