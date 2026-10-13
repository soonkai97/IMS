using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Stock;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.StockList;

/// <summary>
/// PR-04: "low stock" is one definition (<see cref="LowStockRule"/>). The stock list with <c>lowOnly=true</c>, the low-stock list and its export, the Home count and the
/// <c>isLow</c> flag of a stock row all say the same thing, never-stocked products included. PR-10: the low-stock list is worst first by share of the safety level.
/// </summary>
[Collection(ApiCollection.Name)]
public class LowStockOneDefinitionTests(ApiFixture fixture)
{
    private static async Task<List<StockRow>> StockRowsAsync(ApiClient who, string query)
    {
        var rows = new List<StockRow>();
        for (var page = 1; ; page++)
        {
            var result = (await who.GetAsync($"/api/stock?{query}&page={page}&pageSize=500")).Data<PagedResult<StockRow>>();
            rows.AddRange(result.Items);
            if (rows.Count >= result.Total || result.Items.Count == 0) return rows;
        }
    }

    private static async Task<List<LowStockAlert>> AlertsAsync(ApiClient who, Guid? warehouseId)
    {
        var rows = new List<LowStockAlert>();
        for (var page = 1; ; page++)
        {
            var result = (await who.GetAsync($"/api/stock/low-stock?page={page}&pageSize=500" + (warehouseId is null ? "" : $"&warehouseId={warehouseId}"))).Data<PagedResult<LowStockAlert>>();
            rows.AddRange(result.Items);
            if (rows.Count >= result.Total || result.Items.Count == 0) return rows;
        }
    }

    private static HashSet<(Guid Product, Guid? Warehouse)> Keys(IEnumerable<StockRow> rows) => rows.Select(r => (r.ProductId, r.WarehouseId)).ToHashSet();
    private static HashSet<(Guid Product, Guid? Warehouse)> Keys(IEnumerable<LowStockAlert> rows) => rows.Select(r => (r.ProductId, r.WarehouseId)).ToHashSet();

    /// <summary>
    /// Issues more than is on hand: negative stock on, an Admin who may approve their own document (nobody else sees a brand-new warehouse), the override reason an
    /// approver must give.
    /// </summary>
    private static async Task GoBelowZeroAsync(StockScenario s, Guid warehouseId, Line line)
    {
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true, allowSelfApproval: true);
        var submitted = await s.SubmitAsync(s.Admin, await s.DraftIssueAsync(s.Admin, warehouseId, line));
        (await s.Admin.Documents.ApproveWithReasonAsync(submitted.Id, "Counted by hand, delivery is late")).ShouldBeOk();
        (await s.Admin.Documents.PostAsync(submitted.Id)).ShouldBeOk();
    }

    /// <summary>A fresh warehouse with one product of every kind: low, stocked nowhere (never stocked), exactly at the safety level, inactive, no safety level, fine, negative.</summary>
    private sealed record Kinds(WarehouseDto Warehouse, Guid Low, Guid Never, Guid AtLevel, Guid Inactive, Guid NoSafety, Guid Fine, Guid Negative);

    private async Task<(StockScenario S, Kinds K)> SetUpAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var low = await s.NewProductAsync(safetyStock: 10);
        var never = await s.NewProductAsync(safetyStock: 10);
        var atLevel = await s.NewProductAsync(safetyStock: 10);
        var inactive = await s.NewProductAsync(safetyStock: 10);
        var noSafety = await s.NewProductAsync(safetyStock: 0);
        var fine = await s.NewProductAsync(safetyStock: 10);
        var negative = await s.NewProductAsync(safetyStock: 10);
        await s.ReceiveAsync(warehouse.Id, new Line(low.Id, 4, 2m), new Line(atLevel.Id, 10, 2m), new Line(inactive.Id, 1, 2m), new Line(noSafety.Id, 0.5m, 2m),
            new Line(fine.Id, 25, 2m), new Line(negative.Id, 5, 2m));
        (await s.Admin.Products.UpdateAsync(inactive.Id, ProductBuilder.From(fixture.Reference, inactive).Inactive().Build())).ShouldBeOk();
        await GoBelowZeroAsync(s, warehouse.Id, new Line(negative.Id, 8));
        return (s, new Kinds(warehouse, low.Id, never.Id, atLevel.Id, inactive.Id, noSafety.Id, fine.Id, negative.Id));
    }

    [Fact]
    public async Task The_stock_list_with_low_only_has_exactly_the_rows_of_the_low_stock_list()
    {
        var (s, k) = await SetUpAsync();

        var stockLow = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={k.Warehouse.Id}");
        var alerts = await AlertsAsync(s.Admin, k.Warehouse.Id);

        Assert.Equal(Keys(alerts), Keys(stockLow));
        Assert.Equal(alerts.Count, stockLow.Count);
        Assert.Contains((k.Low, k.Warehouse.Id), Keys(stockLow));
        Assert.Contains((k.Negative, k.Warehouse.Id), Keys(stockLow));
        Assert.Contains((k.AtLevel, k.Warehouse.Id), Keys(stockLow)); // exactly at the safety level is low (design 3.8: on-hand <= safety stock)
        foreach (var notLow in new[] { k.Inactive, k.NoSafety, k.Fine, k.Never })
            Assert.DoesNotContain((notLow, (Guid?)k.Warehouse.Id), Keys(stockLow)); // a product stocked nowhere belongs to no warehouse: a warehouse filter leaves it out

        var everywhere = await StockRowsAsync(s.Admin, $"lowOnly=true&productId={k.Never}");
        Assert.Equal((k.Never, (Guid?)null), Assert.Single(Keys(everywhere))); // listed ONCE, with no warehouse
        Assert.Contains((k.Never, (Guid?)null), Keys(await AlertsAsync(s.Admin, null)));
    }

    [Fact]
    public async Task The_totals_agree_everywhere_the_stock_list_the_low_stock_list_the_home_count_and_the_export()
    {
        var (s, k) = await SetUpAsync();

        var stockLow = (await s.Admin.GetAsync("/api/stock?lowOnly=true&pageSize=1")).Data<PagedResult<StockRow>>();
        var lowStock = (await s.Admin.GetAsync("/api/stock/low-stock?pageSize=1")).Data<PagedResult<LowStockAlert>>();
        var summary = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>();
        var lowExport = Xlsx.Read((await s.Admin.Reports.LowStockExportAsync()).Bytes);
        var stockExport = Xlsx.Read((await s.Admin.GetAsync("/api/reports/stock-summary/export?lowOnly=true")).Bytes);

        Assert.Equal(lowStock.Total, stockLow.Total);
        Assert.Equal(lowStock.Total, summary.LowCount);
        Assert.Equal(lowStock.Total, lowExport.Rows.Count);
        Assert.Equal(lowStock.Total, stockExport.Rows.Count); // the stock export with low only is the same set too
        Assert.NotNull(stockExport.FindRow("SKU", (await s.Admin.Products.GetAsync(k.Never)).Data<Ims.Application.Features.Catalog.ProductDto>().Sku));
    }

    [Fact]
    public async Task A_never_stocked_product_is_a_row_with_nothing_on_hand_and_no_cost()
    {
        var (s, k) = await SetUpAsync();

        var row = (await StockRowsAsync(s.Admin, $"lowOnly=true&productId={k.Never}")).Single();

        Assert.Equal((0m, 0m, 10m), (row.QtyOnHand, row.QtyInTransit, row.SafetyStock));
        Assert.Null(row.AvgCost); // nothing was ever bought here: there is no average, which is not an average of zero
        Assert.Null(row.Value);
        Assert.True(row.IsLow);
        Assert.False(row.IsNegative);
        Assert.Null(row.WarehouseId); // stocked nowhere: no warehouse, and one row, not one per warehouse
        Assert.Null(row.WarehouseCode);
        Assert.Null(row.WarehouseName);
        Assert.Equal("PCS", row.UomCode);
    }

    [Fact]
    public async Task The_other_filters_and_the_sorts_apply_to_both_kinds_of_row()
    {
        var (s, k) = await SetUpAsync();
        var never = (await s.Admin.Products.GetAsync(k.Never)).Data<Ims.Application.Features.Catalog.ProductDto>();

        var bySearch = await StockRowsAsync(s.Admin, $"lowOnly=true&search={Uri.EscapeDataString(never.Sku)}");
        var byProduct = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={k.Warehouse.Id}&productId={k.Low}");
        var byCategory = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={k.Warehouse.Id}&categoryId={fixture.Reference.DefaultCategoryId}");
        var otherWarehouse = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={fixture.Reference.DefaultWarehouseId}&productId={k.Low}");
        var all = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={k.Warehouse.Id}");

        Assert.Equal(never.Id, Assert.Single(bySearch).ProductId);
        Assert.Equal(k.Low, Assert.Single(byProduct).ProductId);
        var categoryName = all.Single(r => r.ProductId == k.Low).CategoryName;
        Assert.Contains((k.Low, (Guid?)k.Warehouse.Id), Keys(byCategory));
        Assert.All(byCategory, r => Assert.Equal(categoryName, r.CategoryName));
        Assert.Equal(Keys(all).Where(key => all.Single(r => (r.ProductId, r.WarehouseId) == key).CategoryName == categoryName).ToHashSet(), Keys(byCategory));
        Assert.Empty(otherWarehouse); // the product is stocked (low) in the new warehouse, so it is not "never stocked" anywhere and has no row in the default one

        foreach (var (sort, desc) in new[] { ("Sku", false), ("Sku", true), ("Name", false), ("Name", true), ("QtyOnHand", false), ("QtyOnHand", true), ("Value", false), ("Warehouse", false) })
        {
            var sorted = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={k.Warehouse.Id}&sortBy={sort}&desc={desc.ToString().ToLowerInvariant()}");
            Assert.Equal(Keys(all), Keys(sorted));
            Assert.Equal(sorted.Count, sorted.Select(r => (r.ProductId, r.WarehouseId)).Distinct().Count());
        }
        // The sort is the database's, whatever its collation: do not predict it in .NET. Ascending and descending must be mirrors of each other
        // (product names may repeat), and the SKU tie-break makes the order total.
        var byName = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={k.Warehouse.Id}&sortBy=Name");
        var byNameDesc = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={k.Warehouse.Id}&sortBy=Name&desc=true");
        Assert.Equal(byName.Select(r => r.ProductName).Reverse(), byNameDesc.Select(r => r.ProductName)); // products may share a name; the SKU then breaks the tie the same way in both
        Assert.Equal(Keys(byName), Keys(byNameDesc));
        var byQty = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={k.Warehouse.Id}&sortBy=QtyOnHand");
        Assert.Equal(byQty.Select(r => r.QtyOnHand).Order(), byQty.Select(r => r.QtyOnHand)); // the negative row first, the rest at or below their level
    }

    [Fact]
    public async Task Paging_the_low_only_list_never_repeats_or_skips_a_row()
    {
        var (s, k) = await SetUpAsync();
        var whole = await StockRowsAsync(s.Admin, $"lowOnly=true&warehouseId={k.Warehouse.Id}&sortBy=Name");

        var walked = new List<StockRow>();
        for (var page = 1; walked.Count < whole.Count; page++)
        {
            var result = (await s.Admin.GetAsync($"/api/stock?lowOnly=true&warehouseId={k.Warehouse.Id}&sortBy=Name&page={page}&pageSize=3")).Data<PagedResult<StockRow>>();
            Assert.Equal(whole.Count, result.Total);
            walked.AddRange(result.Items);
        }

        Assert.Equal(whole.Select(r => (r.ProductId, r.WarehouseId)), walked.Select(r => (r.ProductId, r.WarehouseId)));
    }

    [Fact]
    public async Task The_isLow_flag_of_a_stock_row_is_the_same_definition()
    {
        var (s, k) = await SetUpAsync();

        var rows = await StockRowsAsync(s.Admin, $"warehouseId={k.Warehouse.Id}");
        var alerts = Keys(await AlertsAsync(s.Admin, k.Warehouse.Id));

        Assert.All(rows, r => Assert.Equal(alerts.Contains((r.ProductId, r.WarehouseId)), r.IsLow));
        Assert.True(rows.Single(r => r.ProductId == k.Low).IsLow);
        Assert.True(rows.Single(r => r.ProductId == k.Negative).IsLow);
        Assert.True(rows.Single(r => r.ProductId == k.AtLevel).IsLow);   // at the safety level is low: a receipt must bring it ABOVE it
        Assert.False(rows.Single(r => r.ProductId == k.Inactive).IsLow); // an inactive product is never on the low-stock list
        Assert.False(rows.Single(r => r.ProductId == k.NoSafety).IsLow);
        Assert.False(rows.Single(r => r.ProductId == k.Fine).IsLow);
    }

    [Fact]
    public async Task A_user_sees_the_low_only_list_of_their_own_warehouses_only()
    {
        var (s, k) = await SetUpAsync();

        var theirs = await StockRowsAsync(s.Staff, "lowOnly=true");
        var other = await StockRowsAsync(s.Staff, $"lowOnly=true&warehouseId={k.Warehouse.Id}");

        Assert.All(theirs, r => Assert.NotEqual(k.Warehouse.Id, r.WarehouseId));
        Assert.Empty(other);
        Assert.Equal(Keys(await AlertsAsync(s.Staff, null)), Keys(theirs));
    }

    // ----- PR-10: worst first, by share of the safety level -----
    [Fact]
    public async Task The_low_stock_list_is_worst_first_by_the_share_missing_not_by_the_number_of_units()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var half = await s.NewProductAsync(safetyStock: 100);      // 50 of 100: 50 units short, half of the level
        var most = await s.NewProductAsync(safetyStock: 10);       // 4 of 10: 6 units short, 60 % of the level
        var never = await s.NewProductAsync(safetyStock: 10);      // none of 10: 100 %
        var negative = await s.NewProductAsync(safetyStock: 10);   // -3 of 10: 130 %
        await s.ReceiveAsync(warehouse.Id, new Line(half.Id, 50, 1m), new Line(most.Id, 4, 1m), new Line(negative.Id, 2, 1m));
        await GoBelowZeroAsync(s, warehouse.Id, new Line(negative.Id, 5));

        var order = (await AlertsAsync(s.Admin, null)).Select(a => a.ProductId).Where(id => new[] { half.Id, most.Id, never.Id, negative.Id }.Contains(id)).ToList();

        Assert.Equal([negative.Id, never.Id, most.Id, half.Id], order);
    }
}
