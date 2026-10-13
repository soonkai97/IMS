using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Stock;

namespace Ims.Api.Tests.Tests.StockList;

/// <summary>The stock list search matches a SKU or name by text and a barcode by exact value (the barcode is resolved before the list query).</summary>
[Collection(ApiCollection.Name)]
public class StockSearchTests(ApiFixture fixture)
{
    private static async Task<IReadOnlyList<StockRow>> SearchAsync(StockScenario s, string term, Guid? warehouseId = null) =>
        (await s.Admin.Stock.QueryAsync(warehouseId: warehouseId, search: term)).Data<PagedResult<StockRow>>().Items;

    [Fact]
    public async Task Search_finds_stock_by_exact_barcode_by_sku_or_by_name()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var barcode = Unique.Barcode();
        var wanted = await s.NewProductAsync(customize: b => b.WithBarcodes(barcode));
        var other = await s.NewProductAsync(customize: b => b.WithBarcodes(Unique.Barcode()));
        await s.ReceiveAsync(warehouse.Id, new Line(wanted.Id, 5, 1m), new Line(other.Id, 7, 1m));

        foreach (var term in new[] { barcode, wanted.Sku, wanted.Name })
        {
            var row = Assert.Single(await SearchAsync(s, term, warehouse.Id));
            Assert.Equal((wanted.Id, 5m), (row.ProductId, row.QtyOnHand));
        }
    }

    [Fact]
    public async Task A_barcode_search_returns_the_stock_of_that_product_in_every_warehouse()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var first = await s.NewWarehouseAsync();
        var second = await s.NewWarehouseAsync();
        var barcode = Unique.Barcode();
        var product = await s.NewProductAsync(customize: b => b.WithBarcodes(barcode));
        await s.ReceiveAsync(first.Id, new Line(product.Id, 2, 1m));
        await s.ReceiveAsync(second.Id, new Line(product.Id, 3, 1m));

        var rows = await SearchAsync(s, barcode);

        Assert.Equal(new[] { first.Id, second.Id }.Order(), rows.Select(r => r.WarehouseId!.Value).Order());
        Assert.All(rows, r => Assert.Equal(product.Id, r.ProductId));
    }

    [Fact]
    public async Task A_part_of_a_barcode_or_an_unknown_barcode_finds_nothing()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var barcode = Unique.Barcode();
        var product = await s.NewProductAsync(customize: b => b.WithBarcodes(barcode));
        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 1, 1m));

        Assert.Empty(await SearchAsync(s, barcode[..8], warehouse.Id));
        Assert.Empty(await SearchAsync(s, Unique.Barcode(), warehouse.Id));
    }

    [Fact]
    public async Task A_barcode_that_was_removed_from_the_product_no_longer_matches()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var barcode = Unique.Barcode();
        var product = await s.NewProductAsync(customize: b => b.WithBarcodes(barcode));
        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 1, 1m));
        Assert.Single(await SearchAsync(s, barcode, warehouse.Id));

        (await s.Admin.Products.UpdateAsync(product.Id, ProductBuilder.From(fixture.Reference, product).WithBarcodes().Build())).ShouldBeOk();

        Assert.Empty(await SearchAsync(s, barcode, warehouse.Id));
        Assert.Single(await SearchAsync(s, product.Sku, warehouse.Id));
    }
}
