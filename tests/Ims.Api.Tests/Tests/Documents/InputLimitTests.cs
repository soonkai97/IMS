using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>Out-of-range input is a 400 with a message, never a 500 from the database.</summary>
[Collection(ApiCollection.Name)]
public class InputLimitTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    [Theory]
    [InlineData("1000000001")] // above the 1e9 maximum
    [InlineData("1000000000000000")]
    [InlineData("0.00001")]    // more than four decimals
    [InlineData("1.23456")]
    public async Task Quantities_above_one_billion_or_with_more_than_four_decimals_are_400(string quantity)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var response = await s.Admin.PostAsync("/api/documents/receipts", RawRequest(product.Id, quantity));

        response.ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("1000000000")]
    [InlineData("0.0001")]
    [InlineData("12.3456")]
    public async Task Quantities_at_the_limits_are_accepted(string quantity)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        (await s.Admin.PostAsync("/api/documents/receipts", RawRequest(product.Id, quantity))).ShouldBeOk();
    }

    [Fact]
    public async Task More_than_five_hundred_lines_in_one_document_is_400()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var lines = Enumerable.Range(0, 501).Select(_ => new Line(product.Id, 1, 1m)).ToArray();

        (await s.Admin.Documents.CreateReceiptAsync(new DocumentBuilder(Warehouse).WithLines(lines).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Admin.Documents.CreateReceiptAsync(new DocumentBuilder(Warehouse).WithLines(lines.Take(500).ToArray()).Build())).ShouldBeOk();
    }

    [Fact]
    public async Task An_issue_purpose_outside_the_enum_is_400()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var body = new { warehouseId = Warehouse, purpose = 99, lines = new[] { new { productId = product.Id, qty = 1 } } };

        (await s.Admin.PostAsync("/api/documents/issues", body)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_null_barcode_in_the_list_is_400_not_500()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var body = new { sku = Ims.Api.Tests.Support.Unique.Sku(), name = "Null barcode", baseUomId = fixture.Reference.PieceUnitId, safetyStock = 0, isActive = true, barcodes = new string?[] { null } };

        var response = await s.Admin.PostAsync("/api/products", body);

        Assert.True(response.StatusCode is >= 400 and < 500, $"Got {response.StatusCode}: {response.Body}");
    }

    [Fact]
    public async Task Conversions_with_a_duplicate_source_unit_or_the_same_unit_on_both_sides_are_400()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var pcs = fixture.Reference.PieceUnitId;
        var ctn = fixture.Reference.CartonUnitId;
        var duplicate = new ProductBuilder(fixture.Reference).Build() with { Conversions = [new UomConversionDto(ctn, pcs, 12), new UomConversionDto(ctn, pcs, 24)] };
        var sameUnit = new ProductBuilder(fixture.Reference).Build() with { Conversions = [new UomConversionDto(pcs, pcs, 1)] };

        (await s.Admin.Products.CreateAsync(duplicate)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Admin.Products.CreateAsync(sameUnit)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Validation_error_keys_are_camel_case_paths_without_a_command_wrapper()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m));
        (await s.Admin.Documents.SubmitAsync(draft.Id)).ShouldBeOk();

        var reject = await s.Owner.Documents.RejectAsync(draft.Id, "");
        var badLine = await s.Admin.Documents.CreateReceiptAsync(new DocumentBuilder(Warehouse).WithLines(new Line(product.Id, 0, 1m)).Build());
        var badProduct = await s.Admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithSku("").Build());

        Assert.Contains("reason", reject.ShouldBeProblem(HttpStatusCode.BadRequest).Errors!.Keys);
        Assert.Contains("lines[0].qty", badLine.ShouldBeProblem(HttpStatusCode.BadRequest).Errors!.Keys);
        Assert.Contains("sku", badProduct.ShouldBeProblem(HttpStatusCode.BadRequest).Errors!.Keys);
    }

    /// <summary>Quantity is passed as text so the test controls every digit.</summary>
    private object RawRequest(Guid productId, string quantity) => new
    {
        warehouseId = Warehouse,
        lines = new[] { new { productId, qty = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), unitCost = 1 } }
    };
}
