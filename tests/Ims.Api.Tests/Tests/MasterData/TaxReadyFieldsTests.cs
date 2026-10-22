using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Partners;
using Ims.Domain;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Ims.Api.Tests.Tests.MasterData;

/// <summary>
/// PL1.5 acceptance 1 to 4 and 7, what is left after the accounting module was removed (RemoveAccountingModule, part A): the tax-ready fields (<c>hsCode</c>, <c>sstCode</c> on a product,
/// <c>tin</c>, <c>sstRegNo</c> on a partner) can no longer be switched on, so they behave as they did while the module was off. A value is a 400 naming the field, a read leaves the fields out (not
/// even a null), and a save that does not carry them leaves the stored values alone. The tests of the module being on (stored, returned, audited, shaped, cleared) went with the module;
/// part B deletes the fields and this class. Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class TaxReadyFieldsTests(ApiFixture fixture) : IAsyncLifetime
{
    private ApiClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AsAsync(Roles.Admin);

    public Task DisposeAsync() => Task.CompletedTask;

    private static bool Has(ApiResponse response, string property) => JsonDocument.Parse(response.Body).RootElement.TryGetProperty(property, out _);

    private static bool HasInItems(ApiResponse response, string property) =>
        JsonDocument.Parse(response.Body).RootElement.GetProperty("items").EnumerateArray().Any(item => item.TryGetProperty(property, out _));

    private static PartnerSaveRequest Partner(string? tin = null, string? sstRegNo = null, PartnerType type = PartnerType.Customer) =>
        new(type, Unique.Name("Taxed"), "0123", true, tin, sstRegNo);

    /// <summary>A product that holds tax codes the way an installation that had the module on would: written straight to the table, because the API no longer takes them.</summary>
    private async Task<ProductDto> ProductWithStoredCodesAsync()
    {
        var product = (await _admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).Build())).Data<ProductDto>();
        await fixture.Database.ExecuteAsync("UPDATE Product SET HsCode = N'8471.30', SstCode = N'ST-01' WHERE PublicId = @id", ("@id", product.Id));
        return product;
    }

    private async Task<PartnerDto> PartnerWithStoredIdsAsync()
    {
        var partner = (await _admin.Partners.CreateAsync(Partner())).Data<PartnerDto>();
        await fixture.Database.ExecuteAsync("UPDATE Partner SET Tin = N'C2584563200', SstRegNo = N'W10-1808-32000123' WHERE PublicId = @id", ("@id", partner.Id));
        return partner;
    }

    [Fact]
    public async Task A_product_value_is_a_400_naming_the_field_on_create_and_update()
    {
        var existing = (await _admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).Build())).Data<ProductDto>();
        var create = new ProductBuilder(fixture.Reference).WithTaxCodes("8471.30", null).Build();

        var onCreate = (await _admin.Products.CreateAsync(create)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var onUpdate = (await _admin.Products.UpdateAsync(existing.Id, ProductBuilder.From(fixture.Reference, existing).WithTaxCodes(null, "ST-01").Build())).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("not available", onCreate.Detail);
        Assert.Contains("accounting", onCreate.Detail);
        Assert.Equal(["'hsCode' is not available while the accounting module is off."], onCreate.ErrorsFor("hsCode"));
        Assert.NotEmpty(onUpdate.ErrorsFor("sstCode"));
        Assert.Empty(onCreate.ErrorsFor("sstCode"));
        Assert.Empty((await _admin.Products.ListAsync(search: create.Sku)).Data<PagedResult<ProductDto>>().Items); // not created
        Assert.Equal((null, null), await StoredProductAsync(existing.Id));
    }

    [Fact]
    public async Task A_partner_value_is_a_400_naming_the_field_on_create_and_update()
    {
        var existing = (await _admin.Partners.CreateAsync(Partner())).Data<PartnerDto>();
        var create = Partner("C2584563200", "W10");

        var onCreate = (await _admin.Partners.CreateAsync(create)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var onUpdate = (await _admin.Partners.UpdateAsync(existing.Id, new PartnerSaveRequest(existing.Type, existing.Name, existing.Contact, true, null, "W10"))).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("not available", onCreate.Detail);
        Assert.NotEmpty(onCreate.ErrorsFor("tin"));
        Assert.NotEmpty(onCreate.ErrorsFor("sstRegNo"));
        Assert.NotEmpty(onUpdate.ErrorsFor("sstRegNo"));
        Assert.DoesNotContain("C2584563200", onCreate.Detail);
        Assert.Empty((await _admin.Partners.ListAsync(search: create.Name)).Data<PagedResult<PartnerDto>>().Items);
        Assert.Equal((null, null), await StoredPartnerAsync(existing.Id));
    }

    [Fact]
    public async Task A_blank_value_is_nothing_and_is_accepted()
    {
        var request = new ProductBuilder(fixture.Reference).WithTaxCodes("", "  ").Build();

        var product = (await _admin.Products.CreateAsync(request)).Data<ProductDto>();
        (await _admin.Partners.CreateAsync(Partner("", " "))).ShouldBeOk();

        Assert.Equal((null, null), await StoredProductAsync(product.Id));
    }

    [Fact]
    public async Task No_read_returns_the_fields_not_even_as_null_and_the_stored_values_stay()
    {
        var product = await ProductWithStoredCodesAsync();
        var partner = await PartnerWithStoredIdsAsync();

        var got = await _admin.Products.GetAsync(product.Id);
        Assert.False(Has(got, "hsCode"));
        Assert.False(Has(got, "sstCode"));
        Assert.False(HasInItems(await _admin.Products.ListAsync(search: product.Sku), "hsCode"));
        Assert.False(JsonDocument.Parse((await _admin.Products.LookupAsync(product.Sku)).Body).RootElement.GetProperty("product").TryGetProperty("hsCode", out _));
        var partners = await _admin.Partners.ListAsync(search: partner.Name);
        Assert.False(HasInItems(partners, "tin"));
        Assert.False(HasInItems(partners, "sstRegNo"));
        Assert.Equal(("8471.30", "ST-01"), await StoredProductAsync(product.Id)); // hidden, not deleted
        Assert.Equal(("C2584563200", "W10-1808-32000123"), await StoredPartnerAsync(partner.Id));
    }

    [Fact]
    public async Task A_save_that_does_not_carry_the_fields_leaves_the_stored_values_alone()
    {
        var product = await ProductWithStoredCodesAsync();
        var partner = await PartnerWithStoredIdsAsync();

        var savedProduct = (await _admin.Products.UpdateAsync(product.Id, ProductBuilder.From(fixture.Reference, product).WithName(Unique.Name("Renamed")).Build())).Data<ProductDto>();
        var savedPartner = (await _admin.Partners.UpdateAsync(partner.Id, new PartnerSaveRequest(partner.Type, Unique.Name("Renamed"), "0999", true))).Data<PartnerDto>();

        Assert.Null(savedProduct.HsCode);
        Assert.Null(savedPartner.Tin);
        Assert.Equal(("8471.30", "ST-01"), await StoredProductAsync(product.Id));
        Assert.Equal(("C2584563200", "W10-1808-32000123"), await StoredPartnerAsync(partner.Id));
    }

    [Fact]
    public async Task The_create_and_update_replies_leave_the_fields_out_too()
    {
        var product = await ProductWithStoredCodesAsync();

        var reply = await _admin.Products.UpdateAsync(product.Id, ProductBuilder.From(fixture.Reference, product).Build());

        reply.ShouldBeOk();
        Assert.False(Has(reply, "hsCode"));
        Assert.False(Has(reply, "sstCode"));
    }

    [Fact]
    public async Task A_TIN_is_never_written_to_the_application_log_when_the_request_is_refused()
    {
        const string secret = "TINSECRET9988";
        var logs = new CapturedLogs();
        using var host = fixture.CreateHost(b => b.UseSetting("Serilog:MinimumLevel:Default", "Verbose").ConfigureServices(s => s.AddSingleton<ILogEventSink>(logs)));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = _admin.AccessToken;

        (await client.Partners.CreateAsync(Partner(secret))).ShouldBeProblem(HttpStatusCode.BadRequest); // not available

        Assert.Contains(logs.Lines, l => l.Contains("Request rejected")); // the capture sees what the host logs about a rejected request
        Assert.DoesNotContain(logs.Lines, l => l.Contains("TINSECRET", StringComparison.OrdinalIgnoreCase));
    }

    // ----- helpers -----
    private async Task<(string? HsCode, string? SstCode)> StoredProductAsync(Guid id) =>
        (await fixture.Database.ScalarAsync<string?>("SELECT HsCode FROM Product WHERE PublicId = @id", ("@id", id)),
         await fixture.Database.ScalarAsync<string?>("SELECT SstCode FROM Product WHERE PublicId = @id", ("@id", id)));

    private async Task<(string? Tin, string? SstRegNo)> StoredPartnerAsync(Guid id) =>
        (await fixture.Database.ScalarAsync<string?>("SELECT Tin FROM Partner WHERE PublicId = @id", ("@id", id)),
         await fixture.Database.ScalarAsync<string?>("SELECT SstRegNo FROM Partner WHERE PublicId = @id", ("@id", id)));
}
