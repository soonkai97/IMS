using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Import;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Import;

/// <summary>
/// PL1.5 acceptance 5, what is left after the accounting module was removed (RemoveAccountingModule, part A): the optional HsCode and SstCode columns cannot be switched on any more, so a products
/// file that carries either column is refused as a whole, the products template does not show them, and a file without them imports as before and leaves stored codes alone. The tests of the
/// module being on (shape errors per row, trimmed values stored, number cells refused, blank clears, audit) went with the module; part B deletes the columns and this class. Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class TaxColumnsImportTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly string[] Base = ["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes"];

    private ApiClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AsAsync(Roles.Admin);

    public Task DisposeAsync() => Task.CompletedTask;

    private static object?[] Row(string sku, params object?[] tax) => [sku, "Imported item", "General", "PCS", 1, null, .. tax];

    // ----- template -----
    [Fact]
    public async Task The_products_template_has_no_tax_columns()
    {
        var template = Xlsx.Read((await _admin.Import.TemplateAsync(ImportKinds.Products)).ShouldBeOk().Bytes);

        Assert.Equal(["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes"], template.Headers);
    }

    // ----- refused -----
    [Theory]
    [InlineData("HsCode")]
    [InlineData("SstCode")]
    [InlineData("hs code")] // the importer ignores case, spaces and underscores in a header
    public async Task A_file_with_a_tax_column_is_refused_as_a_whole_and_nothing_is_written(string header)
    {
        var sku = Unique.Sku("TAXI");
        var file = Xlsx.Create("Products", [.. Base, header], Row(sku, (object?)null));

        var preview = (await _admin.Import.ProductsAsync(file, commit: false)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var commit = (await _admin.Import.ProductsAsync(file, commit: true)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("not available", preview.Detail);
        Assert.Contains("accounting", commit.Detail);
        Assert.Empty((await _admin.Products.ListAsync(search: sku)).Data<PagedResult<ProductDto>>().Items);
    }

    [Fact]
    public async Task A_file_without_the_tax_columns_imports_as_before_and_leaves_stored_codes_alone()
    {
        var product = (await _admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).Build())).Data<ProductDto>();
        await fixture.Database.ExecuteAsync("UPDATE Product SET HsCode = N'8471.30', SstCode = N'ST-01' WHERE PublicId = @id", ("@id", product.Id));

        var result = (await _admin.Import.ProductsAsync(Xlsx.Create("Products", Base, Row(product.Sku)), commit: true)).Data<ImportResult>();

        Assert.Equal((1, 0), (result.Updated, result.Invalid));
        Assert.Equal("8471.30", await fixture.Database.ScalarAsync<string?>("SELECT HsCode FROM Product WHERE PublicId = @id", ("@id", product.Id)));
        Assert.Equal("ST-01", await fixture.Database.ScalarAsync<string?>("SELECT SstCode FROM Product WHERE PublicId = @id", ("@id", product.Id)));
    }
}
