using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Roles;
using Ims.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// PL2.1 acceptance 5: the CanDisable seam. A module that has an <see cref="IModuleDisableCheck"/> refuses to be turned off while the check counts something (409 with the count, nothing
/// written, no audit event); with 0 it goes off; a module without a check is never refused; turning on and a switch that changes nothing never ask. No module registers a check yet (purchasing does
/// from PL2.3), so a fake stands in, registered in a host of its own. Real SQL Server; every test starts and ends with no module row.
/// </summary>
[Collection(ApiCollection.Name)]
public class ModuleDisableCheckTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Purchasing = ModuleCodes.Purchasing;

    private WebApplicationFactory<Program>? _host;

    public Task InitializeAsync() => ResetAsync();

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
        await ResetAsync();
    }

    private Task ResetAsync() => Ims.Api.Tests.Tests.Purchasing.PurchasingScenario.ResetAsync(fixture); // also closes any order another test left open: the purchasing check refuses to switch off over one

    private static string Why() => $"Disable check test {Guid.NewGuid():N}"[..28];

    private async Task<ApiClient> AdminWithAsync(params IModuleDisableCheck?[] checks)
    {
        _host = fixture.CreateHost(b => b.ConfigureTestServices(s =>
        {
            foreach (var check in checks) if (check is not null) s.AddSingleton<IModuleDisableCheck>(check);
        }));
        var admin = ApiFixture.CreateClient(_host);
        admin.AccessToken = (await fixture.AsAsync(Roles.Admin)).AccessToken;
        return admin;
    }

    private Task<int> ModuleAuditEventsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'"); // the audit log is append-only

    private static async Task<bool> IsOnAsync(ApiClient client, string code) => (await client.Modules.ListAsync()).Data<List<ModuleDto>>().Single(m => m.Code == code).Enabled;

    [Fact]
    public async Task A_check_that_counts_open_data_refuses_the_switch_off_with_409_and_the_count_and_writes_nothing()
    {
        var check = new FakeCheck(Purchasing, 3);
        var admin = await AdminWithAsync(check);
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk();
        var audit = await ModuleAuditEventsAsync();

        var problem = (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains("3 fake orders are open", problem.Detail);
        Assert.Contains("Purchasing", problem.Detail);
        Assert.Contains("cannot be turned off", problem.Detail);
        Assert.True(await IsOnAsync(admin, Purchasing)); // still on, in the list and in the row
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = N'Module.purchasing' AND [Value] = N'true'"));
        Assert.Equal(audit, await ModuleAuditEventsAsync()); // no audit row for a refused switch
    }

    [Fact]
    public async Task When_the_check_counts_nothing_the_switch_off_works_and_is_audited()
    {
        var check = new FakeCheck(Purchasing, 0);
        var admin = await AdminWithAsync(check);
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk();
        check.Count = 3;
        (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeProblem(HttpStatusCode.Conflict);
        check.Count = 0;
        var audit = await ModuleAuditEventsAsync();

        (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeOk();

        Assert.False(await IsOnAsync(admin, Purchasing));
        Assert.Equal(audit + 1, await ModuleAuditEventsAsync());
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk(); // and it can be turned on again
        Assert.True(await IsOnAsync(admin, Purchasing));
    }

    [Fact]
    public async Task A_check_of_another_module_never_refuses_a_module_it_does_not_guard()
    {
        var admin = await AdminWithAsync(new FakeCheck("other", 3)); // a code that is not purchasing: its check must not be asked for purchasing
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk();

        (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeOk();

        Assert.False(await IsOnAsync(admin, Purchasing));
    }

    [Fact]
    public async Task With_nothing_to_count_the_real_check_lets_every_module_go_off()
    {
        var admin = await AdminWithAsync();
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk();

        (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeOk();

        Assert.False(await IsOnAsync(admin, Purchasing));
    }

    [Fact]
    public async Task Turning_on_and_a_switch_that_changes_nothing_never_ask_the_check()
    {
        var check = new FakeCheck(Purchasing, 3);
        var admin = await AdminWithAsync(check);

        (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeOk(); // already off, and there is no row
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk();
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk(); // already on

        Assert.Equal(0, check.Calls);
        (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(1, check.Calls); // only a real switch off asks, once
    }

    [Fact]
    public async Task A_refused_switch_off_does_not_hide_the_surface_the_module_gate_still_lets_the_module_through()
    {
        var admin = await AdminWithAsync(new FakeCheck(Purchasing, 2));
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk();
        (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeProblem(HttpStatusCode.Conflict);

        var permissions = (await admin.GetAsync("/api/permissions")).Data<List<PermissionDto>>().Select(p => p.Code);

        Assert.Contains(Permissions.PoRead, permissions); // the module is still on, so its permissions are still offered
        Assert.Contains(Permissions.PoManage, permissions);
    }

    [Fact]
    public async Task A_check_that_throws_is_a_500_and_never_a_silent_yes_the_switch_is_rolled_back_with_no_audit_row()
    {
        var admin = await AdminWithAsync(new FakeCheck(Purchasing, 0), new ThrowingCheck(Purchasing));
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk();
        var audit = await ModuleAuditEventsAsync();

        (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeProblem(HttpStatusCode.InternalServerError);

        Assert.True(await IsOnAsync(admin, Purchasing)); // still on, in the list and in the row
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = N'Module.purchasing' AND [Value] = N'true'"));
        Assert.Equal(audit, await ModuleAuditEventsAsync());
    }

    [Fact]
    public async Task With_two_checks_on_one_module_the_second_refusing_is_enough_and_the_first_refusing_stops_the_asking()
    {
        var yes = new FakeCheck(Purchasing, 0);
        var refuses = new FakeCheck(Purchasing, 4);
        var admin = await AdminWithAsync(yes, refuses);
        (await admin.Modules.SwitchAsync(Purchasing, true, Why())).ShouldBeOk();

        var problem = (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains("4 fake orders are open", problem.Detail);
        Assert.Equal((1, 1), (yes.Calls, refuses.Calls));
        Assert.True(await IsOnAsync(admin, Purchasing));
        yes.Count = 2; // now the first refuses: the second is not asked
        var first = (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains("2 fake orders are open", first.Detail);
        Assert.Equal(1, refuses.Calls);
        yes.Count = 0;
        refuses.Count = 0;
        (await admin.Modules.SwitchAsync(Purchasing, false, Why())).ShouldBeOk(); // when none counts anything it goes off
    }

    [Fact]
    public async Task The_checks_of_the_real_host_each_guard_a_module_of_the_catalog_and_purchasing_has_exactly_one()
    {
        using var scope = fixture.Host.Services.CreateScope();

        var checks = scope.ServiceProvider.GetServices<IModuleDisableCheck>().ToList();

        Assert.NotEmpty(checks);
        Assert.All(checks, c => Assert.True(ModuleCatalog.IsKnown(c.ModuleCode), $"{c.GetType().Name} guards '{c.ModuleCode}', which is not a module of the catalog"));
        Assert.Single(checks, c => c.ModuleCode == ModuleCodes.Purchasing);
        var enableChecks = scope.ServiceProvider.GetServices<IModuleEnableCheck>().ToList();
        Assert.All(enableChecks, c => Assert.True(ModuleCatalog.IsKnown(c.ModuleCode), $"{c.GetType().Name} guards unknown module {c.ModuleCode}"));
        Assert.Single(enableChecks, c => c.ModuleCode == ModuleCodes.Purchasing);
        await Task.CompletedTask;
    }

    private sealed class FakeCheck(string moduleCode, int count) : IModuleDisableCheck
    {
        public int Count { get; set; } = count;
        public int Calls { get; private set; }
        public string ModuleCode => moduleCode;

        public Task<int> CountBlockersAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Count);
        }

        public string Describe(int count) => $"{count} fake orders are open";
    }

    private sealed class ThrowingCheck(string moduleCode) : IModuleDisableCheck
    {
        public string ModuleCode => moduleCode;
        public Task<int> CountBlockersAsync(CancellationToken ct) => throw new InvalidOperationException("the count failed");
        public string Describe(int count) => "unreachable";
    }
}
