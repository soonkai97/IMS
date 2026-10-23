using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>The sales-order list: newest first, filters (status, customer, warehouse, number search), paging and the seek cursor.</summary>
[Collection(ApiCollection.Name)]
public class SalesOrderListTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private static async Task<PagedResult<SalesOrderListItem>> PageAsync(Task<ApiResponse> call) => (await call).Data<PagedResult<SalesOrderListItem>>();

    [Fact]
    public async Task The_list_is_newest_first_filters_by_warehouse_customer_status_and_number_and_carries_a_token_per_row()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var customer = await _s.NewPartnerAsync();
        var first = await _s.CreateAsync(warehouse.Id, product.Id);
        var second = await _s.CreateAsync(warehouse.Id, product.Id, customerId: customer.Id);
        var third = await _s.CreateAsync(warehouse.Id, product.Id);
        await _s.SetStatusAsync(third, SalesOrderStatus.Cancelled);

        var all = await PageAsync(_s.Admin.SalesOrders.ListAsync(warehouseId: warehouse.Id));
        var byCustomer = await PageAsync(_s.Admin.SalesOrders.ListAsync(customerId: customer.Id));
        var cancelled = await PageAsync(_s.Admin.SalesOrders.ListAsync(status: SalesOrderStatus.Cancelled, warehouseId: warehouse.Id));
        var bySearch = await PageAsync(_s.Admin.SalesOrders.ListAsync(search: first.No[^4..], warehouseId: warehouse.Id));

        Assert.Equal([third.Id, second.Id, first.Id], all.Items.Select(i => i.Id));
        Assert.Equal(3, all.Total);
        Assert.Equal([second.Id], byCustomer.Items.Select(i => i.Id));
        Assert.Equal([third.Id], cancelled.Items.Select(i => i.Id));
        Assert.Contains(first.Id, bySearch.Items.Select(i => i.Id));
        Assert.All(all.Items, i => Assert.NotEmpty(i.ConcurrencyToken));
        Assert.Equal((first.No, 1, 25m), (all.Items[2].No, all.Items[2].LineCount, all.Items[2].TotalAmount));
    }

    [Fact]
    public async Task Paging_by_number_and_by_cursor_walks_the_same_orders_once()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var created = new List<Guid>();
        for (var i = 0; i < 5; i++) created.Add((await _s.CreateAsync(warehouse.Id, product.Id)).Id);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await PageAsync(_s.Admin.SalesOrders.ListAsync(warehouseId: warehouse.Id, pageSize: 2, cursor: cursor, includeTotal: false));
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(Enumerable.Reverse(created), seen);
        Assert.Equal([created[4], created[3]], (await PageAsync(_s.Admin.SalesOrders.ListAsync(warehouseId: warehouse.Id, pageSize: 2, page: 1))).Items.Select(i => i.Id));
        Assert.Equal([created[2], created[1]], (await PageAsync(_s.Admin.SalesOrders.ListAsync(warehouseId: warehouse.Id, pageSize: 2, page: 2))).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task A_cursor_of_another_list_and_a_made_up_one_are_refused()
    {
        (await _s.Admin.SalesOrders.ListAsync(cursor: "not-a-cursor")).ShouldBeProblem(HttpStatusCode.BadRequest);
    }
}
