using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Import;

namespace Ims.Api.Tests.Tests.Import;

/// <summary>
/// PS-02 / PS-19: the product import never creates a category or unit (one typo must not pollute the catalogue); it reports them as row errors.
/// Units match by code or by name, case-insensitively. A bad number says it is not a number. The unchanged template imports cleanly.
/// </summary>
[Collection(ApiCollection.Name)]
public class ImportReferenceDataTests(ApiFixture fixture)
{
    private static readonly string[] Headers = ["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes"];

    [Fact]
    public async Task An_unknown_category_is_a_row_error_and_nothing_is_created_by_preview_or_commit()
    {
        var admin = await fixture.AsAsync("Admin");
        var category = Unique.Name("Nonexistent");
        var sku = Unique.Sku("IMP");
        var file = Xlsx.Create("Products", Headers, [sku, "Typo category", category, "PCS", 1, null]);
        var categoriesBefore = (await admin.Categories.ListAsync()).Data<List<CategoryDto>>().Count;

        var preview = (await admin.Import.ProductsAsync(file, commit: false)).Data<ImportResult>();
        var commit = await admin.Import.ProductsAsync(file, commit: true);

        var row = Assert.Single(preview.Rows);
        Assert.False(row.Ok);
        Assert.Equal(ImportRowStatus.Error, row.Status);
        Assert.Equal($"Category '{category}' doesn't exist. Add it first or fix the name.", Assert.Single(row.Errors));
        commit.ShouldBeProblem(System.Net.HttpStatusCode.BadRequest);
        Assert.Equal(categoriesBefore, (await admin.Categories.ListAsync()).Data<List<CategoryDto>>().Count);
        Assert.Empty((await admin.Products.ListAsync(search: sku)).Data<Ims.Application.PagedResult<ProductDto>>().Items);
    }

    [Fact]
    public async Task An_unknown_unit_is_a_row_error_and_no_unit_is_created()
    {
        var admin = await fixture.AsAsync("Admin");
        var unit = Unique.Code("Z");
        var file = Xlsx.Create("Products", Headers, [Unique.Sku("IMP"), "Typo unit", "", unit, 1, null]);
        var unitsBefore = (await admin.Units.ListAsync()).Data<List<UomDto>>().Count;

        var preview = (await admin.Import.ProductsAsync(file, commit: false)).Data<ImportResult>();
        (await admin.Import.ProductsAsync(file, commit: true)).ShouldBeProblem(System.Net.HttpStatusCode.BadRequest);

        Assert.Contains("doesn't exist", Assert.Single(Assert.Single(preview.Rows).Errors));
        Assert.Equal(unitsBefore, (await admin.Units.ListAsync()).Data<List<UomDto>>().Count);
    }

    [Theory]
    [InlineData("CTN")]
    [InlineData("ctn")]
    [InlineData("Carton")]
    [InlineData("carton")]
    public async Task A_unit_matches_by_code_or_name_without_regard_to_case_and_no_second_unit_appears(string text)
    {
        var admin = await fixture.AsAsync("Admin");
        var sku = Unique.Sku("IMP");
        var file = Xlsx.Create("Products", Headers, [sku, "In cartons", "", text, 1, null]);
        var unitsBefore = (await admin.Units.ListAsync()).Data<List<UomDto>>().Count;

        (await admin.Import.ProductsAsync(file, commit: true)).ShouldBeOk();

        Assert.Equal("CTN", (await admin.Products.FindAsync(sku)).BaseUomCode);
        Assert.Equal(unitsBefore, (await admin.Units.ListAsync()).Data<List<UomDto>>().Count);
    }

    [Fact]
    public async Task A_category_matches_without_regard_to_case_and_a_blank_unit_means_pieces()
    {
        var admin = await fixture.AsAsync("Admin");
        var category = Unique.Name("MixedCase");
        (await admin.Categories.CreateAsync(category)).ShouldBeOk();
        var sku = Unique.Sku("IMP");
        var file = Xlsx.Create("Products", Headers, [sku, "Lower case category", category.ToLowerInvariant(), null, 1, null]);

        (await admin.Import.ProductsAsync(file, commit: true)).ShouldBeOk();

        var product = await admin.Products.FindAsync(sku);
        Assert.Equal((category, "PCS"), (product.CategoryName, product.BaseUomCode));
    }

    [Fact]
    public async Task A_safety_stock_that_is_not_a_number_says_so_and_a_negative_one_says_that()
    {
        var admin = await fixture.AsAsync("Admin");
        var file = Xlsx.Create("Products", Headers,
            [Unique.Sku("IMP"), "Letters", "General", "PCS", "abc", null],
            [Unique.Sku("IMP"), "Negative", "General", "PCS", -2, null]);

        var rows = (await admin.Import.ProductsAsync(file, commit: false)).Data<ImportResult>().Rows;

        Assert.Equal("Safety stock 'abc' is not a number.", Assert.Single(rows[0].Errors));
        Assert.Equal("Safety stock cannot be negative.", Assert.Single(rows[1].Errors));
    }

    [Fact]
    public async Task A_duplicate_sku_names_the_row_where_it_first_appeared()
    {
        var admin = await fixture.AsAsync("Admin");
        var sku = Unique.Sku("IMP");
        var file = Xlsx.Create("Products", Headers,
            [sku, "First", "General", "PCS", 1, null],
            [Unique.Sku("IMP"), "Other", "General", "PCS", 1, null],
            [sku.ToLowerInvariant(), "Again", "General", "PCS", 1, null]);

        var rows = (await admin.Import.ProductsAsync(file, commit: false)).Data<ImportResult>().Rows;

        Assert.Equal($"SKU '{sku.ToLowerInvariant()}' also appears in row 2.", Assert.Single(rows[2].Errors));
    }

    [Fact]
    public async Task Several_problems_in_one_row_are_all_reported()
    {
        var admin = await fixture.AsAsync("Admin");
        var file = Xlsx.Create("Products", Headers, [Unique.Sku("IMP"), "", Unique.Name("NoSuchCat"), Unique.Code("Q"), "x", null]);

        var row = Assert.Single((await admin.Import.ProductsAsync(file, commit: false)).Data<ImportResult>().Rows);

        Assert.Equal(4, row.Errors.Count); // name, category, unit, number
    }

    [Fact]
    public async Task The_downloaded_product_template_previews_with_no_error_rows()
    {
        var admin = await fixture.AsAsync("Admin");
        var template = (await admin.Import.TemplateAsync("products")).Bytes;

        var result = (await admin.Import.ProductsAsync(template, commit: false)).Data<ImportResult>();

        var row = Assert.Single(result.Rows);
        Assert.True(row.Ok, string.Join("; ", row.Errors));
    }

    [Fact]
    public async Task An_opening_stock_quantity_or_cost_that_is_not_a_number_says_so()
    {
        var admin = await fixture.AsAsync("Admin");
        var product = Ims.Infrastructure.SeedDefaults.DemoProducts[0].Sku;
        var file = Xlsx.Create("Opening stock", ["SKU", "Warehouse", "Qty", "UnitCost"],
            [product, Ims.Infrastructure.SeedDefaults.DefaultWarehouseCode, "ten", 1],
            [product, Ims.Infrastructure.SeedDefaults.DefaultWarehouseCode, 5, "cheap"],
            [product, Ims.Infrastructure.SeedDefaults.DefaultWarehouseCode, 0, 1]);

        var rows = (await admin.Import.OpeningStockAsync(file, commit: false)).Data<ImportResult>().Rows;

        Assert.Equal("Quantity 'ten' is not a number.", Assert.Single(rows[0].Errors));
        Assert.Equal("Unit cost 'cheap' is not a number.", Assert.Single(rows[1].Errors));
        Assert.Contains("greater than zero", Assert.Single(rows[2].Errors));
    }
}
