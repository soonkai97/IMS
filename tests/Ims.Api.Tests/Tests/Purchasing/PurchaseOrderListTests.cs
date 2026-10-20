using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Purchasing;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// The list of purchase orders: newest first, seek paging with an encrypted cursor that only this list accepts, and the filters (status, supplier, warehouse, date, number), each of which
/// counts only what it returns.
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderListTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;
    private Ims.Application.Features.Warehouses.WarehouseDto _warehouse = null!;
    private Ims.Application.Features.Catalog.ProductDto _product = null!;

    public async Task InitializeAsync()
    {
        _p = await PurchasingScenario.CreateAsync(fixture);
        _warehouse = await _p.NewWarehouseAsync();
        _product = await _p.NewProductAsync();
    }

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private async Task<PagedResult<PurchaseOrderListItem>> ListAsync(PurchaseOrderStatus? status = null, Guid? supplier = null, string? search = null, string? from = null, string? to = null,
        int pageSize = 50, string? cursor = null, bool includeTotal = true) =>
        (await _p.Admin.PurchaseOrders.ListAsync(status, supplier, _warehouse.Id, search, from, to, pageSize, cursor: cursor, includeTotal: includeTotal)).Data<PagedResult<PurchaseOrderListItem>>();

    private async Task<List<PurchaseOrderDto>> CreateManyAsync(int count, Guid? supplier = null)
    {
        var orders = new List<PurchaseOrderDto>();
        for (var i = 0; i < count; i++) orders.Add(await _p.CreateAsync(_warehouse.Id, _product.Id, supplierId: supplier));
        return orders;
    }

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(Limits.MalaysiaUtcOffsetHours)).ToString("yyyy-MM-dd");

    private static string Tomorrow => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(Limits.MalaysiaUtcOffsetHours).AddDays(1)).ToString("yyyy-MM-dd");

    [Fact]
    public async Task The_list_is_newest_first_and_a_row_carries_the_number_the_names_the_line_count_and_the_total()
    {
        var orders = await CreateManyAsync(3);

        var page = await ListAsync();

        Assert.Equal(orders.Select(o => o.No).Reverse(), page.Items.Select(i => i.No));
        var row = page.Items[0];
        Assert.Equal((PurchaseOrderStatus.Draft, _warehouse.Name, 1, 25m, orders[2].ConcurrencyToken), (row.Status, row.WarehouseName, row.LineCount, row.TotalAmount, row.ConcurrencyToken));
        Assert.Equal(3, page.Total);
        Assert.NotEmpty(row.SupplierName);
    }

    [Fact]
    public async Task Following_the_cursor_walks_every_order_once_in_order_and_the_last_page_has_none()
    {
        var orders = await CreateManyAsync(5);
        var seen = new List<string>();
        string? cursor = null;

        for (var pages = 0; pages < 10; pages++)
        {
            var page = await ListAsync(pageSize: 2, cursor: cursor, includeTotal: false);
            Assert.Equal(Ims.Application.Paging.TotalNotCounted, page.Total);
            seen.AddRange(page.Items.Select(i => i.No));
            cursor = page.NextCursor;
            if (cursor is null) break;
        }

        Assert.Equal(orders.Select(o => o.No).Reverse(), seen);
        Assert.Null(cursor);
    }

    [Fact]
    public async Task A_cursor_that_was_altered_or_made_for_another_list_is_refused()
    {
        await CreateManyAsync(3);
        var cursor = (await ListAsync(pageSize: 1)).NextCursor!;
        var documentCursor = (await _p.Admin.Documents.ListAsync(pageSize: 1)).Data<PagedResult<Ims.Application.Features.Documents.DocumentListItem>>().NextCursor;

        (await _p.Admin.PurchaseOrders.ListAsync(cursor: cursor + "A")).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Admin.PurchaseOrders.ListAsync(cursor: "not-a-cursor")).ShouldBeProblem(HttpStatusCode.BadRequest);
        if (documentCursor is not null) (await _p.Admin.PurchaseOrders.ListAsync(cursor: documentCursor)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.NotEqual(cursor, (await ListAsync(pageSize: 1)).Items[0].Id.ToString()); // the cursor is not the id of an order
    }

    [Fact]
    public async Task The_cursor_does_not_show_the_number_of_orders_or_an_id()
    {
        var orders = await CreateManyAsync(2);
        var cursor = (await ListAsync(pageSize: 1)).NextCursor!;

        Assert.DoesNotContain(orders[1].Id.ToString("N"), cursor);
        Assert.True(cursor.Length > 60, "an encrypted token, not a bare number");
    }

    [Fact]
    public async Task A_filter_by_status_returns_only_that_status_and_counts_only_those()
    {
        var orders = await CreateManyAsync(3);
        await _p.SetStatusAsync(orders[1], PurchaseOrderStatus.Approved);

        var approved = await ListAsync(PurchaseOrderStatus.Approved);
        var drafts = await ListAsync(PurchaseOrderStatus.Draft);
        var closed = await ListAsync(PurchaseOrderStatus.Closed);

        Assert.Equal([orders[1].No], approved.Items.Select(i => i.No));
        Assert.Equal(1, approved.Total);
        Assert.Equal([orders[2].No, orders[0].No], drafts.Items.Select(i => i.No));
        Assert.Equal(2, drafts.Total);
        Assert.Empty(closed.Items);
    }

    [Fact]
    public async Task A_filter_by_supplier_returns_the_orders_of_that_supplier_and_an_unknown_supplier_matches_nothing()
    {
        var other = await _p.NewPartnerAsync();
        var ofDefault = await CreateManyAsync(2);
        var ofOther = await CreateManyAsync(1, other.Id);

        var mine = await ListAsync(supplier: fixture.Reference.SupplierId);
        var theirs = await ListAsync(supplier: other.Id);
        var nobody = await ListAsync(supplier: Guid.NewGuid());

        Assert.Equal(ofDefault.Select(o => o.No).Reverse(), mine.Items.Select(i => i.No));
        Assert.Equal([ofOther[0].No], theirs.Items.Select(i => i.No));
        Assert.Equal(0, nobody.Total);
    }

    [Fact]
    public async Task A_filter_by_date_uses_the_Malaysia_day_of_creation_both_ends_included()
    {
        var orders = await CreateManyAsync(2);

        Assert.Equal(2, (await ListAsync(from: Today, to: Today)).Total);
        Assert.Equal(2, (await ListAsync(from: Today)).Total);
        Assert.Equal(0, (await ListAsync(from: Tomorrow)).Total);
        Assert.Equal(2, (await ListAsync(to: Tomorrow)).Total);
        // An order created "yesterday" in Malaysia is outside today and inside yesterday.
        await fixture.Database.ExecuteAsync("UPDATE PurchaseOrder SET CreatedAt = DATEADD(DAY, -2, CreatedAt) WHERE PublicId = @id", ("@id", orders[0].Id));
        Assert.Equal([orders[1].No], (await ListAsync(from: Today, to: Today)).Items.Select(i => i.No));
    }

    [Fact]
    public async Task A_range_that_ends_before_it_starts_is_a_400()
    {
        (await _p.Admin.PurchaseOrders.ListAsync(from: Tomorrow, to: Today)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_search_finds_an_order_by_a_part_of_its_number_and_a_too_short_or_too_long_search_is_a_400()
    {
        var orders = await CreateManyAsync(12);

        var found = await ListAsync(search: orders[9].No[^4..]);

        Assert.Equal([orders[9].No], found.Items.Select(i => i.No));
        Assert.Equal([orders[0].No], (await ListAsync(search: orders[0].No.ToLowerInvariant())).Items.Select(i => i.No)); // case does not matter
        (await _p.Admin.PurchaseOrders.ListAsync(search: "ab")).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Admin.PurchaseOrders.ListAsync(search: new string('x', Limits.SearchMaxLength + 1))).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Empty((await ListAsync(search: "50%_%")).Items); // wildcard characters of LIKE are text, not a pattern
    }

    [Fact]
    public async Task The_list_of_another_warehouse_does_not_contain_these_orders()
    {
        await CreateManyAsync(2);
        var other = await _p.NewWarehouseAsync();

        var page = (await _p.Admin.PurchaseOrders.ListAsync(warehouseId: other.Id)).Data<PagedResult<PurchaseOrderListItem>>();

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task A_page_deeper_than_the_offset_limit_is_refused_and_a_page_size_above_the_maximum_is_cut()
    {
        (await _p.Admin.PurchaseOrders.ListAsync(pageSize: 200, page: 100)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var page = (await _p.Admin.PurchaseOrders.ListAsync(pageSize: 100_000)).Data<PagedResult<PurchaseOrderListItem>>();
        Assert.Equal(Limits.MaxPageSize, page.PageSize);
    }
}
