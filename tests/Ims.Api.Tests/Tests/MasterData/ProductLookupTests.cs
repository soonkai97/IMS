using System.Text.RegularExpressions;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.MasterData;

/// <summary>
/// Barcode or SKU lookup: the SKU is tried first, then the barcode, each by one seek on its unique index. One predicate over both columns made SQL Server walk the
/// Product table (docs/dba/README.md section 4d). The results must not change with that: case-insensitive, trimmed, inactive products found, replaced barcodes gone.
/// A code that is one product's SKU and another's barcode now has an answer, the SKU's product, whichever product was created first.
/// </summary>
[Collection(ApiCollection.Name)]
public class ProductLookupTests(ApiFixture fixture)
{
    private ReferenceData Ref => fixture.Reference;

    private async Task<ProductLookupResult> LookupAsync(ApiClient by, string code) => (await by.Products.LookupAsync(code)).Data<ProductLookupResult>();

    [Fact]
    public async Task A_sku_or_barcode_is_found_whatever_its_case_and_with_spaces_around_it()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcode = Unique.Sku("Bar");
        var product = await new ProductBuilder(Ref).WithSku(Unique.Sku("MixedCase")).WithBarcodes(barcode).CreateAsync(admin);

        foreach (var code in new[] { product.Sku.ToLowerInvariant(), product.Sku.ToUpperInvariant(), $"  {product.Sku}  ", barcode.ToLowerInvariant(), $" {barcode.ToUpperInvariant()} " })
        {
            var result = await LookupAsync(admin, code);
            Assert.True(result.Found, code);
            Assert.Equal(product.Id, result.Product!.Id);
        }
    }

    [Fact]
    public async Task A_product_with_several_barcodes_is_found_by_each_and_comes_back_with_all_of_them()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcodes = new[] { Unique.Barcode(), Unique.Barcode(), Unique.Barcode() };
        var product = await new ProductBuilder(Ref).WithBarcodes(barcodes).CreateAsync(admin);

        foreach (var barcode in barcodes)
        {
            var found = (await LookupAsync(admin, barcode)).Product!;
            Assert.Equal(product.Id, found.Id);
            Assert.Equal(barcodes.Order(), found.Barcodes.Order());
        }
    }

    [Fact]
    public async Task An_inactive_product_is_still_found_by_sku_and_barcode()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcode = Unique.Barcode();
        var product = await new ProductBuilder(Ref).Inactive().WithBarcodes(barcode).CreateAsync(admin);

        foreach (var code in new[] { product.Sku, barcode })
        {
            var found = (await LookupAsync(admin, code)).Product!;
            Assert.Equal(product.Id, found.Id);
            Assert.False(found.IsActive);
        }
    }

    [Fact]
    public async Task A_barcode_taken_off_a_product_is_not_found_any_more()
    {
        var admin = await fixture.AsAsync("Admin");
        var keep = Unique.Barcode();
        var dropped = Unique.Barcode();
        var product = await new ProductBuilder(Ref).WithBarcodes(keep, dropped).CreateAsync(admin);

        (await admin.Products.UpdateAsync(product.Id, ProductBuilder.From(Ref, product).WithBarcodes(keep).Build())).ShouldBeOk();

        Assert.False((await LookupAsync(admin, dropped)).Found);
        Assert.Equal(product.Id, (await LookupAsync(admin, keep)).Product!.Id);
    }

    [Fact]
    public async Task A_code_that_is_one_products_sku_and_anothers_barcode_belongs_to_the_sku_whichever_was_created_first()
    {
        var admin = await fixture.AsAsync("Admin");
        var shared = Unique.Sku("Both");
        var barcodeOwner = await new ProductBuilder(Ref).WithBarcodes(shared, Unique.Barcode()).CreateAsync(admin);   // the lower id
        var skuOwner = await new ProductBuilder(Ref).WithSku(shared).CreateAsync(admin);

        Assert.NotEqual(barcodeOwner.Id, skuOwner.Id);   // created in this order; the public ids are random, the internal ids (not on the wire) rise
        Assert.Equal(skuOwner.Id, (await LookupAsync(admin, shared)).Product!.Id);
        Assert.Equal(skuOwner.Id, (await LookupAsync(admin, shared.ToLowerInvariant())).Product!.Id);
        Assert.Equal(barcodeOwner.Id, (await LookupAsync(admin, barcodeOwner.Barcodes[1])).Product!.Id);
    }

    [Fact]
    public async Task The_lookup_never_compares_the_sku_and_the_barcode_in_one_predicate()
    {
        var admin = await fixture.AsAsync("Admin");
        var barcode = Unique.Barcode();
        var product = await new ProductBuilder(Ref).WithBarcodes(barcode).CreateAsync(admin);
        var log = new CommandTextLog();
        using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = admin.AccessToken;

        foreach (var code in new[] { product.Sku, barcode, "no-such-code-" + Guid.NewGuid().ToString("N") })
            Assert.True((await client.Products.LookupAsync(code)).IsSuccess);

        // "[p].[Sku] = @code OR EXISTS (... [Barcode] = @code)" is the shape that walked the table; each code is asked of one column per statement.
        var products = log.Containing("FROM [Product]");
        Assert.NotEmpty(products);
        Assert.All(products, t => Assert.DoesNotMatch(new Regex(@"\[Sku\]\s*=\s*@\w+\s+OR\b", RegexOptions.IgnoreCase), t));
    }
}
