using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Stock;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.StockList;

/// <summary>
/// A product with a safety level and no balance row in ANY warehouse the caller can see is "never stocked": it is listed ONCE, with no warehouse and nothing on hand.
/// A product that has a balance row in a visible warehouse follows the normal rule per warehouse. Only the caller's warehouses count, so a scoped caller learns
/// nothing about stock elsewhere.
/// </summary>
[Collection(ApiCollection.Name)]
public class LowStockNeverStockedTests(ApiFixture fixture)
{
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
    public async Task A_product_stocked_nowhere_is_listed_once_with_no_warehouse_however_many_warehouses_there_are()
    {
        var s = await StockScenario.CreateAsync(fixture);
        await s.NewWarehouseAsync();
        await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 10);

        var row = Assert.Single(await AlertsAsync(s.Admin), r => r.ProductId == product.Id);

        Assert.Null(row.WarehouseId);
        Assert.Null(row.WarehouseCode);
        Assert.Null(row.WarehouseName);
        Assert.Equal((0m, 10m, 10m, false), (row.QtyOnHand, row.SafetyStock, row.Shortfall, row.IsNegative));
    }

    [Fact]
    public async Task A_product_with_a_balance_in_a_visible_warehouse_follows_the_normal_rule_there_and_is_not_listed_for_the_others()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewWarehouseAsync();
        var b = await s.NewWarehouseAsync();
        var fine = await s.NewProductAsync(safetyStock: 10);
        var low = await s.NewProductAsync(safetyStock: 10);
        await s.ReceiveAsync(a.Id, new Line(fine.Id, 11, 2m), new Line(low.Id, 10, 2m)); // above the level; exactly at it

        var all = await AlertsAsync(s.Admin);

        Assert.DoesNotContain(all, r => r.ProductId == fine.Id);                              // stocked above its level in A, nothing is said about B
        Assert.Contains(all, r => r.ProductId == low.Id && r.WarehouseId == a.Id);            // at its level in A: low in A
        Assert.DoesNotContain(all, r => r.ProductId == low.Id && r.WarehouseId != a.Id);      // and not once more for B or as "stocked nowhere"
        Assert.DoesNotContain(await AlertsAsync(s.Admin, b.Id), r => r.ProductId == fine.Id || r.ProductId == low.Id);
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
        var countBefore = (await inB.Stock.SummaryAsync()).Data<StockSummary>().LowCount;
        await s.ReceiveAsync(a.Id, new Line(product.Id, 500, 2m)); // plenty of stock in the warehouse the caller cannot see
        var after = await AlertsAsync(inB);
        var countAfter = (await inB.Stock.SummaryAsync()).Data<StockSummary>().LowCount;

        Assert.Contains(after, r => r.ProductId == product.Id && r.WarehouseId is null && r.QtyOnHand == 0m); // still "stocked nowhere" for them
        Assert.Equal(before.Select(r => (r.ProductId, r.WarehouseId, r.QtyOnHand)), after.Select(r => (r.ProductId, r.WarehouseId, r.QtyOnHand)));
        Assert.Equal(countBefore, countAfter);                      // the Home count cannot tell either
        Assert.Equal(after.Count, countAfter);
        Assert.All(after, r => Assert.True(r.WarehouseId is null || r.WarehouseId == b.Id));
    }

    [Fact]
    public async Task An_old_zero_row_in_an_inactive_warehouse_does_not_make_a_product_stocked()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var old = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 10);
        await s.ReceiveAsync(old.Id, new Line(product.Id, 5, 2m));
        await s.IssueAsync(old.Id, new Line(product.Id, 5)); // a zero balance row remains in the old warehouse
        (await s.Admin.Warehouses.UpdateAsync(old.Id, new WarehouseSaveRequest(old.Code, old.Name, null, null, false))).ShouldBeOk();

        var all = await AlertsAsync(s.Admin);

        var row = Assert.Single(all, r => r.ProductId == product.Id);
        Assert.Null(row.WarehouseId); // stocked nowhere that is open, and a closed warehouse raises no alert of its own
        Assert.Equal(all.Count, (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>().LowCount);
    }
}
