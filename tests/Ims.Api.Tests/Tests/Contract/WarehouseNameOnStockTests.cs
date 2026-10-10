using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Stock;

namespace Ims.Api.Tests.Tests.Contract;

/// <summary>A stock row, a low-stock alert and a ledger row name their warehouse as well as coding it, so no screen needs a second lookup.</summary>
[Collection(ApiCollection.Name)]
public class WarehouseNameOnStockTests(ApiFixture fixture)
{
    [Fact]
    public async Task Stock_rows_carry_the_warehouse_name_next_to_its_code()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 4, 1m));

        var row = await s.BalanceAsync(product.Id, warehouse.Id);

        Assert.NotNull(row);
        Assert.Equal((warehouse.Code, warehouse.Name), (row.WarehouseCode, row.WarehouseName));
    }

    [Fact]
    public async Task Low_stock_alerts_carry_the_warehouse_name()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 100);
        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 4, 1m));

        var alerts = (await s.Admin.Stock.LowStockAsync()).Data<PagedResult<LowStockAlert>>();

        var alert = Assert.Single(alerts.Items, a => a.ProductId == product.Id && a.WarehouseId == warehouse.Id);
        Assert.Equal((warehouse.Code, warehouse.Name), (alert.WarehouseCode, alert.WarehouseName));
    }

    [Fact]
    public async Task Ledger_rows_carry_the_warehouse_name()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 4, 1m));

        var entry = Assert.Single(await s.LedgerAsync(product.Id, warehouse.Id));

        Assert.Equal((warehouse.Code, warehouse.Name), (entry.WarehouseCode, entry.WarehouseName));
    }

    [Fact]
    public async Task The_name_is_on_the_wire_too()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 4, 1m));

        using var json = (await s.Admin.Stock.QueryAsync(product.Id, warehouse.Id)).Json();

        Assert.Equal(warehouse.Name, json.RootElement.GetProperty("items")[0].GetProperty("warehouseName").GetString());
    }
}
