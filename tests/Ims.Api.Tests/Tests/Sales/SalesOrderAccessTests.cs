using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.2 acceptance 5 to 7: who may do what. While the module is off every endpoint is a 404 for every signed-in caller and a 401 for no token; with it on, the permission decides (a forged
/// token without the codes is 403, even for the role claim of an Administrator); a caller without so.manage never receives a selling price, an amount or a total (the field is absent, not null),
/// and cost.view has nothing to do with it; and a caller scoped to some warehouses sees only their orders (404 by id, absent from the list, 400 on a write).
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderAccessTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private ApiClient Forged(IEnumerable<string> permissions) =>
        fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private static IEnumerable<string> EveryPermissionExcept(params string[] codes) => PermissionCatalog.Codes.Except(codes);

    private async Task<IReadOnlyList<(string Name, Func<ApiClient, Task<ApiResponse>> Call)>> EndpointsAsync(SalesOrderDto order)
    {
        var request = _s.Request(order.WarehouseId, order.Lines[0].ProductId).Build();
        return
        [
            ("list", c => c.SalesOrders.ListAsync()),
            ("get", c => c.SalesOrders.GetAsync(order.Id)),
            ("create", c => c.SalesOrders.CreateAsync(request)),
            ("update", c => c.SalesOrders.UpdateAsync(order.Id, request))
        ];
    }

    // ----- the gate -----
    [Fact]
    public async Task While_the_module_is_off_every_endpoint_is_404_for_every_signed_in_caller_and_401_for_no_token()
    {
        var order = await _s.CreateAsync((await _s.NewWarehouseAsync()).Id, (await _s.NewProductAsync()).Id);
        var endpoints = await EndpointsAsync(order);
        await _s.TurnOffAsync();
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder");

        foreach (var (name, call) in endpoints)
        {
            foreach (var who in new[] { _s.Admin, _s.Owner, _s.Supervisor, _s.Clerk, _s.Staff, Forged(EveryPermissionExcept()), Forged([]) })
                (await call(who)).ShouldBeProblem(HttpStatusCode.NotFound);
            Assert.True((await call(fixture.Anonymous())).StatusCode == 401, $"{name} without a token must be 401");
        }

        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder"));
    }

    [Fact]
    public async Task The_404_of_an_off_module_is_the_404_of_a_route_that_does_not_exist()
    {
        await _s.TurnOffAsync();

        var gated = (await _s.Admin.SalesOrders.ListAsync()).ShouldBeProblem(HttpStatusCode.NotFound);
        var unknown = (await _s.Admin.GetAsync("/api/sales-orders-no-such-route")).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.Equal((unknown.Title, unknown.Detail), (gated.Title, gated.Detail));
    }

    // ----- the permissions -----
    [Fact]
    public async Task A_forged_token_without_so_read_and_so_manage_is_403_on_every_endpoint_even_with_the_role_claim_of_an_administrator()
    {
        var order = await _s.CreateAsync((await _s.NewWarehouseAsync()).Id, (await _s.NewProductAsync()).Id);

        foreach (var (_, call) in await EndpointsAsync(order))
            (await call(Forged(EveryPermissionExcept(Permissions.SoRead, Permissions.SoManage)))).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_forged_token_with_so_read_only_reads_and_one_with_so_manage_only_writes()
    {
        var order = await _s.CreateAsync((await _s.NewWarehouseAsync()).Id, (await _s.NewProductAsync()).Id);
        var readOnly = Forged(EveryPermissionExcept(Permissions.SoManage));
        var writeOnly = Forged(EveryPermissionExcept(Permissions.SoRead));

        foreach (var (name, call) in await EndpointsAsync(order))
        {
            var reads = name is "list" or "get";
            (await call(reads ? writeOnly : readOnly)).ShouldBeProblem(HttpStatusCode.Forbidden);
            if (name != "create") Assert.NotEqual(HttpStatusCode.Forbidden, (await call(reads ? readOnly : writeOnly)).Status);
        }
    }

    [Fact]
    public async Task The_default_roles_read_and_write_as_the_owner_decided_supervisor_clerk_and_owner_manage_staff_only_reads()
    {
        var order = await _s.CreateAsync(fixture.Reference.DefaultWarehouseId, (await _s.NewProductAsync()).Id);
        var request = _s.Request(fixture.Reference.DefaultWarehouseId, order.Lines[0].ProductId).Build();

        foreach (var who in new[] { _s.Admin, _s.Supervisor, _s.Clerk, _s.Owner })
        {
            (await who.SalesOrders.ListAsync()).ShouldBeOk();
            (await who.SalesOrders.GetAsync(order.Id)).ShouldBeOk();
            (await who.SalesOrders.CreateAsync(request)).ShouldBeOk();
        }
        (await _s.Staff.SalesOrders.ListAsync()).ShouldBeOk();
        (await _s.Staff.SalesOrders.GetAsync(order.Id)).ShouldBeOk();
        (await _s.Staff.SalesOrders.CreateAsync(request)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await _s.Staff.SalesOrders.UpdateAsync(order.Id, request)).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    // ----- selling price visibility (plan D11) -----
    private static readonly string[] MoneyKeys = ["unitPrice", "amount", "totalAmount"];

    /// <summary>The number of money keys anywhere in the body (the page, the rows, the lines).</summary>
    private static int MoneyKeysIn(string json)
    {
        var seen = 0;
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        if (MoneyKeys.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) seen++;
                        Walk(p.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray()) Walk(item);
                    break;
            }
        }
        Walk(JsonDocument.Parse(json).RootElement);
        return seen;
    }

    [Fact]
    public async Task A_caller_with_so_read_only_gets_no_price_no_amount_and_no_total_the_keys_are_absent_not_null()
    {
        var order = await _s.CreateAsync(fixture.Reference.DefaultWarehouseId, (await _s.NewProductAsync()).Id);

        var detail = await _s.Staff.SalesOrders.GetAsync(order.Id);
        var list = await _s.Staff.SalesOrders.ListAsync(warehouseId: fixture.Reference.DefaultWarehouseId);

        foreach (var response in new[] { detail, list })
        {
            response.ShouldBeOk();
            Assert.Equal(0, MoneyKeysIn(response.Body));
        }
        var read = detail.Data<SalesOrderDto>();
        Assert.Null(read.TotalAmount);
        Assert.All(read.Lines, l => Assert.Null(l.UnitPrice ?? l.Amount));
        Assert.Equal(order.No, read.No);      // what is not money is still there
        Assert.Equal(10m, read.TotalQty); // quantities stay
    }

    [Fact]
    public async Task A_caller_with_so_manage_gets_the_prices_and_one_without_cost_view_still_does()
    {
        var order = await _s.CreateAsync(fixture.Reference.DefaultWarehouseId, (await _s.NewProductAsync()).Id);
        var managerWithoutCost = Forged(EveryPermissionExcept(Permissions.CostView));

        foreach (var who in new[] { _s.Admin, _s.Owner, _s.Supervisor, _s.Clerk, managerWithoutCost })
        {
            var detail = await who.SalesOrders.GetAsync(order.Id);
            var read = detail.Data<SalesOrderDto>();
            Assert.Equal(25m, read.TotalAmount);
            Assert.Equal((2.5m, 25m), (read.Lines[0].UnitPrice, read.Lines[0].Amount));
            Assert.True(MoneyKeysIn(detail.Body) >= 3);
            var row = (await who.SalesOrders.ListAsync(warehouseId: fixture.Reference.DefaultWarehouseId)).Data<PagedResult<SalesOrderListItem>>().Items.Single(i => i.Id == order.Id);
            Assert.Equal(25m, row.TotalAmount);
        }
    }

    [Fact]
    public async Task A_custom_role_with_so_read_but_not_so_manage_sees_the_order_without_money_whatever_else_it_holds()
    {
        var order = await _s.CreateAsync(fixture.Reference.DefaultWarehouseId, (await _s.NewProductAsync()).Id);

        var response = await Forged(EveryPermissionExcept(Permissions.SoManage)).SalesOrders.GetAsync(order.Id); // cost.view is held, and does not matter

        response.ShouldBeOk();
        Assert.Equal(0, MoneyKeysIn(response.Body));
    }

    [Fact]
    public void Every_money_field_of_a_sales_order_contract_is_gated_by_so_manage_and_only_by_it()
    {
        var gated = new[] { typeof(SalesOrderDto).GetProperty(nameof(SalesOrderDto.TotalAmount)), typeof(SalesOrderLineDto).GetProperty(nameof(SalesOrderLineDto.UnitPrice)),
            typeof(SalesOrderLineDto).GetProperty(nameof(SalesOrderLineDto.Amount)), typeof(SalesOrderListItem).GetProperty(nameof(SalesOrderListItem.TotalAmount)) };

        Assert.All(gated, p => Assert.Equal(Permissions.SoManage, ((VisibleWithAttribute)Attribute.GetCustomAttribute(p!, typeof(VisibleWithAttribute))!).Permission));
        var money = typeof(SalesOrderDto).Assembly.GetTypes().Where(t => t.Namespace == typeof(SalesOrderDto).Namespace && t.Name.StartsWith("SalesOrder"))
            .SelectMany(t => t.GetProperties()).Where(p => p.PropertyType == typeof(decimal?) && p.Name != nameof(SalesOrderDto.TotalQty));
        Assert.All(money, p => Assert.NotNull(Attribute.GetCustomAttribute(p, typeof(VisibleWithAttribute)))); // a new money field cannot be added without its gate
    }

    [Fact]
    public void No_sales_order_contract_carries_a_cost_or_a_margin()
    {
        var contracts = typeof(SalesOrderDto).Assembly.GetTypes().Where(t => t.Namespace == typeof(SalesOrderDto).Namespace && t.Name.StartsWith("SalesOrder"))
            .Concat([typeof(SalesOrder), typeof(SalesOrderLine)]);

        foreach (var type in contracts)
            Assert.DoesNotContain(type.GetProperties().Select(p => p.Name), n => n.Contains("Cost", StringComparison.OrdinalIgnoreCase) || n.Contains("Margin", StringComparison.OrdinalIgnoreCase));
    }

    // ----- warehouse scope -----
    [Fact]
    public async Task A_caller_scoped_to_one_warehouse_sees_only_its_orders_and_an_order_elsewhere_is_404_by_id()
    {
        var mine = await _s.NewWarehouseAsync();
        var other = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var inMine = await _s.CreateAsync(mine.Id, product.Id);
        var inOther = await _s.CreateAsync(other.Id, product.Id);
        var (clerk, _) = await new AccountFactory(fixture).SignInAsync(await new AccountFactory(fixture).CreateAsync(Roles.Clerk, [mine.Id]));

        var listed = (await clerk.SalesOrders.ListAsync()).Data<PagedResult<SalesOrderListItem>>().Items;
        (await clerk.SalesOrders.GetAsync(inMine.Id)).ShouldBeOk();
        (await clerk.SalesOrders.GetAsync(inOther.Id)).ShouldBeProblem(HttpStatusCode.NotFound);
        var filtered = (await clerk.SalesOrders.ListAsync(warehouseId: other.Id)).Data<PagedResult<SalesOrderListItem>>();

        Assert.Contains(listed, i => i.Id == inMine.Id);
        Assert.DoesNotContain(listed, i => i.Id == inOther.Id);
        Assert.Empty(filtered.Items);
        Assert.Equal(0, filtered.Total);
    }

    [Fact]
    public async Task A_caller_scoped_to_one_warehouse_cannot_create_in_another_the_same_400_as_a_document_nor_edit_an_order_there()
    {
        var mine = await _s.NewWarehouseAsync();
        var other = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var inOther = await _s.CreateAsync(other.Id, product.Id);
        var (clerk, _) = await new AccountFactory(fixture).SignInAsync(await new AccountFactory(fixture).CreateAsync(Roles.Clerk, [mine.Id]));

        var order = (await clerk.SalesOrders.CreateAsync(_s.Request(mine.Id, product.Id).Build())).Data<SalesOrderDto>(); // inside the scope it works
        var document = (await clerk.Documents.CreateIssueAsync(new DocumentBuilder(other.Id).WithPartner(fixture.Reference.CustomerId).WithLines(new Line(product.Id, 1m, 1m)).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        var outside = (await clerk.SalesOrders.CreateAsync(_s.Request(other.Id, product.Id).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await clerk.SalesOrders.UpdateAsync(inOther.Id, _s.Request(other.Id, product.Id).Build(), inOther.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.Equal(document.Detail, outside.Detail);
        Assert.Equal(mine.Id, order.WarehouseId);
        Assert.Equal(inOther.ConcurrencyToken, (await _s.Admin.SalesOrders.GetAsync(inOther.Id)).Data<SalesOrderDto>().ConcurrencyToken);
    }
}
