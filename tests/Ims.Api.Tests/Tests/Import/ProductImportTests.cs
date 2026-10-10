using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Import;

namespace Ims.Api.Tests.Tests.Import;

/// <summary>MD-05: the product import validates every row, writes nothing on preview and only the valid rows on commit.</summary>
[Collection(ApiCollection.Name)]
public class ProductImportTests(ApiFixture fixture)
{
    private static readonly string[] Headers = ["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes"];

    private async Task<bool> ExistsAsync(ApiClient client, string sku) =>
        (await client.Products.ListAsync(search: sku)).Data<PagedResult<ProductDto>>().Items.Any(p => p.Sku == sku);

    /// <summary>A file with one good row and five different kinds of bad row.</summary>
    private sealed record MixedFile(byte[] Bytes, string GoodSku, string MissingNameSku, string NegativeSafetySku, string GoodCategory, string GoodBarcode, string TakenBarcode, string OwnerSku);

    private async Task<MixedFile> BuildMixedFileAsync()
    {
        var admin = await fixture.AsAsync("Admin");
        var owner = await new ProductBuilder(fixture.Reference).WithBarcodes(Unique.Barcode()).CreateAsync(admin);
        var good = Unique.Sku("IMP");
        var missingName = Unique.Sku("IMP");
        var negative = Unique.Sku("IMP");
        var category = Unique.Name("ImportCat");
        var barcode = Unique.Barcode();

        var bytes = Xlsx.Create("Products", Headers,
            [good, "Good product", category, "PCS", 5, barcode + "," + Unique.Barcode()],       // row 2: fine
            [missingName, "", "General", "PCS", 1, null],                                       // row 3: no name
            [good, "Same sku again", "General", "PCS", 1, null],                                // row 4: duplicate sku in the file
            [Unique.Sku("IMP"), "Stolen barcode", "General", "PCS", 1, owner.Barcodes[0]],      // row 5: barcode belongs to another product
            [negative, "Negative safety", "General", "PCS", -3, null],                          // row 6: bad number
            ["", "No sku", "General", "PCS", 1, null]);                                         // row 7: no sku
        return new MixedFile(bytes, good, missingName, negative, category, barcode, owner.Barcodes[0], owner.Sku);
    }

    [Fact]
    public async Task Preview_reports_every_bad_row_and_writes_nothing()
    {
        var admin = await fixture.AsAsync("Admin");
        var file = await BuildMixedFileAsync();
        var productsBefore = (await admin.Products.ListAsync(pageSize: 1)).Data<PagedResult<ProductDto>>().Total;

        var result = (await admin.Import.ProductsAsync(file.Bytes, commit: false)).Data<ImportResult>();

        Assert.False(result.Committed);
        Assert.Equal((6, 1, 5, 1, 0), (result.Total, result.Valid, result.Invalid, result.Created, result.Updated)); // a preview reports what a commit would create
        Assert.Equal([2, 3, 4, 5, 6, 7], result.Rows.Select(r => r.Row));
        Assert.True(result.Rows[0].Ok);
        Assert.Contains("name", Assert.Single(result.Rows[1].Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("appears more than once", Assert.Single(result.Rows[2].Errors));
        Assert.Contains($"already belongs to {file.OwnerSku}", Assert.Single(result.Rows[3].Errors));
        Assert.Contains("safety stock", Assert.Single(result.Rows[4].Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SKU", Assert.Single(result.Rows[5].Errors));
        Assert.False(await ExistsAsync(admin, file.GoodSku));
        Assert.Equal(productsBefore, (await admin.Products.ListAsync(pageSize: 1)).Data<PagedResult<ProductDto>>().Total);
        Assert.DoesNotContain((await admin.Categories.ListAsync()).Data<List<CategoryDto>>(), c => c.Name == file.GoodCategory);
    }

    [Fact]
    public async Task Preview_is_the_default_when_commit_is_not_given()
    {
        var admin = await fixture.AsAsync("Admin");
        var sku = Unique.Sku("IMP");
        var file = Xlsx.Create("Products", Headers, [sku, "Not saved", "General", "PCS", 1, null]);

        var result = (await admin.Import.ProductsAsync(file, commit: false)).Data<ImportResult>();
        var withoutFlag = (await admin.UploadAsync("/api/import/products", file)).Data<ImportResult>();

        Assert.False(result.Committed);
        Assert.False(withoutFlag.Committed);
        Assert.False(await ExistsAsync(admin, sku));
    }

    [Fact]
    public async Task Commit_with_any_invalid_row_is_refused_and_nothing_at_all_is_written()
    {
        var admin = await fixture.AsAsync("Admin");
        var file = await BuildMixedFileAsync();

        var response = await admin.Import.ProductsAsync(file.Bytes, commit: true);

        var problem = response.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("nothing was imported", problem.Detail);
        Assert.False(await ExistsAsync(admin, file.GoodSku), "The one valid row must not be written when others are invalid.");
        Assert.False(await ExistsAsync(admin, file.MissingNameSku));
        Assert.DoesNotContain((await admin.Categories.ListAsync()).Data<List<CategoryDto>>(), c => c.Name == file.GoodCategory);
        Assert.Equal(file.OwnerSku, (await admin.Products.FindAsync(file.TakenBarcode)).Sku);
    }

    [Fact]
    public async Task Commit_of_a_file_without_errors_writes_every_row_with_its_category_unit_and_barcodes()
    {
        var admin = await fixture.AsAsync("Admin");
        var first = Unique.Sku("IMP");
        var second = Unique.Sku("IMP");
        var category = Unique.Name("ImportCat");
        var barcode = Unique.Barcode();
        var file = Xlsx.Create("Products", Headers,
            [first, "First product", category, "PCS", 5, barcode + "," + Unique.Barcode()],
            [second, "Second product", category, "BOX", 0, null]);

        var result = (await admin.Import.ProductsAsync(file, commit: true)).Data<ImportResult>();

        Assert.True(result.Committed);
        Assert.Equal((2, 2, 0, 2, 0), (result.Total, result.Valid, result.Invalid, result.Created, result.Updated));
        var one = (await admin.Products.FindAsync(first));
        Assert.Equal(("First product", category, "PCS", 5m), (one.Name, one.CategoryName, one.BaseUomCode, one.SafetyStock));
        Assert.Equal(2, one.Barcodes.Count);
        Assert.Contains(barcode, one.Barcodes);
        var two = (await admin.Products.FindAsync(second));
        Assert.Equal(("BOX", one.CategoryId), (two.BaseUomCode, two.CategoryId)); // the category is created once and shared
    }

    [Fact]
    public async Task Importing_an_existing_sku_updates_it_and_adds_barcodes_without_losing_old_ones()
    {
        var admin = await fixture.AsAsync("Admin");
        var existingBarcode = Unique.Barcode();
        var product = await new ProductBuilder(fixture.Reference).WithBarcodes(existingBarcode).WithSafetyStock(1).CreateAsync(admin);
        var newBarcode = Unique.Barcode();
        var file = Xlsx.Create("Products", Headers, [product.Sku, "Updated by import", "General", "PCS", 9, newBarcode]);

        var result = (await admin.Import.ProductsAsync(file, commit: true)).Data<ImportResult>();

        Assert.Equal((0, 1), (result.Created, result.Updated));
        var updated = (await admin.Products.GetAsync(product.Id)).Data<ProductDto>();
        Assert.Equal(("Updated by import", 9m), (updated.Name, updated.SafetyStock));
        Assert.Equal(new[] { existingBarcode, newBarcode }.Order(), updated.Barcodes.Order());
        Assert.True(updated.IsActive);
    }

    [Fact]
    public async Task Preview_marks_each_row_New_Update_or_Error_and_counts_what_a_commit_would_do()
    {
        var admin = await fixture.AsAsync("Admin");
        var existing = await new ProductBuilder(fixture.Reference).CreateAsync(admin);
        var created = Unique.Sku("IMP");
        var file = Xlsx.Create("Products", Headers,
            [created, "Brand new", "General", "PCS", 1, null],                 // row 2: new
            [existing.Sku.ToLowerInvariant(), "Renamed", "General", "PCS", 2, null], // row 3: existing SKU in another case: update
            [Unique.Sku("IMP"), "", "General", "PCS", 1, null]);                // row 4: error

        var preview = (await admin.Import.ProductsAsync(file, commit: false)).Data<ImportResult>();

        Assert.Equal([ImportRowStatus.New, ImportRowStatus.Update, ImportRowStatus.Error], preview.Rows.Select(r => r.Status));
        Assert.Equal((3, 2, 1, 1, 1), (preview.Total, preview.Valid, preview.Invalid, preview.Created, preview.Updated));
        Assert.False(await ExistsAsync(admin, created));
    }

    [Fact]
    public async Task The_preview_statuses_and_counts_are_what_the_commit_then_reports()
    {
        var admin = await fixture.AsAsync("Admin");
        var existing = await new ProductBuilder(fixture.Reference).CreateAsync(admin);
        var file = Xlsx.Create("Products", Headers,
            [Unique.Sku("IMP"), "Brand new", "General", "PCS", 1, null],
            [existing.Sku, "Renamed", "General", "PCS", 2, null]);

        var preview = (await admin.Import.ProductsAsync(file, commit: false)).Data<ImportResult>();
        var commit = (await admin.Import.ProductsAsync(file, commit: true)).Data<ImportResult>();

        Assert.Equal((preview.Created, preview.Updated), (commit.Created, commit.Updated));
        Assert.Equal(preview.Rows.Select(r => r.Status), commit.Rows.Select(r => r.Status));
    }

    [Fact]
    public async Task Row_status_is_published_as_a_string_on_the_wire()
    {
        var admin = await fixture.AsAsync("Admin");
        var file = Xlsx.Create("Products", Headers, [Unique.Sku("IMP"), "Brand new", "General", "PCS", 1, null]);

        var response = await admin.Import.ProductsAsync(file, commit: false);

        using var json = response.Json();
        Assert.Equal("New", json.RootElement.GetProperty("rows")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_unit_change_is_skipped_with_a_note_once_the_product_has_stock_movements()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 3, 1m));
        var file = Xlsx.Create("Products", Headers, [product.Sku, "Renamed", "General", "KG", 2, null]);

        var result = (await s.Admin.Import.ProductsAsync(file, commit: true)).Data<ImportResult>();

        var row = Assert.Single(result.Rows);
        Assert.True(row.Ok);
        Assert.Contains("Unit not changed", Assert.Single(row.Errors));
        var after = (await s.Admin.Products.GetAsync(product.Id)).Data<ProductDto>();
        Assert.Equal(("PCS", "Renamed"), (after.BaseUomCode, after.Name));
    }

    [Fact]
    public async Task An_unknown_unit_is_created_and_a_missing_unit_defaults_to_pieces()
    {
        var admin = await fixture.AsAsync("Admin");
        var unit = Unique.Code("U");
        var withUnit = Unique.Sku("IMP");
        var withoutUnit = Unique.Sku("IMP");
        var file = Xlsx.Create("Products", Headers, [withUnit, "Has unit", "", unit, 1, null], [withoutUnit, "No unit", "", null, 1, null]);

        (await admin.Import.ProductsAsync(file, commit: true)).ShouldBeOk();

        Assert.Equal(unit.ToUpperInvariant(), (await admin.Products.FindAsync(withUnit)).BaseUomCode);
        Assert.Equal("PCS", (await admin.Products.FindAsync(withoutUnit)).BaseUomCode);
    }

    [Fact]
    public async Task Header_names_are_matched_loosely_and_the_optional_columns_may_be_missing()
    {
        var admin = await fixture.AsAsync("Admin");
        var sku = Unique.Sku("IMP");
        var file = Xlsx.Create("Sheet1", ["sku", " NAME ", "Safety_Stock"], [sku, "Loose headers", 4]);

        var result = (await admin.Import.ProductsAsync(file, commit: true)).Data<ImportResult>();

        Assert.Equal(1, result.Created);
        Assert.Equal(4m, (await admin.Products.FindAsync(sku)).SafetyStock);
    }

    [Fact]
    public async Task A_file_without_the_required_columns_is_400_naming_them()
    {
        var admin = await fixture.AsAsync("Admin");
        var file = Xlsx.Create("Products", ["SKU", "Description"], [Unique.Sku("IMP"), "x"]);

        var problem = (await admin.Import.ProductsAsync(file, commit: true)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("Missing column", problem.Detail);
        Assert.Contains("name", problem.Detail);
    }

    [Fact]
    public async Task A_file_that_is_not_an_xlsx_workbook_is_400()
    {
        var admin = await fixture.AsAsync("Admin");

        var response = await admin.Import.ProductsAsync("SKU,Name\nA,B\n"u8.ToArray(), commit: true);

        Assert.Contains(".xlsx", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
    }

    [Fact]
    public async Task A_workbook_with_only_a_header_row_imports_nothing_without_error()
    {
        var admin = await fixture.AsAsync("Admin");

        var result = (await admin.Import.ProductsAsync(Xlsx.Create("Products", Headers), commit: true)).Data<ImportResult>();

        Assert.Equal((0, 0, 0, 0), (result.Total, result.Valid, result.Invalid, result.Created));
    }

    [Fact]
    public async Task More_than_five_thousand_rows_is_refused()
    {
        var admin = await fixture.AsAsync("Admin");
        var rows = Enumerable.Range(0, 5001).Select(i => new object?[] { $"BULK-{i}", "x" }).ToArray();

        var response = await admin.Import.ProductsAsync(Xlsx.Create("Products", ["SKU", "Name"], rows), commit: false);

        Assert.Contains("Too many rows", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
    }

    [Fact]
    public async Task A_supervisor_may_import_but_a_clerk_may_not()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var file = Xlsx.Create("Products", Headers, [Unique.Sku("IMP"), "By supervisor", "General", "PCS", 1, null]);

        (await s.Supervisor.Import.ProductsAsync(file, commit: false)).ShouldBeOk();
        (await s.Clerk.Import.ProductsAsync(file, commit: false)).ShouldBe(HttpStatusCode.Forbidden);
    }
}
