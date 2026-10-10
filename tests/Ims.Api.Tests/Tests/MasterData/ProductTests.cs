using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;

namespace Ims.Api.Tests.Tests.MasterData;

[Collection(ApiCollection.Name)]
public class ProductTests(ApiFixture fixture)
{
    private ReferenceData Ref => fixture.Reference;

    [Fact]
    public async Task Create_returns_the_product_with_its_barcodes_unit_and_category()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcode = Unique.Barcode();

        var created = await new ProductBuilder(Ref).WithSafetyStock(12.5m).WithBarcodes(barcode).CreateAsync(admin);

        Assert.True(created.Id > 0);
        Assert.True(created.IsActive);
        Assert.Equal(12.5m, created.SafetyStock);
        Assert.Equal([barcode], created.Barcodes);
        Assert.Equal("PCS", created.BaseUomCode);
        Assert.Equal(Ref.DefaultCategoryId, created.CategoryId);
        var fetched = (await admin.Products.GetAsync(created.Id)).Data<ProductDto>();
        Assert.Equal((created.Sku, created.Name, created.BaseUomId), (fetched.Sku, fetched.Name, fetched.BaseUomId));
    }

    [Fact]
    public async Task Search_finds_a_product_by_sku_name_or_exact_barcode()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcode = Unique.Barcode();
        var product = await new ProductBuilder(Ref).WithBarcodes(barcode).CreateAsync(admin);

        foreach (var term in new[] { product.Sku, product.Name, barcode })
        {
            var page = (await admin.Products.ListAsync(search: term)).Data<PagedResult<ProductDto>>();
            Assert.Contains(page.Items, p => p.Id == product.Id);
        }
    }

    [Fact]
    public async Task Lookup_resolves_a_barcode_or_a_sku()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcode = Unique.Barcode();
        var product = await new ProductBuilder(Ref).WithBarcodes(barcode).CreateAsync(admin);

        foreach (var code in new[] { barcode, product.Sku })
        {
            var result = (await admin.Products.LookupAsync(code)).Data<ProductLookupResult>();
            Assert.True(result.Found);
            Assert.Equal(product.Id, result.Product!.Id);
        }
    }

    [Fact]
    public async Task Lookup_of_an_unknown_code_is_a_200_with_found_false_not_a_404()
    {
        var admin = await fixture.AsAsync("Admin");

        var response = await admin.Products.LookupAsync("no-such-code-" + Guid.NewGuid().ToString("N"));

        response.ShouldBeOk();
        var result = response.Read<ProductLookupResult>();
        Assert.False(result.Found);
        Assert.Null(result.Product);
        using var json = response.Json();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.RootElement.GetProperty("product").ValueKind);
    }

    [Fact]
    public async Task Update_changes_fields_and_replaces_the_barcode_list()
    {
        var admin = await fixture.AsAsync("Admin");
        var product = await new ProductBuilder(Ref).WithBarcodes(Unique.Barcode(), Unique.Barcode()).CreateAsync(admin);
        var keep = product.Barcodes[0];
        var added = Unique.Barcode();

        var request = ProductBuilder.From(Ref, product).WithName("Renamed " + product.Name).WithSafetyStock(7).WithBarcodes(keep, added).Build();
        var updated = (await admin.Products.UpdateAsync(product.Id, request)).Data<ProductDto>();

        Assert.Equal(request.Name, updated.Name);
        Assert.Equal(7, updated.SafetyStock);
        Assert.Equal(new[] { keep, added }.Order(), updated.Barcodes.Order());
        var fetched = (await admin.Products.GetAsync(product.Id)).Data<ProductDto>();
        Assert.Equal((updated.Name, updated.SafetyStock), (fetched.Name, fetched.SafetyStock));
        Assert.Equal(updated.Barcodes.Order(), fetched.Barcodes.Order());
    }

    [Fact]
    public async Task A_product_keeps_its_own_barcodes_on_update_without_a_conflict()
    {
        var admin = await fixture.AsAsync("Admin");
        var product = await new ProductBuilder(Ref).WithBarcodes(Unique.Barcode()).CreateAsync(admin);

        (await admin.Products.UpdateAsync(product.Id, ProductBuilder.From(Ref, product).Build())).ShouldBeOk();
    }

    [Fact]
    public async Task The_same_barcode_twice_in_one_request_is_stored_once()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcode = Unique.Barcode();

        var product = await new ProductBuilder(Ref).WithBarcodes(barcode, barcode).CreateAsync(admin);

        Assert.Equal([barcode], product.Barcodes);
    }

    [Fact]
    public async Task Update_of_a_missing_product_is_404()
    {
        var admin = await fixture.AsAsync("Admin");

        var response = await admin.Products.UpdateAsync(9_999_999, new ProductBuilder(Ref).Build());

        response.ShouldBeProblem(HttpStatusCode.NotFound, "Not found");
        (await admin.Products.GetAsync(9_999_999)).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    // ---------- uniqueness ----------

    [Fact]
    public async Task A_duplicate_sku_is_409_naming_the_sku()
    {
        var admin = await fixture.AsAsync("Admin");
        var existing = await new ProductBuilder(Ref).CreateAsync(admin);

        var response = await admin.Products.CreateAsync(new ProductBuilder(Ref).WithSku(existing.Sku).Build());

        var problem = response.ShouldBeProblem(HttpStatusCode.Conflict, "Conflict");
        Assert.Contains(existing.Sku, problem.Detail);
    }

    [Fact]
    public async Task A_sku_that_differs_only_by_case_or_spaces_is_still_a_duplicate()
    {
        var admin = await fixture.AsAsync("Admin");
        var existing = await new ProductBuilder(Ref).WithSku(Unique.Sku("Case")).CreateAsync(admin);

        (await admin.Products.CreateAsync(new ProductBuilder(Ref).WithSku(existing.Sku.ToLowerInvariant()).Build())).ShouldBe(HttpStatusCode.Conflict);
        (await admin.Products.CreateAsync(new ProductBuilder(Ref).WithSku("  " + existing.Sku + " ").Build())).ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Renaming_a_sku_to_another_products_sku_is_409()
    {
        var admin = await fixture.AsAsync("Admin");
        var a = await new ProductBuilder(Ref).CreateAsync(admin);
        var b = await new ProductBuilder(Ref).CreateAsync(admin);

        var response = await admin.Products.UpdateAsync(b.Id, ProductBuilder.From(Ref, b).WithSku(a.Sku).Build());

        response.ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_barcode_owned_by_another_product_is_409_on_create_and_on_update()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcode = Unique.Barcode();
        await new ProductBuilder(Ref).WithBarcodes(barcode).CreateAsync(admin);
        var other = await new ProductBuilder(Ref).CreateAsync(admin);

        var create = await admin.Products.CreateAsync(new ProductBuilder(Ref).WithBarcodes(barcode).Build());
        var update = await admin.Products.UpdateAsync(other.Id, ProductBuilder.From(Ref, other).WithBarcodes(barcode).Build());

        Assert.Contains(barcode, create.ShouldBeProblem(HttpStatusCode.Conflict, "Conflict").Detail);
        update.ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_conflicting_create_leaves_no_half_saved_product_behind()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcode = Unique.Barcode();
        await new ProductBuilder(Ref).WithBarcodes(barcode).CreateAsync(admin);
        var sku = Unique.Sku("Half");

        (await admin.Products.CreateAsync(new ProductBuilder(Ref).WithSku(sku).WithBarcodes(barcode).Build())).ShouldBe(HttpStatusCode.Conflict);

        Assert.Empty((await admin.Products.ListAsync(search: sku)).Data<PagedResult<ProductDto>>().Items);
    }

    // ---------- validation ----------

    [Theory]
    [InlineData("empty sku", "Sku")]
    [InlineData("blank sku", "Sku")]
    [InlineData("sku over 64 characters", "Sku")]
    [InlineData("empty name", "Name")]
    [InlineData("name over 300 characters", "Name")]
    [InlineData("negative safety stock", "SafetyStock")]
    public async Task Invalid_input_is_400_with_an_error_on_the_offending_field(string fault, string field)
    {
        var admin = await fixture.AsAsync("Admin");
        var builder = new ProductBuilder(Ref);
        _ = fault switch
        {
            "empty sku" => builder.WithSku(""),
            "blank sku" => builder.WithSku("   "),
            "sku over 64 characters" => builder.WithSku(new string('S', 65)),
            "empty name" => builder.WithName(""),
            "name over 300 characters" => builder.WithName(new string('N', 301)),
            "negative safety stock" => builder.WithSafetyStock(-1),
            _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, null)
        };

        var response = await admin.Products.CreateAsync(builder.Build());

        var problem = response.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.NotEmpty(problem.ErrorsFor(field));
    }

    [Fact]
    public async Task An_unknown_base_unit_or_category_is_400()
    {
        var admin = await fixture.AsAsync("Admin");

        (await admin.Products.CreateAsync(new ProductBuilder(Ref).WithBaseUnit(9_999_999).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.Products.CreateAsync(new ProductBuilder(Ref).WithCategory(9_999_999).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_conversion_must_lead_into_the_base_unit_with_a_positive_factor()
    {
        var admin = await fixture.AsAsync("Admin");
        var wrongTarget = new ProductBuilder(Ref).Build() with { Conversions = [new UomConversionDto(Ref.CartonUnitId, Ref.KilogramUnitId, 12)] };
        var zeroFactor = new ProductBuilder(Ref).Build() with { Conversions = [new UomConversionDto(Ref.CartonUnitId, Ref.PieceUnitId, 0)] };

        (await admin.Products.CreateAsync(wrongTarget)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.Products.CreateAsync(zeroFactor)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_malformed_json_body_is_400_not_500()
    {
        var admin = await fixture.AsAsync("Admin");

        var response = await admin.PostRawJsonAsync("/api/products", "{ \"sku\": ");

        response.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- deactivation and the base-unit lock ----------

    [Fact]
    public async Task A_deactivated_product_is_refused_on_new_documents_and_accepted_again_when_reactivated()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var warehouse = fixture.Reference.DefaultWarehouseId;
        var receipt = new DocumentBuilder(warehouse).WithLines(new Line(product.Id, 1, 1m)).Build();

        (await s.Admin.Products.UpdateAsync(product.Id, ProductBuilder.From(Ref, product).Active(false).Build())).ShouldBeOk();
        var onReceipt = await s.Admin.Documents.CreateReceiptAsync(receipt);
        var onIssue = await s.Admin.Documents.CreateIssueAsync(receipt);

        Assert.Contains("inactive", onReceipt.ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);
        onIssue.ShouldBeProblem(HttpStatusCode.BadRequest);

        (await s.Admin.Products.UpdateAsync(product.Id, ProductBuilder.From(Ref, product).Active(true).Build())).ShouldBeOk();
        (await s.Admin.Documents.CreateReceiptAsync(receipt)).ShouldBeOk();
    }

    [Fact]
    public async Task A_deactivated_product_keeps_its_history_and_can_be_filtered_out_of_lists()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Ref.DefaultWarehouseId, new Line(product.Id, 4, 1m));

        (await s.Admin.Products.UpdateAsync(product.Id, ProductBuilder.From(Ref, product).Active(false).Build())).ShouldBeOk();

        Assert.False((await s.Admin.Products.GetAsync(product.Id)).Data<ProductDto>().IsActive);
        Assert.Equal(4, await s.OnHandAsync(product.Id, Ref.DefaultWarehouseId));
        Assert.Empty((await s.Admin.Products.ListAsync(search: product.Sku, active: true)).Data<PagedResult<ProductDto>>().Items);
        Assert.Single((await s.Admin.Products.ListAsync(search: product.Sku, active: false)).Data<PagedResult<ProductDto>>().Items);
    }

    [Fact]
    public async Task The_base_unit_can_change_before_any_stock_movement_and_is_locked_after()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var changed = (await s.Admin.Products.UpdateAsync(product.Id, ProductBuilder.From(Ref, product).WithBaseUnit(Ref.KilogramUnitId).Build())).Data<ProductDto>();
        Assert.Equal("KG", changed.BaseUomCode);

        await s.ReceiveAsync(Ref.DefaultWarehouseId, new Line(product.Id, 3, 1m));
        var refused = await s.Admin.Products.UpdateAsync(product.Id, ProductBuilder.From(Ref, changed).WithBaseUnit(Ref.PieceUnitId).Build());

        Assert.Contains("base unit cannot change", refused.ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("KG", (await s.Admin.Products.GetAsync(product.Id)).Data<ProductDto>().BaseUomCode);
    }

    [Fact]
    public async Task Other_fields_can_still_change_after_stock_movements()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Ref.DefaultWarehouseId, new Line(product.Id, 3, 1m));

        var updated = await s.Admin.Products.UpdateAsync(product.Id, ProductBuilder.From(Ref, product).WithName("Still editable").WithSafetyStock(9).Build());

        Assert.Equal("Still editable", updated.Data<ProductDto>().Name);
    }
}
