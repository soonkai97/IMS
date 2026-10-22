using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Roles;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// PL2.1 acceptance 1 to 3 over the real API: the purchasing module is listed and switchable by an Administrator only, its permissions <c>po.read</c> and <c>po.manage</c> are offered only
/// while it is on, a role that holds one keeps it (hidden) through a save while it is off, and the <c>Requires</c> closure is enforced. The
/// role-page rules themselves are proven in <see cref="ModuleAwareRolesTests"/> over <c>po.read</c>; this class runs them over both purchasing codes. Every test starts and ends with no module row, and the roles it makes are deleted.
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchasingModuleTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Purchasing = ModuleCodes.Purchasing;
    private static readonly string[] Both = [Permissions.PoRead, Permissions.PoManage];
    private static readonly string[] Companions = [Permissions.MasterDataRead, Permissions.CostView, Permissions.StockRead];

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

    private Task ResetAsync() => Ims.Api.Tests.Tests.Purchasing.PurchasingScenario.ResetAsync(fixture); // also closes any order another test left open: the purchasing check refuses to switch off over one

    private static string Why() => $"Purchasing test {Guid.NewGuid():N}"[..24];

    private async Task<ModuleSwitchResult> SwitchAsync(bool enabled) => (await _admin.Modules.SwitchAsync(Purchasing, enabled, Why())).Data<ModuleSwitchResult>();

    private async Task<RoleDto> MakeAsync(params string[] permissions)
    {
        var role = (await _admin.CreateAsync(Unique.Name("Buyer"), permissions)).Data<RoleDto>();
        _made.Add(role);
        return role;
    }

    private async Task<RoleDto> ReadAsync(Guid id) => (await _admin.GetAsync($"/api/roles/{id}")).Data<RoleDto>();

    private Task<int> StoredGrantsAsync(Guid roleId) =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission rp JOIN Role r ON r.Id = rp.RoleId WHERE r.PublicId = @id AND rp.PermissionCode IN (@a, @b) AND rp.REC_DELETED = 0",
            ("@id", roleId), ("@a", Permissions.PoRead), ("@b", Permissions.PoManage));

    private Task<int> ModuleRowsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    [Fact]
    public async Task The_module_is_listed_off_and_an_administrator_switches_it_on_and_off_with_a_reason_and_an_audit_event()
    {
        var entry = (await _admin.Modules.ListAsync()).Data<List<ModuleDto>>().Single(m => m.Code == Purchasing);
        Assert.False(entry.Enabled);
        Assert.Equal("Purchasing", entry.Name);
        Assert.Empty(entry.Requires);

        var reason = Why();
        var on = (await _admin.Modules.SwitchAsync(Purchasing, true, reason)).Data<ModuleSwitchResult>();
        var off = await SwitchAsync(false);

        Assert.True(on.Enabled);
        Assert.False(off.Enabled);
        var page = (await _admin.Audit.QueryAsync(action: AuditActions.ModuleEnabled, entityId: "Module.purchasing")).Data<AuditPage>();
        Assert.Contains(page.Items, e => e.After!.Contains(reason, StringComparison.Ordinal));
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = N'Module.purchasing' AND [Value] = N'false'"));
    }

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task Only_the_administrator_switches_it_so_every_other_role_gets_403_and_nothing_changes(string role)
    {
        (await (await fixture.AsAsync(role)).Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(0, await ModuleRowsAsync());
    }

    [Fact]
    public async Task A_forged_token_that_holds_the_purchasing_permissions_but_not_modules_manage_is_403_because_the_claim_decides()
    {
        var holdingPurchasing = ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: [.. Both, .. Companions]);
        var withoutSwitch = ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: PermissionCatalog.Codes.Except([Permissions.ModulesManage]));

        (await fixture.CreateClient(holdingPurchasing).Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await fixture.CreateClient(withoutSwitch).Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(0, await ModuleRowsAsync());
    }

    [Fact]
    public async Task The_permissions_are_not_offered_and_not_accepted_while_the_module_is_off_and_are_offered_with_their_module_and_requirements_when_on()
    {
        var off = (await _admin.GetAsync("/api/permissions")).Data<List<PermissionDto>>().Select(p => p.Code).ToList();
        Assert.DoesNotContain(Permissions.PoRead, off);
        Assert.DoesNotContain(Permissions.PoManage, off);
        var problem = (await _admin.CreateAsync(Unique.Name("Nope"), [.. Companions, .. Both])).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("not available", problem.Detail);
        Assert.Contains(Permissions.PoRead, problem.Detail);

        await SwitchAsync(true);
        var on = (await _admin.GetAsync("/api/permissions")).Data<List<PermissionDto>>();

        var read = on.Single(p => p.Code == Permissions.PoRead);
        var manage = on.Single(p => p.Code == Permissions.PoManage);
        Assert.Equal(Purchasing, read.Module);
        Assert.Equal(Purchasing, manage.Module);
        Assert.Equal([Permissions.MasterDataRead], read.Requires);
        Assert.Equal([Permissions.PoRead, Permissions.CostView, Permissions.StockRead], manage.Requires);
        Assert.False(read.IsDangerous);
        Assert.False(manage.IsDangerous);
    }

    [Theory]
    [InlineData(Permissions.PoRead, new[] { Permissions.PoRead }, Permissions.MasterDataRead)]
    [InlineData(Permissions.PoManage, new[] { Permissions.PoManage, Permissions.CostView, Permissions.StockRead, Permissions.MasterDataRead }, Permissions.PoRead)]
    [InlineData(Permissions.PoManage, new[] { Permissions.PoManage, Permissions.PoRead, Permissions.StockRead, Permissions.MasterDataRead }, Permissions.CostView)]
    [InlineData(Permissions.PoManage, new[] { Permissions.PoManage, Permissions.PoRead, Permissions.CostView, Permissions.MasterDataRead }, Permissions.StockRead)]
    public async Task A_role_that_lacks_what_a_purchasing_permission_requires_is_refused_with_a_400_that_names_it(string code, string[] set, string missing)
    {
        await SwitchAsync(true);

        var problem = (await _admin.CreateAsync(Unique.Name("Open"), set)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains(code, problem.Detail);
        Assert.Contains(missing, problem.Detail);
    }

    [Fact]
    public async Task A_closed_set_is_saved_while_the_module_is_on()
    {
        await SwitchAsync(true);

        var role = await MakeAsync([.. Companions, .. Both]);

        Assert.Contains(Permissions.PoManage, role.Permissions);
        Assert.Equal(2, await StoredGrantsAsync(role.Id));
    }

    [Fact]
    public async Task A_role_keeps_both_grants_through_saves_while_the_module_is_off_and_the_closure_still_protects_what_they_need()
    {
        await SwitchAsync(true);
        var role = await MakeAsync([.. Companions, .. Both]);
        await SwitchAsync(false);
        var seen = await ReadAsync(role.Id);
        Assert.DoesNotContain(Permissions.PoRead, seen.Permissions); // hidden, so the editor cannot send it back

        var changed = (await _admin.ReplaceAsync(seen, [.. seen.Permissions, Permissions.DocumentCreate])).Data<RolePermissionsResult>();

        Assert.Equal([Permissions.DocumentCreate], changed.Added);
        Assert.Empty(changed.Removed);
        Assert.Equal(2, await StoredGrantsAsync(role.Id));
        // Dropping stock.read would leave the hidden po.manage without what it requires.
        var problem = (await _admin.ReplaceAsync(changed.Role, [Permissions.MasterDataRead, Permissions.CostView, Permissions.DocumentCreate])).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Contains(Permissions.PoManage, problem.Detail);
        Assert.Contains(Permissions.StockRead, problem.Detail);
        Assert.Equal(2, await StoredGrantsAsync(role.Id));

        await SwitchAsync(true);

        Assert.Equal(PermissionCatalog.InOrder([.. Companions, .. Both, Permissions.DocumentCreate]), (await ReadAsync(role.Id)).Permissions);
    }

    [Fact]
    public async Task Turning_the_module_on_counts_the_roles_that_hold_a_purchasing_permission_the_five_system_roles_and_the_roles_made_here()
    {
        await SwitchAsync(true);
        await MakeAsync([.. Companions, .. Both]);
        await MakeAsync(Permissions.MasterDataRead, Permissions.PoRead);
        await MakeAsync(Permissions.CostView);
        await SwitchAsync(false);
        var holders = await fixture.Database.ScalarAsync<int>(
            "SELECT COUNT(DISTINCT r.Id) FROM Role r JOIN RolePermission rp ON rp.RoleId = r.Id WHERE r.REC_DELETED = 0 AND rp.REC_DELETED = 0 AND rp.PermissionCode IN (@a, @b)",
            ("@a", Permissions.PoRead), ("@b", Permissions.PoManage));
        Assert.True(holders >= 7); // the five system roles after the grant migration, and two made here

        var on = await SwitchAsync(true);

        Assert.Equal(holders, on.RolesRegainingGrants);
    }
}
