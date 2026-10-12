using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.StockList;

/// <summary>
/// PS-04: the low-stock list, its total, the Home count and the export are one definition. A product with a safety level that was never stocked
/// is low (on hand 0), a negative balance is flagged, and products that are fine or have no safety level stay out.
/// </summary>
[Collection(ApiCollection.Name)]
public class LowStockTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    /// <summary>Every row of the list (all pages, 500 at a time): the list is long, and a row on page 2 is still on the list.</summary>
    private static async Task<List<LowStockAlert>> AlertsAsync(ApiClient who, Guid? warehouseId = null)
    {
        var rows = new List<LowStockAlert>();
        for (var page = 1; ; page++)
        {
            var result = (await who.GetAsync($"/api/stock/low-stock?page={page}&pageSize=500" + (warehouseId is null ? "" : $"&warehouseId={warehouseId}"))).Data<PagedResult<LowStockAlert>>();
            rows.AddRange(result.Items);
            if (rows.Count >= result.Total || result.Items.Count == 0) return rows;
        }
    }

    [Fact]
    public async Task A_product_with_a_safety_level_and_no_stock_row_is_low_with_zero_on_hand()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(safetyStock: 5);

        var alerts = await AlertsAsync(s.Admin);

        var alert = Assert.Single(alerts, a => a.ProductId == product.Id); // once, whatever the number of warehouses
        Assert.Equal((0m, 5m, 5m, false), (alert.QtyOnHand, alert.SafetyStock, alert.Shortfall, alert.IsNegative));
        Assert.Equal(product.Sku, alert.Sku);
        Assert.Null(alert.WarehouseId); // stocked nowhere: it belongs to no warehouse
    }

    [Fact]
    public async Task A_never_stocked_product_is_listed_once_and_a_warehouse_filter_leaves_it_out()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 5);

        var everywhere = await AlertsAsync(s.Admin);
        var inDefault = await AlertsAsync(s.Admin, Warehouse);
        var inOther = await AlertsAsync(s.Admin, other.Id);

        Assert.Single(everywhere, a => a.ProductId == product.Id);
        Assert.DoesNotContain(inDefault, a => a.ProductId == product.Id); // it belongs to no warehouse
        Assert.DoesNotContain(inOther, a => a.ProductId == product.Id);
        Assert.All(inOther, a => Assert.Equal(other.Id, a.WarehouseId));
    }

    [Fact]
    public async Task A_user_sees_a_never_stocked_product_once_and_nothing_of_other_warehouses()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 5);

        var theirs = await AlertsAsync(s.Staff);
        var inTheOtherOne = await AlertsAsync(s.Staff, other.Id);

        Assert.Single(theirs, a => a.ProductId == product.Id);
        Assert.Empty(inTheOtherOne);
        Assert.All(theirs, a => Assert.NotEqual(other.Id, a.WarehouseId));
    }

    [Fact]
    public async Task Products_without_a_safety_level_and_inactive_ones_stay_off_the_list()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var noSafety = await s.NewProductAsync(safetyStock: 0);
        var inactive = await s.NewProductAsync(safetyStock: 5, customize: b => b.Inactive());

        var alerts = await AlertsAsync(s.Admin);

        Assert.DoesNotContain(alerts, a => a.ProductId == noSafety.Id || a.ProductId == inactive.Id);
    }

    [Fact]
    public async Task A_product_stocked_somewhere_is_not_reported_for_the_warehouses_it_was_never_received_in()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 5);
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 50, 1m)); // plenty here, nothing in the other warehouse

        Assert.DoesNotContain(await AlertsAsync(s.Admin), a => a.ProductId == product.Id);
        Assert.DoesNotContain(await AlertsAsync(s.Admin, other.Id), a => a.ProductId == product.Id);
    }

    [Fact]
    public async Task A_product_that_runs_out_after_being_stocked_is_low_with_a_stock_row_at_zero()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(safetyStock: 5);
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 1m));
        await s.IssueAsync(Warehouse, new Line(product.Id, 3));

        var alert = Assert.Single(await AlertsAsync(s.Admin, Warehouse), a => a.ProductId == product.Id);

        Assert.Equal((0m, 5m), (alert.QtyOnHand, alert.Shortfall));
    }

    [Fact]
    public async Task A_negative_balance_is_flagged_and_listed_even_without_a_safety_level()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(safetyStock: 0);
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        await using (await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true))
        {
            var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 45)));
            await s.CompleteAsync(submitted, "Counted by hand, delivery is late");
        }

        var alert = Assert.Single(await AlertsAsync(s.Admin, Warehouse), a => a.ProductId == product.Id);
        var row = (await s.Admin.Stock.QueryAsync(product.Id, Warehouse)).Data<PagedResult<StockRow>>().Items.Single();
        var lowOnly = (await s.Admin.Stock.QueryAsync(product.Id, Warehouse, lowOnly: true)).Data<PagedResult<StockRow>>().Items;

        Assert.Equal((-40m, true, 40m), (alert.QtyOnHand, alert.IsNegative, alert.Shortfall));
        Assert.True(row.IsNegative && row.IsLow);
        Assert.Single(lowOnly);
    }

    [Fact]
    public async Task A_stock_row_above_zero_is_not_negative()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(safetyStock: 10);
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 1m));

        var row = (await s.Admin.Stock.QueryAsync(product.Id, Warehouse)).Data<PagedResult<StockRow>>().Items.Single();

        Assert.True(row.IsLow);
        Assert.False(row.IsNegative);
    }

    [Fact]
    public async Task The_home_count_the_list_total_and_the_export_agree()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var never = await s.NewProductAsync(safetyStock: 5);
        var short_ = await s.NewProductAsync(safetyStock: 10);
        await s.ReceiveAsync(Warehouse, new Line(short_.Id, 2, 1m));

        var summary = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>();
        var page = (await s.Admin.Stock.LowStockAsync()).Data<PagedResult<LowStockAlert>>();
        var export = Xlsx.Read((await s.Admin.Reports.LowStockExportAsync()).Bytes);

        Assert.Equal(page.Total, summary.LowCount);
        Assert.Equal(page.Total, export.Rows.Count);
        Assert.NotNull(export.FindRow("SKU", never.Sku));
        Assert.NotNull(export.FindRow("SKU", short_.Sku));
    }

    [Fact]
    public async Task Paging_walks_the_list_without_repeating_or_skipping_a_row()
    {
        var s = await StockScenario.CreateAsync(fixture);
        await s.NewProductAsync(safetyStock: 5);
        await s.NewProductAsync(safetyStock: 5);
        await s.NewProductAsync(safetyStock: 5);

        // The first 30 rows in one page, and the same 30 walked in pages of 5.
        var firstThirty = (await AlertsAsync(s.Admin)).Take(30).ToList();
        var walked = new List<LowStockAlert>();
        for (var page = 1; walked.Count < 30; page++)
        {
            var result = (await s.Admin.GetAsync($"/api/stock/low-stock?page={page}&pageSize=5")).Data<PagedResult<LowStockAlert>>();
            if (result.Items.Count == 0) break;
            walked.AddRange(result.Items);
        }

        Assert.Equal(firstThirty.Select(a => (a.ProductId, a.WarehouseId)), walked.Take(firstThirty.Count).Select(a => (a.ProductId, a.WarehouseId)));
        Assert.Equal(walked.Count, walked.Select(a => (a.ProductId, a.WarehouseId)).Distinct().Count()); // no row twice
    }

    [Fact]
    public async Task Products_with_stock_counts_only_positive_balances()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var before = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>();
        var product = await s.NewProductAsync();

        await s.ReceiveAsync(Warehouse, new Line(product.Id, 4, 1m));
        var stocked = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>();
        await s.IssueAsync(Warehouse, new Line(product.Id, 4)); // back to zero: a row exists, but no stock
        var emptied = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>();

        Assert.Equal(before.SkuCount + 1, stocked.SkuCount);
        Assert.Equal(before.SkuCount, emptied.SkuCount);
    }
}
