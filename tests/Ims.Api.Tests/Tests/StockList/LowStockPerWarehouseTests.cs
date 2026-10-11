using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Stock;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.StockList;

/// <summary>
/// "Never stocked" is decided per warehouse: an active product with a safety level and no balance row in warehouse W is low in W, for everyone who can see W.
/// Stock in another warehouse says nothing about W, and a caller who cannot see a warehouse learns nothing about it from the answer.
/// </summary>
[Collection(ApiCollection.Name)]
public class LowStockPerWarehouseTests(ApiFixture fixture)
{
    private static async Task<List<LowStockAlert>> AlertsAsync(ApiClient who, int? warehouseId = null)
    {
        var rows = new List<LowStockAlert>();
        for (var page = 1; ; page++)
        {
            var result = (await who.GetAsync($"/api/stock/low-stock?page={page}&pageSize=500" + (warehouseId is null ? "" : $"&warehouseId={warehouseId}"))).Data<PagedResult<LowStockAlert>>();
            rows.AddRange(result.Items);
            if (rows.Count >= result.Total || result.Items.Count == 0) return rows;
        }
    }

    private static async Task<List<StockRow>> LowRowsAsync(ApiClient who, int warehouseId)
    {
        var rows = new List<StockRow>();
        for (var page = 1; ; page++)
        {
            var result = (await who.GetAsync($"/api/stock?lowOnly=true&warehouseId={warehouseId}&page={page}&pageSize=500")).Data<PagedResult<StockRow>>();
            rows.AddRange(result.Items);
            if (rows.Count >= result.Total || result.Items.Count == 0) return rows;
        }
    }

    [Fact]
    public async Task A_product_stocked_in_one_warehouse_is_still_low_in_a_warehouse_that_never_had_it()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewWarehouseAsync();
        var b = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 10);
        await s.ReceiveAsync(a.Id, new Line(product.Id, 50, 2m));

        var inB = await AlertsAsync(s.Admin, b.Id);
        var inA = await AlertsAsync(s.Admin, a.Id);

        var row = Assert.Single(inB, r => r.ProductId == product.Id);
        Assert.Equal((0m, 10m), (row.QtyOnHand, row.SafetyStock));
        Assert.DoesNotContain(inA, r => r.ProductId == product.Id); // 50 on hand, safety level 10: fine where it is stocked
        Assert.Contains((await LowRowsAsync(s.Admin, b.Id)), r => r.ProductId == product.Id && r.QtyOnHand == 0m); // the stock list says the same
        Assert.DoesNotContain((await LowRowsAsync(s.Admin, a.Id)), r => r.ProductId == product.Id);
    }

    [Fact]
    public async Task A_warehouse_that_received_the_product_takes_it_off_that_warehouses_list_only()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewWarehouseAsync();
        var b = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 10);

        Assert.Contains(await AlertsAsync(s.Admin, a.Id), r => r.ProductId == product.Id);
        Assert.Contains(await AlertsAsync(s.Admin, b.Id), r => r.ProductId == product.Id);
        await s.ReceiveAsync(b.Id, new Line(product.Id, 11, 2m)); // above the level of 10

        Assert.Contains(await AlertsAsync(s.Admin, a.Id), r => r.ProductId == product.Id); // A never had it
        Assert.DoesNotContain(await AlertsAsync(s.Admin, b.Id), r => r.ProductId == product.Id);
    }

    [Fact]
    public async Task A_caller_scoped_to_B_gets_the_same_answer_whether_or_not_A_holds_the_product()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewWarehouseAsync();
        var b = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 10);
        var accounts = new AccountFactory(fixture);
        var (inB, _) = await accounts.SignInAsync(await accounts.CreateAsync(Roles.WarehouseStaff, warehouseIds: [b.Id]));

        var before = await AlertsAsync(inB);
        var summaryBefore = (await inB.Stock.SummaryAsync()).Data<StockSummary>();
        await s.ReceiveAsync(a.Id, new Line(product.Id, 500, 2m)); // plenty of stock in the warehouse the caller cannot see
        var after = await AlertsAsync(inB);
        var summaryAfter = (await inB.Stock.SummaryAsync()).Data<StockSummary>();

        Assert.All(after, r => Assert.Equal(b.Id, r.WarehouseId));
        Assert.Contains(after, r => r.ProductId == product.Id && r.QtyOnHand == 0m); // low in B, as before
        Assert.Equal(before.Select(r => (r.ProductId, r.WarehouseId, r.QtyOnHand)), after.Select(r => (r.ProductId, r.WarehouseId, r.QtyOnHand)));
        Assert.Equal(summaryBefore.LowCount, summaryAfter.LowCount); // the Home count cannot tell either
        Assert.Equal(after.Count, summaryAfter.LowCount);
        Assert.DoesNotContain(await LowRowsAsync(inB, a.Id), r => true); // asking for A by id shows nothing of A
    }

    [Fact]
    public async Task The_home_count_of_a_scoped_caller_counts_their_warehouses_only()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var b = await s.NewWarehouseAsync();
        await s.NewProductAsync(safetyStock: 10);
        var accounts = new AccountFactory(fixture);
        var (inB, _) = await accounts.SignInAsync(await accounts.CreateAsync(Roles.Clerk, warehouseIds: [b.Id]));

        var scoped = (await inB.Stock.SummaryAsync()).Data<StockSummary>();
        var everything = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>();

        Assert.Equal((await AlertsAsync(inB)).Count, scoped.LowCount);
        Assert.True(everything.LowCount > scoped.LowCount); // the other warehouses add their own rows for the same products
    }

    [Fact]
    public async Task An_inactive_warehouse_is_not_listed_and_an_old_zero_row_there_hides_nothing_elsewhere()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var old = await s.NewWarehouseAsync();
        var live = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 10);
        await s.ReceiveAsync(old.Id, new Line(product.Id, 5, 2m));
        await s.IssueAsync(old.Id, new Line(product.Id, 5)); // a zero balance row remains in the old warehouse
        var request = new WarehouseSaveRequest(old.Code, old.Name, null, null, false);
        (await s.Admin.Warehouses.UpdateAsync(old.Id, request)).ShouldBeOk();

        var all = await AlertsAsync(s.Admin);

        Assert.Contains(all, r => r.ProductId == product.Id && r.WarehouseId == live.Id); // never stocked in the live warehouse
        Assert.DoesNotContain(all, r => r.ProductId == product.Id && r.WarehouseId == old.Id); // a closed warehouse raises no alerts, with or without an old row
        Assert.DoesNotContain(await LowRowsAsync(s.Admin, old.Id), r => r.ProductId == product.Id);
        Assert.Equal(all.Count, (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>().LowCount);
    }
}
