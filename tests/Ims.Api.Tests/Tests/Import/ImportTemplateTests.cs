using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;

namespace Ims.Api.Tests.Tests.Import;

[Collection(ApiCollection.Name)]
public class ImportTemplateTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("products", "products-template.xlsx", "Products", new[] { "SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes" })]
    [InlineData("opening-stock", "opening-stock-template.xlsx", "Opening stock", new[] { "SKU", "Warehouse", "Qty", "UnitCost" })]
    public async Task The_template_downloads_as_an_xlsx_with_the_expected_header_row_and_a_sample(string kind, string fileName, string sheet, string[] headers)
    {
        var admin = await fixture.AsAsync("Admin");

        var response = await admin.Import.TemplateAsync(kind);

        response.ShouldBeOk();
        Assert.Equal(Xlsx.ContentType, response.ContentType);
        Assert.Contains(fileName, response.ContentDisposition);
        var content = Xlsx.Read(response.Bytes);
        Assert.Equal(sheet, content.SheetName);
        Assert.Equal(headers, content.Headers);
        Assert.Single(content.Rows);
    }

    [Fact]
    public async Task A_downloaded_template_is_read_by_the_matching_import_without_a_format_error()
    {
        var admin = await fixture.AsAsync("Admin");
        var template = (await admin.Import.TemplateAsync("products")).Bytes;

        var response = await admin.Import.ProductsAsync(template, commit: false);

        var result = response.Data<Ims.Application.Features.Import.ImportResult>();
        Assert.Equal(1, result.Total); // the sample row is read; its barcodes may clash with demo data, which is a row error, not a format error
    }

    [Fact]
    public async Task An_unknown_template_is_400()
    {
        var admin = await fixture.AsAsync("Admin");

        (await admin.Import.TemplateAsync("nonsense")).ShouldBeProblem(HttpStatusCode.BadRequest);
    }
}
