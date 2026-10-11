using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Import;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>The usable unit-cost range is explicit: above it the answer is a 400 with a sentence, never a database overflow (a 500).</summary>
[Collection(ApiCollection.Name)]
public class UnitCostLimitTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    [Fact]
    public async Task A_unit_cost_above_the_ceiling_is_a_400_with_a_sentence_and_the_ceiling_itself_is_fine()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var request = (decimal cost) => new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(product.Id, 1, cost)).Build();

        var tooHigh = await s.Admin.Documents.CreateReceiptAsync(request(Limits.MaxUnitCost + 0.01m));
        var absurd = await s.Admin.Documents.CreateReceiptAsync(request(99_999_999_999_999m));
        var atCeiling = await s.Admin.Documents.CreateReceiptAsync(request(Limits.MaxUnitCost));

        Assert.Equal([Messages.CostTooLarge], tooHigh.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("UnitCost"));
        absurd.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(Limits.MaxUnitCost, atCeiling.Data<DocumentDto>().TotalAmount);
    }

    [Fact]
    public async Task A_cost_that_is_too_large_once_spread_over_a_small_unit_is_a_400_not_an_overflow()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 0.000001m)); // 1 carton = 0.000001 pieces
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId)
            .WithLines(new Line(product.Id, 1, 900_000_000m, fixture.Reference.CartonUnitId)).Build();

        var created = await s.Admin.Documents.CreateReceiptAsync(request);

        Assert.Equal(Messages.CostTooLargeForUnit, created.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
    }

    [Fact]
    public async Task The_opening_stock_import_reports_a_cost_above_the_ceiling_as_a_row_error()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var file = Xlsx.Create("Opening stock", ["SKU", "Warehouse", "Qty", "UnitCost"],
            [product.Sku, fixture.Reference.DefaultWarehouseCode, 1, 2.5],
            [product.Sku, fixture.Reference.DefaultWarehouseCode, 1, 1_000_000_001]);

        var preview = (await s.Admin.Import.OpeningStockAsync(file, commit: false)).Data<ImportResult>();

        Assert.Equal([true, false], preview.Rows.Select(r => r.Ok));
        Assert.Equal(Messages.CostTooLarge, Assert.Single(preview.Rows[1].Errors));
    }
}
