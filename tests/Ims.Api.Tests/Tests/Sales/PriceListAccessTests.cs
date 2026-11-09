using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.11 acceptance 3: who may do what. While the module is off every endpoint is a 404 for every signed-in caller and a 401 for no token; with it on, only <c>pricelist.manage</c> opens a price list
/// (a forged token without it is 403 even with the role claim of an Administrator, and so.manage, cost.view and valuation.view do not stand in for it), and a price is gated by that permission and by
/// nothing else: it is never tied to <c>cost.view</c>. The audit trail shows the prices of a save only to a reader who holds it too.
/// </summary>
[Collection(ApiCollection.Name)]
public class PriceListAccessTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private ApiClient Forged(IEnumerable<string> permissions) =>
        fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private static IEnumerable<string> EveryPermissionExcept(params string[] codes) => PermissionCatalog.Codes.Except(codes);

    private static IReadOnlyList<(string Name, Func<ApiClient, Task<ApiResponse>> Call)> Endpoints(PriceListDto list, PriceListSaveRequest request) =>
    [
        ("list", c => c.PriceLists.ListAsync()),
        ("get", c => c.PriceLists.GetAsync(list.Id)),
        ("create", c => c.PriceLists.CreateAsync(request)),
        ("update", c => c.PriceLists.UpdateAsync(list.Id, request)),
        ("customer", c => c.PriceLists.SetCustomerAsync(list.Id, null)),
        ("delete", c => c.PriceLists.DeleteAsync(Guid.NewGuid()))
    ];

    private async Task<(PriceListDto List, PriceListSaveRequest Request)> OneAsync()
    {
        var product = await _s.NewProductAsync();
        var list = await _s.NewListAsync(_s.List().WithItem(product.Id, 3m));
        return (list, _s.List().WithItem(product.Id, 4m).Build());
    }

    [Fact]
    public async Task While_the_module_is_off_every_endpoint_is_404_for_every_signed_in_caller_and_401_for_no_token()
    {
        var (list, request) = await OneAsync();
        await _s.TurnOffAsync(); // the module is off, and a test starts with no lists: the list is gone, the 404 must come from the gate and not from that
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList");

        foreach (var (name, call) in Endpoints(list, request))
        {
            foreach (var who in new[] { _s.Admin, _s.Owner, _s.Supervisor, _s.Clerk, _s.Staff, Forged(EveryPermissionExcept()), Forged([]) })
                (await call(who)).ShouldBeProblem(HttpStatusCode.NotFound);
            Assert.True((await call(fixture.Anonymous())).StatusCode == 401, $"{name} without a token must be 401");
        }

        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList"));
    }

    [Fact]
    public async Task The_404_of_an_off_module_is_the_404_of_a_route_that_does_not_exist()
    {
        await _s.TurnOffAsync();

        var gated = (await _s.Admin.PriceLists.ListAsync()).ShouldBeProblem(HttpStatusCode.NotFound);
        var unknown = (await _s.Admin.GetAsync("/api/price-lists-no-such-route")).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.Equal((unknown.Title, unknown.Detail), (gated.Title, gated.Detail));
    }

    [Fact]
    public async Task A_forged_token_without_pricelist_manage_is_403_on_every_endpoint_whatever_else_it_holds()
    {
        var (list, request) = await OneAsync();
        var everythingButIt = EveryPermissionExcept(Permissions.PricelistManage); // so.manage, cost.view and valuation.view among them

        foreach (var (_, call) in Endpoints(list, request))
        {
            (await call(Forged(everythingButIt))).ShouldBeProblem(HttpStatusCode.Forbidden);
            (await call(Forged([Permissions.SoManage, Permissions.SoRead, Permissions.CostView, Permissions.ValuationView, Permissions.MasterDataRead]))).ShouldBeProblem(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task A_forged_token_with_only_pricelist_manage_is_let_in_and_sees_the_prices_without_cost_view()
    {
        var (list, request) = await OneAsync();
        var only = Forged([Permissions.PricelistManage]);

        foreach (var (name, call) in Endpoints(list, request))
            Assert.NotEqual(HttpStatusCode.Forbidden, (await call(only)).Status);
        var read = (await only.PriceLists.GetAsync(list.Id)).Data<PriceListDto>();
        Assert.Equal(3m, Assert.Single(read.Items).Price);
        Assert.Contains("\"price\":3", (await only.PriceLists.GetAsync(list.Id)).Body);
    }

    [Fact]
    public async Task The_default_roles_the_administrator_the_owner_and_the_supervisor_manage_price_lists_and_the_clerk_and_warehouse_staff_cannot()
    {
        var (list, request) = await OneAsync();

        foreach (var who in new[] { _s.Admin, _s.Owner, _s.Supervisor })
            foreach (var (_, call) in Endpoints(list, request).Where(e => e.Name is "list" or "get"))
                Assert.Equal(HttpStatusCode.OK, (await call(who)).Status);
        foreach (var who in new[] { _s.Clerk, _s.Staff }) // the clerk holds so.manage: an order editor is not a price-list editor
            foreach (var (_, call) in Endpoints(list, request))
                (await call(who)).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_audit_reader_without_pricelist_manage_sees_the_event_but_no_price_and_one_with_it_sees_the_prices_in_words()
    {
        var product = await _s.NewProductAsync(b => b.WithSku("SEE-ME"));
        var list = await _s.NewListAsync(_s.List().Default().WithItem(product.Id, 7.77m));
        async Task<string> BodyAsync(ApiClient reader) =>
            (await reader.Audit.QueryAsync(entityType: AuditEntities.PriceList, entityId: list.Id.ToString(), action: AuditActions.PriceListCreated)).ShouldBeOk().Body;

        var blind = await BodyAsync(Forged([Permissions.AuditRead, Permissions.SoManage, Permissions.CostView, Permissions.ValuationView]));
        var sighted = await BodyAsync(Forged([Permissions.AuditRead, Permissions.PricelistManage]));

        Assert.Contains(AuditActions.PriceListCreated, blind);   // the event is there
        Assert.DoesNotContain("ListPriceChanges", blind);
        Assert.DoesNotContain("7.77", blind);
        Assert.Contains("ListPriceChanges", sighted);
        Assert.Contains("SEE-ME", sighted);
        Assert.Contains("7.77", sighted);
    }
}
