using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Purchasing;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.2 acceptance 5 to 7: who may do what. While the module is off every endpoint is a 404 for every signed-in caller and a 401 for no token; with it on, the permission decides (a forged
/// token without the codes is 403, even for the role claim of an Administrator); a caller without cost.view never receives a price, an amount or a total, in the detail, the list or the
/// raw JSON; and a caller scoped to some warehouses sees only their orders (404 by id, absent from the list, 400 on a write).
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderAccessTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private ApiClient Forged(IEnumerable<string> permissions) =>
        fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private IEnumerable<string> EveryPermissionExcept(params string[] codes) => PermissionCatalog.Codes.Except(codes);

    private async Task<IReadOnlyList<(string Name, Func<ApiClient, Task<ApiResponse>> Call)>> EndpointsAsync(PurchaseOrderDto order)
    {
        var request = _p.Request(order.WarehouseId, (await _p.Admin.PurchaseOrders.GetAsync(order.Id)).Data<PurchaseOrderDto>().Lines[0].ProductId).Build();
        return
        [
            ("list", c => c.PurchaseOrders.ListAsync()),
            ("get", c => c.PurchaseOrders.GetAsync(order.Id)),
            ("create", c => c.PurchaseOrders.CreateAsync(request)),
            ("update", c => c.PurchaseOrders.UpdateAsync(order.Id, request))
        ];
    }

    // ----- acceptance 7: the gate -----
    [Fact]
    public async Task While_the_module_is_off_every_endpoint_is_404_for_every_signed_in_caller_and_401_for_no_token()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        var order = await _p.CreateAsync(warehouse.Id, product.Id);
        var endpoints = await EndpointsAsync(order);
        await _p.TurnOffAsync();
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder");

        foreach (var (name, call) in endpoints)
        {
            foreach (var who in new[] { _p.Admin, _p.Owner, _p.Supervisor, _p.Clerk, _p.Staff, Forged(EveryPermissionExcept()), Forged([]) })
                (await call(who)).ShouldBeProblem(HttpStatusCode.NotFound);
            Assert.True((await call(fixture.Anonymous())).StatusCode == 401, $"{name} without a token must be 401");
        }

        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder"));
    }

    [Fact]
    public async Task The_404_of_an_off_module_is_the_404_of_a_route_that_does_not_exist()
    {
        await _p.TurnOffAsync();

        var gated = (await _p.Admin.PurchaseOrders.ListAsync()).ShouldBeProblem(HttpStatusCode.NotFound);
        var unknown = (await _p.Admin.GetAsync("/api/purchase-orders-no-such-route")).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.Equal((unknown.Title, unknown.Detail), (gated.Title, gated.Detail));
    }

    // ----- acceptance 7: the permissions -----
    [Fact]
    public async Task A_forged_token_without_po_read_and_po_manage_is_403_on_every_endpoint_even_with_the_role_claim_of_an_administrator()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var order = await _p.CreateAsync(warehouse.Id, (await _p.NewProductAsync()).Id);
        var endpoints = await EndpointsAsync(order);

        foreach (var (_, call) in endpoints)
            (await call(Forged(EveryPermissionExcept(Permissions.PoRead, Permissions.PoManage)))).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_forged_token_with_po_read_only_reads_and_one_with_po_manage_only_writes()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var order = await _p.CreateAsync(warehouse.Id, (await _p.NewProductAsync()).Id);
        var endpoints = await EndpointsAsync(order);
        var readOnly = Forged(EveryPermissionExcept(Permissions.PoManage));
        var writeOnly = Forged(EveryPermissionExcept(Permissions.PoRead));

        foreach (var (name, call) in endpoints)
        {
            var reads = name is "list" or "get";
            (await call(reads ? writeOnly : readOnly)).ShouldBeProblem(HttpStatusCode.Forbidden);
            // The other holder is let through the door: whatever the endpoint then answers (the update has no If-Match here, a create may be refused by the data), it is not a 403.
            if (name != "create") Assert.NotEqual(HttpStatusCode.Forbidden, (await call(reads ? readOnly : writeOnly)).Status);
        }
    }

    [Fact]
    public async Task The_default_roles_read_and_write_as_the_owner_decided_supervisor_and_clerk_manage_owner_and_staff_only_read()
    {
        var order = await _p.CreateAsync(fixture.Reference.DefaultWarehouseId, (await _p.NewProductAsync()).Id);
        var request = _p.Request(fixture.Reference.DefaultWarehouseId, order.Lines[0].ProductId).Build();

        foreach (var who in new[] { _p.Admin, _p.Supervisor, _p.Clerk })
        {
            (await who.PurchaseOrders.ListAsync()).ShouldBeOk();
            (await who.PurchaseOrders.GetAsync(order.Id)).ShouldBeOk();
            (await who.PurchaseOrders.CreateAsync(request)).ShouldBeOk();
        }
        foreach (var who in new[] { _p.Owner, _p.Staff })
        {
            (await who.PurchaseOrders.ListAsync()).ShouldBeOk();
            (await who.PurchaseOrders.GetAsync(order.Id)).ShouldBeOk();
            (await who.PurchaseOrders.CreateAsync(request)).ShouldBeProblem(HttpStatusCode.Forbidden);
            (await who.PurchaseOrders.UpdateAsync(order.Id, request)).ShouldBeProblem(HttpStatusCode.Forbidden);
        }
    }

    private static readonly string[] MoneyKeys = ["unitPrice", "amount", "totalAmount"];

    /// <summary>Every money key anywhere in the body (the page, the rows, the lines) is JSON null: the withheld value is not sent, whatever the nesting. (The rule of the cost visibility pattern sets a tagged value to null.)</summary>
    private static void AssertNoMoney(string json)
    {
        var seen = 0;
        void Walk(System.Text.Json.JsonElement e)
        {
            switch (e.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        if (MoneyKeys.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) { seen++; Assert.Equal(System.Text.Json.JsonValueKind.Null, p.Value.ValueKind); }
                        Walk(p.Value);
                    }
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray()) Walk(item);
                    break;
            }
        }
        Walk(System.Text.Json.JsonDocument.Parse(json).RootElement);
        Assert.True(seen > 0, "the walk found no money key at all: it would pass for any body");
    }

    [Fact]
    public async Task A_caller_with_po_manage_but_without_cost_view_cannot_save_an_order_403_so_prices_they_cannot_see_are_never_overwritten()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        var order = await _p.CreateAsync(warehouse.Id, product.Id);
        var blind = Forged(EveryPermissionExcept(Permissions.CostView));
        var request = _p.Request(warehouse.Id, product.Id).Build();
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder");

        (await blind.PurchaseOrders.CreateAsync(request)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await blind.PurchaseOrders.UpdateAsync(order.Id, request, order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder"));
        var after = (await _p.Admin.PurchaseOrders.GetAsync(order.Id)).Data<PurchaseOrderDto>();
        Assert.Equal((order.TotalAmount, order.ConcurrencyToken), (after.TotalAmount, after.ConcurrencyToken)); // not one price moved
    }

    // ----- acceptance 5: cost visibility -----
    [Fact]
    public async Task A_caller_without_cost_view_gets_no_price_no_amount_and_no_total_in_the_detail_the_list_or_the_raw_json()
    {
        var order = await _p.CreateAsync(fixture.Reference.DefaultWarehouseId, (await _p.NewProductAsync()).Id);

        var detail = await _p.Staff.PurchaseOrders.GetAsync(order.Id);
        var list = await _p.Staff.PurchaseOrders.ListAsync(warehouseId: fixture.Reference.DefaultWarehouseId);

        foreach (var response in new[] { detail, list })
        {
            response.ShouldBeOk();
            AssertNoMoney(response.Body);
        }
        var read = detail.Data<PurchaseOrderDto>();
        Assert.Null(read.TotalAmount);
        Assert.All(read.Lines, l => Assert.Null(l.UnitPrice ?? l.Amount));
        Assert.Equal(order.No, read.No); // what is not money is still there
    }

    [Fact]
    public async Task A_caller_with_cost_view_gets_the_prices()
    {
        var order = await _p.CreateAsync(fixture.Reference.DefaultWarehouseId, (await _p.NewProductAsync()).Id);

        foreach (var who in new[] { _p.Admin, _p.Owner, _p.Supervisor, _p.Clerk })
        {
            var read = (await who.PurchaseOrders.GetAsync(order.Id)).Data<PurchaseOrderDto>();
            Assert.Equal(25m, read.TotalAmount);
            Assert.Equal((2.5m, 25m), (read.Lines[0].UnitPrice, read.Lines[0].Amount));
            var row = (await who.PurchaseOrders.ListAsync(warehouseId: fixture.Reference.DefaultWarehouseId)).Data<PagedResult<PurchaseOrderListItem>>().Items.Single(i => i.Id == order.Id);
            Assert.Equal(25m, row.TotalAmount);
        }
    }

    [Fact]
    public async Task A_custom_role_with_po_read_but_not_cost_view_sees_the_order_without_money()
    {
        var order = await _p.CreateAsync(fixture.Reference.DefaultWarehouseId, (await _p.NewProductAsync()).Id);

        var response = await Forged(EveryPermissionExcept(Permissions.CostView, Permissions.PoManage)).PurchaseOrders.GetAsync(order.Id);

        response.ShouldBeOk();
        AssertNoMoney(response.Body);
    }

    // ----- acceptance 6: warehouse scope -----
    [Fact]
    public async Task A_caller_scoped_to_one_warehouse_sees_only_its_orders_and_an_order_elsewhere_is_404_by_id()
    {
        var mine = await _p.NewWarehouseAsync();
        var other = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        var inMine = await _p.CreateAsync(mine.Id, product.Id);
        var inOther = await _p.CreateAsync(other.Id, product.Id);
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk, [mine.Id]);
        var (clerk, _) = await new AccountFactory(fixture).SignInAsync(account);

        var listed = (await clerk.PurchaseOrders.ListAsync()).Data<PagedResult<PurchaseOrderListItem>>().Items;
        (await clerk.PurchaseOrders.GetAsync(inMine.Id)).ShouldBeOk();
        (await clerk.PurchaseOrders.GetAsync(inOther.Id)).ShouldBeProblem(HttpStatusCode.NotFound);
        var filtered = (await clerk.PurchaseOrders.ListAsync(warehouseId: other.Id)).Data<PagedResult<PurchaseOrderListItem>>();

        Assert.Contains(listed, i => i.Id == inMine.Id);
        Assert.DoesNotContain(listed, i => i.Id == inOther.Id);
        Assert.Empty(filtered.Items);
        Assert.Equal(0, filtered.Total);
    }

    [Fact]
    public async Task A_caller_scoped_to_one_warehouse_cannot_create_in_another_the_same_400_as_a_document_nor_edit_an_order_there()
    {
        var mine = await _p.NewWarehouseAsync();
        var other = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        var inOther = await _p.CreateAsync(other.Id, product.Id);
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk, [mine.Id]);
        var (clerk, _) = await new AccountFactory(fixture).SignInAsync(account);

        var order = (await clerk.PurchaseOrders.CreateAsync(_p.Request(mine.Id, product.Id).Build())).Data<PurchaseOrderDto>(); // inside the scope it works
        var document = (await clerk.Documents.CreateReceiptAsync(new DocumentBuilder(other.Id).WithPartner(fixture.Reference.SupplierId).WithLines(new Line(product.Id, 1m, 1m)).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        var outside = (await clerk.PurchaseOrders.CreateAsync(_p.Request(other.Id, product.Id).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await clerk.PurchaseOrders.UpdateAsync(inOther.Id, _p.Request(other.Id, product.Id).Build(), inOther.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.Equal(document.Detail, outside.Detail);
        Assert.Equal(mine.Id, order.WarehouseId);
        Assert.Equal(inOther.ConcurrencyToken, (await _p.Admin.PurchaseOrders.GetAsync(inOther.Id)).Data<PurchaseOrderDto>().ConcurrencyToken);
    }

}
