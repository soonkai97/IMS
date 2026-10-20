using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Import;
using Ims.Application.Features.Modules;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Import;

/// <summary>
/// PL1.5 acceptance 5: the products import takes the optional HsCode and SstCode columns only while the accounting module is on, with the same shape rules as the API, and the products
/// template shows them only then. Off, a file that carries either column is refused as a whole. On, a column in the file replaces that field of every row (a blank cell clears it), and a
/// column that is not in the file leaves the stored values alone. Real SQL Server; every test starts and ends with no module row.
/// </summary>
[Collection(ApiCollection.Name)]
public class TaxColumnsImportTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly string[] Base = ["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes"];

    private ApiClient _admin = null!;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _admin = await fixture.AsAsync(Roles.Admin);
    }

    public Task DisposeAsync() => ResetAsync();

    private Task ResetAsync() => fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    private async Task SwitchAsync(bool enabled) =>
        (await _admin.Modules.SwitchAsync(ModuleCodes.Accounting, enabled, $"Import tax test {Guid.NewGuid():N}"[..24])).ShouldBeOk();

    private async Task<ProductDto> FindAsync(string sku) => await _admin.Products.FindAsync(sku);

    private static object?[] Row(string sku, params object?[] tax) => [sku, "Imported item", "General", "PCS", 1, null, .. tax];

    // ----- template -----
    [Fact]
    public async Task The_products_template_has_the_tax_columns_only_while_the_module_is_on()
    {
        var off = Xlsx.Read((await _admin.Import.TemplateAsync(ImportKinds.Products)).ShouldBeOk().Bytes);
        await SwitchAsync(true);
        var on = Xlsx.Read((await _admin.Import.TemplateAsync(ImportKinds.Products)).ShouldBeOk().Bytes);

        Assert.Equal(["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes"], off.Headers);
        Assert.Equal(["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes", "HsCode", "SstCode"], on.Headers);
        Assert.Equal(["", ""], on.Rows[0].Skip(6)); // the sample row leaves them blank, so the unchanged template stores no tax code
    }

    [Fact]
    public async Task The_opening_stock_template_is_the_same_with_the_module_on()
    {
        var off = Xlsx.Read((await _admin.Import.TemplateAsync(ImportKinds.OpeningStock)).ShouldBeOk().Bytes).Headers;
        await SwitchAsync(true);

        Assert.Equal(off, Xlsx.Read((await _admin.Import.TemplateAsync(ImportKinds.OpeningStock)).ShouldBeOk().Bytes).Headers);
    }

    // ----- off -----
    [Theory]
    [InlineData("HsCode")]
    [InlineData("SstCode")]
    [InlineData("hs code")] // the importer ignores case, spaces and underscores in a header
    public async Task With_the_module_off_a_file_with_a_tax_column_is_refused_as_a_whole_and_nothing_is_written(string header)
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
    public async Task With_the_module_off_a_file_without_the_tax_columns_imports_as_before_and_leaves_stored_codes_alone()
    {
        await SwitchAsync(true);
        var product = (await _admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithTaxCodes("8471.30", "ST-01").Build())).Data<ProductDto>();
        await SwitchAsync(false);

        var result = (await _admin.Import.ProductsAsync(Xlsx.Create("Products", Base, Row(product.Sku)), commit: true)).Data<ImportResult>();
        await SwitchAsync(true);

        Assert.Equal((1, 0), (result.Updated, result.Invalid));
        var after = await FindAsync(product.Sku);
        Assert.Equal(("8471.30", "ST-01"), (after.HsCode, after.SstCode));
    }

    // ----- on -----
    [Fact]
    public async Task With_the_module_on_the_preview_lists_every_row_with_a_bad_tax_value_and_a_commit_is_refused()
    {
        await SwitchAsync(true);
        var (good, badHs, badSst, badBoth) = (Unique.Sku("TAXI"), Unique.Sku("TAXI"), Unique.Sku("TAXI"), Unique.Sku("TAXI"));
        var file = Xlsx.Create("Products", [.. Base, "HsCode", "SstCode"],
            Row(good, "8471.30", "ST-01"),          // row 2: fine
            Row(badHs, "84-71", "ST-01"),           // row 3
            Row(badSst, "8471.30", "ST_01"),        // row 4
            Row(badBoth, "12345678901234567", "123456789012345678901")); // row 5: both, too long

        var preview = (await _admin.Import.ProductsAsync(file, commit: false)).Data<ImportResult>();
        var commit = await _admin.Import.ProductsAsync(file, commit: true);

        Assert.Equal((4, 1, 3), (preview.Total, preview.Valid, preview.Invalid));
        Assert.True(preview.Rows[0].Ok);
        Assert.Equal(Messages.HsCodeInvalid, Assert.Single(preview.Rows[1].Errors));
        Assert.Equal(Messages.SstCodeInvalid, Assert.Single(preview.Rows[2].Errors));
        Assert.Equal([Messages.HsCodeInvalid, Messages.SstCodeInvalid], preview.Rows[3].Errors);
        commit.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Empty((await _admin.Products.ListAsync(search: good)).Data<PagedResult<ProductDto>>().Items); // all or nothing
    }

    [Fact]
    public async Task With_the_module_on_a_commit_stores_trimmed_values_on_new_and_existing_products()
    {
        await SwitchAsync(true);
        var existing = (await _admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithTaxCodes("1111", "OLD-1").Build())).Data<ProductDto>();
        var fresh = Unique.Sku("TAXI");
        var file = Xlsx.Create("Products", [.. Base, "HsCode", "SstCode"], Row(fresh, "  8471.30 ", "ST-01"), Row(existing.Sku, "9999", "ST-02"));

        var result = (await _admin.Import.ProductsAsync(file, commit: true)).Data<ImportResult>();

        Assert.Equal((1, 1), (result.Created, result.Updated));
        var created = await FindAsync(fresh);
        var updated = await FindAsync(existing.Sku);
        Assert.Equal(("8471.30", "ST-01"), (created.HsCode, created.SstCode));
        Assert.Equal(("9999", "ST-02"), (updated.HsCode, updated.SstCode));
    }

    [Fact]
    public async Task A_blank_cell_in_a_present_column_clears_the_field_and_a_column_that_is_not_in_the_file_leaves_it()
    {
        await SwitchAsync(true);
        var product = (await _admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithTaxCodes("8471.30", "ST-01").Build())).Data<ProductDto>();

        // Only the HsCode column, blank: the HS code goes, the SST code (no column) stays.
        (await _admin.Import.ProductsAsync(Xlsx.Create("Products", [.. Base, "HsCode"], Row(product.Sku, "")), commit: true)).ShouldBeOk();
        var afterHs = await FindAsync(product.Sku);
        Assert.Equal((null, "ST-01"), (afterHs.HsCode, afterHs.SstCode));

        // No tax column at all: nothing changes.
        (await _admin.Import.ProductsAsync(Xlsx.Create("Products", Base, Row(product.Sku)), commit: true)).ShouldBeOk();
        var afterNone = await FindAsync(product.Sku);
        Assert.Equal((null, "ST-01"), (afterNone.HsCode, afterNone.SstCode));

        // Only the SstCode column, with a value: it changes, the (empty) HS code stays empty.
        (await _admin.Import.ProductsAsync(Xlsx.Create("Products", [.. Base, "SstCode"], Row(product.Sku, "ST-09")), commit: true)).ShouldBeOk();
        var afterSst = await FindAsync(product.Sku);
        Assert.Equal((null, "ST-09"), (afterSst.HsCode, afterSst.SstCode));
    }

    [Fact]
    public async Task The_import_changes_are_in_the_row_audit_of_the_product()
    {
        await SwitchAsync(true);
        var sku = Unique.Sku("TAXI");

        (await _admin.Import.ProductsAsync(Xlsx.Create("Products", [.. Base, "HsCode"], Row(sku, "8471.30")), commit: true)).ShouldBeOk();

        var product = await FindAsync(sku);
        var rows = (await _admin.Audit.QueryAsync(entityType: nameof(Product), entityId: product.Id.ToString())).Data<AuditPage>().Items;
        Assert.Contains(rows, e => e.Action == AuditActions.RowCreated && e.After!.Contains("\"HsCode\":\"8471.30\""));
    }
}
