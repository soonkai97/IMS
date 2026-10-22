using System.Text.Json;
using System.Text.Json.Nodes;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Import;
using Ims.Application.Features.Partners;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.MasterData;

/// <summary>
/// The tax-ready fields (<c>hsCode</c>, <c>sstCode</c>, <c>tin</c>, <c>sstRegNo</c>) are gone from the API with the accounting module (RemoveAccountingModule, then RemoveTaxReadyFields): a
/// request that still carries one of the keys is not refused, the key is ignored and nothing is stored; no response carries them; the products template and the import have no such columns.
/// Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class RemovedTaxFieldsTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly string[] Keys = ["hsCode", "sstCode", "tin", "sstRegNo"];

    private ApiClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AsAsync(Roles.Admin);

    public Task DisposeAsync() => Task.CompletedTask;

    private static JsonObject WithOldKeys(object request)
    {
        var body = JsonSerializer.SerializeToNode(request, request.GetType(), JsonDefaults.Options)!.AsObject();
        foreach (var key in Keys) body[key] = "C2584563200";
        return body;
    }

    private static void AssertNoTaxKeys(ApiResponse response) =>
        Assert.All(Keys, key => Assert.DoesNotContain($"\"{key}\"", response.Body, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task A_product_save_that_still_carries_the_old_keys_is_accepted_and_the_keys_are_ignored()
    {
        var request = new ProductBuilder(fixture.Reference).Build();

        var created = (await _admin.PostAsync("/api/products", WithOldKeys(request))).ShouldBeOk();
        var product = created.Read<ProductDto>();
        var updated = (await _admin.PutAsync($"/api/products/{product.Id}", WithOldKeys(request with { Name = Unique.Name("Renamed") }))).ShouldBeOk();

        AssertNoTaxKeys(created);
        AssertNoTaxKeys(updated);
        AssertNoTaxKeys(await _admin.Products.GetAsync(product.Id));
        AssertNoTaxKeys(await _admin.Products.ListAsync(search: product.Sku));
    }

    [Fact]
    public async Task A_partner_save_that_still_carries_the_old_keys_is_accepted_and_the_keys_are_ignored()
    {
        var request = new PartnerSaveRequest(PartnerType.Customer, Unique.Name("Old keys"), "0123", true);

        var created = (await _admin.PostAsync("/api/partners", WithOldKeys(request))).ShouldBeOk();
        var partner = created.Read<PartnerDto>();
        var updated = (await _admin.PutAsync($"/api/partners/{partner.Id}", WithOldKeys(request with { Contact = "0999" }))).ShouldBeOk();

        AssertNoTaxKeys(created);
        AssertNoTaxKeys(updated);
        AssertNoTaxKeys(await _admin.Partners.ListAsync(search: partner.Name));
    }

    [Fact]
    public async Task The_products_template_has_no_tax_columns_and_a_file_with_one_imports_without_it()
    {
        var template = Xlsx.Read((await _admin.Import.TemplateAsync(ImportKinds.Products)).ShouldBeOk().Bytes);
        Assert.Equal(["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes"], template.Headers);

        var sku = Unique.Sku("TAXI");
        object?[] row = [sku, "Imported item", "General", "PCS", 1, null, "8471.30"];
        var file = Xlsx.Create("Products", ["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes", "HsCode"], row);
        var result = (await _admin.Import.ProductsAsync(file, commit: true)).Data<ImportResult>();

        Assert.Equal((1, 0), (result.Created, result.Invalid));
        Assert.Equal(sku, (await _admin.Products.FindAsync(sku)).Sku);
    }
}
