using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Roles;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// PL3.15: the first module with prerequisites. <c>returns</c> requires <c>sales</c> and <c>purchasing</c>: turning it on is refused (409 naming what is off) while either is off, and turning either off
/// is refused (409 naming <c>returns</c>) while it is on; nothing is written and no audit event is made on a refusal; the state is read from the stored rows, not from the cache. Its permission
/// <c>returns.create</c> is offered only while it is on. Real SQL Server; every test starts and ends with no module row.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReturnsModuleTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Returns = ModuleCodes.Returns;
    private ApiClient _admin = null!;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _admin = await fixture.AsAsync(Roles.Admin);
    }

    public Task DisposeAsync() => ResetAsync();

    private async Task ResetAsync()
    {
        await Sales.SalesScenario.ResetAsync(fixture);       // every module off, open orders closed, the two standard sales rules back
        await Purchasing.PurchasingScenario.ResetAsync(fixture);
    }

    private static string Why() => $"Returns test {Guid.NewGuid():N}"[..24];

    private Task<ApiResponse> SwitchAsync(string code, bool enabled) => _admin.Modules.SwitchAsync(code, enabled, Why());

    private Task<int> ModuleEventsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'");

    private Task<int> RowsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    private async Task<bool> IsOnAsync(string code) => (await _admin.Modules.ListAsync()).Data<List<ModuleDto>>().Single(m => m.Code == code).Enabled;

    [Fact]
    public async Task The_module_is_listed_off_with_its_two_prerequisites_and_the_other_two_modules_list_none()
    {
        var list = (await _admin.Modules.ListAsync()).Data<List<ModuleDto>>();

        var returns = list.Single(m => m.Code == Returns);
        Assert.Equal(("Returns", false), (returns.Name, returns.Enabled));
        Assert.Equal([ModuleCodes.Sales, ModuleCodes.Purchasing], returns.Requires);
        Assert.All(list.Where(m => m.Code != Returns), m => Assert.Empty(m.Requires));
    }

    [Fact]
    public async Task Turning_returns_on_is_409_naming_every_module_that_is_off_and_writes_nothing()
    {
        var events = await ModuleEventsAsync();

        var both = (await SwitchAsync(Returns, true)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.ModuleNeedsOthers("Returns", ["Sales", "Purchasing"]), both.Detail);

        (await SwitchAsync(ModuleCodes.Sales, true)).ShouldBeOk();
        var one = (await SwitchAsync(Returns, true)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.ModuleNeedsOthers("Returns", ["Purchasing"]), one.Detail);

        Assert.False(await IsOnAsync(Returns));
        Assert.Equal(1, await RowsAsync());                       // only the sales row
        Assert.Equal(events + 1, await ModuleEventsAsync());      // the sales switch; the two refusals wrote no event
    }

    [Fact]
    public async Task With_both_prerequisites_on_returns_turns_on_and_off_and_a_prerequisite_cannot_go_off_while_it_is_on()
    {
        (await SwitchAsync(ModuleCodes.Sales, true)).ShouldBeOk();
        (await SwitchAsync(ModuleCodes.Purchasing, true)).ShouldBeOk();
        (await SwitchAsync(Returns, true)).ShouldBeOk();
        Assert.True(await IsOnAsync(Returns));
        var events = await ModuleEventsAsync();

        var sales = (await SwitchAsync(ModuleCodes.Sales, false)).ShouldBeProblem(HttpStatusCode.Conflict);
        var purchasing = (await SwitchAsync(ModuleCodes.Purchasing, false)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.ModuleIsNeededBy("Sales", ["Returns"]), sales.Detail);
        Assert.Equal(Messages.ModuleIsNeededBy("Purchasing", ["Returns"]), purchasing.Detail);
        Assert.True(await IsOnAsync(ModuleCodes.Sales));
        Assert.True(await IsOnAsync(ModuleCodes.Purchasing));
        Assert.Equal(events, await ModuleEventsAsync());          // refusals wrote nothing

        (await SwitchAsync(Returns, false)).ShouldBeOk();         // returns itself is never refused
        (await SwitchAsync(ModuleCodes.Sales, false)).ShouldBeOk();
        (await SwitchAsync(ModuleCodes.Purchasing, false)).ShouldBeOk();
        Assert.Equal((false, false, false), (await IsOnAsync(Returns), await IsOnAsync(ModuleCodes.Sales), await IsOnAsync(ModuleCodes.Purchasing)));
    }

    [Fact]
    public async Task A_switch_that_changes_nothing_is_not_refused_even_when_the_prerequisites_are_off()
    {
        (await SwitchAsync(Returns, false)).ShouldBeOk();         // already off
        (await SwitchAsync(ModuleCodes.Sales, false)).ShouldBeOk();
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task The_prerequisites_are_read_from_the_stored_rows_and_not_from_the_cached_switches()
    {
        (await SwitchAsync(ModuleCodes.Sales, true)).ShouldBeOk();
        (await SwitchAsync(ModuleCodes.Purchasing, true)).ShouldBeOk();
        Assert.True(await IsOnAsync(ModuleCodes.Sales));          // the cache now says sales is on
        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] = N'Module.sales'"); // and the row says it is off, as after a flip in the same instant

        (await SwitchAsync(Returns, true)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.False(await fixture.Database.ScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM SystemSetting WHERE [Key] = N'Module.returns') THEN 1 ELSE 0 END"));
    }

    [Fact]
    public async Task A_stored_row_that_is_not_true_does_not_count_as_on()
    {
        (await SwitchAsync(ModuleCodes.Purchasing, true)).ShouldBeOk();
        await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.sales', N'false')");

        (await SwitchAsync(Returns, true)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_permission_returns_create_is_offered_only_while_the_module_is_on_with_its_module_and_its_requirements()
    {
        Assert.DoesNotContain((await _admin.GetAsync("/api/permissions")).Data<List<PermissionDto>>(), p => p.Code == Permissions.ReturnsCreate);

        (await SwitchAsync(ModuleCodes.Sales, true)).ShouldBeOk();
        (await SwitchAsync(ModuleCodes.Purchasing, true)).ShouldBeOk();
        (await SwitchAsync(Returns, true)).ShouldBeOk();
        var entry = (await _admin.GetAsync("/api/permissions")).Data<List<PermissionDto>>().Single(p => p.Code == Permissions.ReturnsCreate);

        Assert.Equal(Returns, entry.Module);
        Assert.Equal([Permissions.DocumentCreate, Permissions.StockRead], entry.Requires);
    }

    [Fact]
    public async Task Only_an_administrator_may_switch_the_module_and_a_refusal_still_comes_after_the_permission_check()
    {
        foreach (var role in new[] { Roles.Owner, Roles.Supervisor, Roles.Clerk, Roles.WarehouseStaff })
            (await (await fixture.AsAsync(role)).Modules.SwitchAsync(Returns, true, Why())).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(0, await RowsAsync());
    }
}
