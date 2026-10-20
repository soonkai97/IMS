using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Partners;
using Ims.Domain;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Ims.Api.Tests.Tests.MasterData;

/// <summary>
/// PL1.5 acceptance 1 to 4 and 7: the tax-ready fields (<c>hsCode</c>, <c>sstCode</c> on a product, <c>tin</c>, <c>sstRegNo</c> on a partner) are accepted and returned only while the accounting
/// module is on (ADR modules-and-tracking-dimensions 4.3). Off: a value is a 400 naming the field, a read leaves the fields out (not even a null), and a save that does not carry them leaves
/// the stored values alone, so switching the module off and on again loses nothing. On: a save replaces both fields, so null (or blank) clears. Real SQL Server; every test starts and ends
/// with no module row.
/// </summary>
[Collection(ApiCollection.Name)]
public class TaxReadyFieldsTests(ApiFixture fixture) : IAsyncLifetime
{
    private ApiClient _admin = null!;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _admin = await fixture.AsAsync(Roles.Admin);
    }

    public Task DisposeAsync() => ResetAsync();

    private Task ResetAsync() => fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    private async Task SwitchAsync(bool enabled) =>
        (await _admin.Modules.SwitchAsync(ModuleCodes.Accounting, enabled, $"Tax fields test {Guid.NewGuid():N}"[..24])).ShouldBeOk();

    private static bool Has(ApiResponse response, string property) => JsonDocument.Parse(response.Body).RootElement.TryGetProperty(property, out _);

    private static bool HasInItems(ApiResponse response, string property) =>
        JsonDocument.Parse(response.Body).RootElement.GetProperty("items").EnumerateArray().Any(item => item.TryGetProperty(property, out _));

    private static PartnerSaveRequest Partner(string? tin = null, string? sstRegNo = null, PartnerType type = PartnerType.Customer) =>
        new(type, Unique.Name("Taxed"), "0123", true, tin, sstRegNo);

    private async Task<ProductDto> ProductWithCodesAsync() =>
        (await _admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithTaxCodes("8471.30", "ST-01").Build())).Data<ProductDto>();

    private async Task<PartnerDto> PartnerWithIdsAsync() =>
        (await _admin.Partners.CreateAsync(Partner("C2584563200", "W10-1808-32000123"))).Data<PartnerDto>();

    // ----- on: stored, returned, audited -----
    [Fact]
    public async Task With_the_module_on_a_product_keeps_its_codes_and_every_read_returns_them()
    {
        await SwitchAsync(true);
        var created = await ProductWithCodesAsync();

        Assert.Equal(("8471.30", "ST-01"), (created.HsCode, created.SstCode));
        var got = (await _admin.Products.GetAsync(created.Id)).Data<ProductDto>();
        var listed = (await _admin.Products.ListAsync(search: created.Sku)).Data<PagedResult<ProductDto>>().Items.Single(p => p.Id == created.Id);
        var found = await _admin.Products.FindAsync(created.Sku);
        Assert.All(new[] { got, listed, found }, p => Assert.Equal(("8471.30", "ST-01"), (p.HsCode, p.SstCode)));
        Assert.Equal(("8471.30", "ST-01"), await StoredProductAsync(created.Id));
    }

    [Fact]
    public async Task With_the_module_on_a_partner_keeps_its_identifiers_and_every_read_returns_them()
    {
        await SwitchAsync(true);
        var created = await PartnerWithIdsAsync();

        Assert.Equal(("C2584563200", "W10-1808-32000123"), (created.Tin, created.SstRegNo));
        var listed = (await _admin.Partners.ListAsync(search: created.Name)).Data<PagedResult<PartnerDto>>().Items.Single(p => p.Id == created.Id);
        Assert.Equal(("C2584563200", "W10-1808-32000123"), (listed.Tin, listed.SstRegNo));
    }

    [Fact]
    public async Task Values_are_trimmed_and_a_blank_value_is_stored_as_none()
    {
        await SwitchAsync(true);
        var product = (await _admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithTaxCodes("  8471.30  ", "   ").Build())).Data<ProductDto>();
        var partner = (await _admin.Partners.CreateAsync(Partner(" C2584563200 ", ""))).Data<PartnerDto>();

        Assert.Equal(("8471.30", null), (product.HsCode, product.SstCode));
        Assert.Equal(("8471.30", null), await StoredProductAsync(product.Id));
        Assert.Equal(("C2584563200", null), (partner.Tin, partner.SstRegNo));
    }

    [Fact]
    public async Task The_row_audit_shows_the_old_and_new_values_of_the_tax_fields()
    {
        await SwitchAsync(true);
        var product = await ProductWithCodesAsync();
        (await _admin.Products.UpdateAsync(product.Id, ProductBuilder.From(fixture.Reference, product).WithTaxCodes("9999", "ST-02").Build())).ShouldBeOk();
        var partner = await PartnerWithIdsAsync();
        (await _admin.Partners.UpdateAsync(partner.Id, Partner("C99", "W99") with { Name = partner.Name })).ShouldBeOk();

        var productRows = await AuditRowsAsync(nameof(Product), product.Id);
        var created = Assert.Single(productRows, e => e.Action == AuditActions.RowCreated);
        Assert.Contains("\"HsCode\":\"8471.30\"", created.After);
        var updated = Assert.Single(productRows, e => e.Action == AuditActions.RowUpdated);
        Assert.Contains("\"HsCode\":\"8471.30\"", updated.Before);
        Assert.Contains("\"SstCode\":\"ST-01\"", updated.Before);
        Assert.Contains("\"HsCode\":\"9999\"", updated.After);
        Assert.Contains("\"SstCode\":\"ST-02\"", updated.After);

        var partnerUpdate = Assert.Single(await AuditRowsAsync(nameof(Domain.Partner), partner.Id), e => e.Action == AuditActions.RowUpdated);
        Assert.Contains("\"Tin\":\"C2584563200\"", partnerUpdate.Before);
        Assert.Contains("\"Tin\":\"C99\"", partnerUpdate.After);
        Assert.Contains("\"SstRegNo\":\"W99\"", partnerUpdate.After);
    }

    // ----- shape -----
    [Theory]
    [InlineData("hsCode", "84-71")]
    [InlineData("hsCode", "84A71")]
    [InlineData("hsCode", "12345678901234567")]
    [InlineData("sstCode", "ST_01")]
    [InlineData("sstCode", "123456789012345678901")]
    public async Task A_malformed_product_value_is_a_400_naming_the_field_and_nothing_is_saved(string field, string value)
    {
        await SwitchAsync(true);
        var builder = new ProductBuilder(fixture.Reference);
        var request = field == "hsCode" ? builder.WithTaxCodes(value, null).Build() : builder.WithTaxCodes(null, value).Build();

        var problem = (await _admin.Products.CreateAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.NotEmpty(problem.ErrorsFor(field));
        Assert.DoesNotContain(value, problem.Detail);
        Assert.DoesNotContain(value, string.Join(' ', problem.ErrorsFor(field)));
        Assert.Empty((await _admin.Products.ListAsync(search: request.Sku)).Data<PagedResult<ProductDto>>().Items);
    }

    [Theory]
    [InlineData("tin", "C-258")]
    [InlineData("tin", "C 258")]
    [InlineData("tin", "123456789012345678901")]
    [InlineData("sstRegNo", "W10/1808")]
    [InlineData("sstRegNo", "1234567890123456789012345678901")]
    public async Task A_malformed_partner_value_is_a_400_naming_the_field_and_nothing_is_saved(string field, string value)
    {
        await SwitchAsync(true);
        var request = field == "tin" ? Partner(value) : Partner(null, value);

        var problem = (await _admin.Partners.CreateAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.NotEmpty(problem.ErrorsFor(field));
        Assert.DoesNotContain(value, problem.Detail); // the reason of a rejected request is logged, and a TIN can be a person's
        Assert.Empty((await _admin.Partners.ListAsync(search: request.Name)).Data<PagedResult<PartnerDto>>().Items);
    }

    [Fact]
    public async Task A_malformed_value_on_an_update_leaves_the_stored_ones()
    {
        await SwitchAsync(true);
        var product = await ProductWithCodesAsync();

        (await _admin.Products.UpdateAsync(product.Id, ProductBuilder.From(fixture.Reference, product).WithTaxCodes("8471.30", "ST/9").Build())).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(("8471.30", "ST-01"), await StoredProductAsync(product.Id));
    }

    [Fact]
    public async Task A_TIN_is_never_written_to_the_application_log_whether_it_is_rejected_stored_or_refused_by_a_switched_off_module()
    {
        const string secret = "TINSECRET9988";
        var logs = new CapturedLogs();
        using var host = fixture.CreateHost(b => b.UseSetting("Serilog:MinimumLevel:Default", "Verbose").ConfigureServices(s => s.AddSingleton<ILogEventSink>(logs)));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = _admin.AccessToken;

        (await client.Partners.CreateAsync(Partner(secret))).ShouldBeProblem(HttpStatusCode.BadRequest);   // module off: not available
        await SwitchAsync(true);
        (await client.Partners.CreateAsync(Partner(secret + "-X", secret + "/Y"))).ShouldBeProblem(HttpStatusCode.BadRequest); // malformed
        var stored = (await client.Partners.CreateAsync(Partner(secret, "W-" + secret))).Data<PartnerDto>();           // accepted
        (await client.Partners.UpdateAsync(stored.Id, Partner(secret + "!", null) with { Name = stored.Name })).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains(logs.Lines, l => l.Contains("Request rejected")); // the capture sees what the host logs about a rejected request
        Assert.DoesNotContain(logs.Lines, l => l.Contains("TINSECRET", StringComparison.OrdinalIgnoreCase));
    }

    // ----- on: null clears -----
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task With_the_module_on_a_save_without_a_value_clears_it(string? cleared)
    {
        await SwitchAsync(true);
        var product = await ProductWithCodesAsync();
        var partner = await PartnerWithIdsAsync();

        var savedProduct = (await _admin.Products.UpdateAsync(product.Id, ProductBuilder.From(fixture.Reference, product).WithTaxCodes(cleared, "ST-01").Build())).Data<ProductDto>();
        var savedPartner = (await _admin.Partners.UpdateAsync(partner.Id, new PartnerSaveRequest(partner.Type, partner.Name, partner.Contact, true, cleared, "W10-1808-32000123"))).Data<PartnerDto>();

        Assert.Equal((null, "ST-01"), (savedProduct.HsCode, savedProduct.SstCode));
        Assert.Equal((null, "ST-01"), await StoredProductAsync(product.Id));
        Assert.Equal((null, "W10-1808-32000123"), (savedPartner.Tin, savedPartner.SstRegNo));
        Assert.Equal((null, "W10-1808-32000123"), await StoredPartnerAsync(partner.Id));
    }

    [Fact]
    public async Task A_response_without_a_value_leaves_the_property_out_even_with_the_module_on()
    {
        await SwitchAsync(true);
        var product = (await _admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).Build())).ShouldBeOk();

        Assert.False(Has(product, "hsCode"));
        Assert.False(Has(product, "sstCode"));
    }

    // ----- off -----
    [Fact]
    public async Task With_the_module_off_a_product_value_is_a_400_naming_the_field_on_create_and_update()
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
    public async Task With_the_module_off_a_partner_value_is_a_400_naming_the_field_on_create_and_update()
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
    public async Task With_the_module_off_a_blank_value_is_nothing_and_is_accepted()
    {
        var request = new ProductBuilder(fixture.Reference).WithTaxCodes("", "  ").Build();

        var product = (await _admin.Products.CreateAsync(request)).Data<ProductDto>();
        (await _admin.Partners.CreateAsync(Partner("", " "))).ShouldBeOk();

        Assert.Equal((null, null), await StoredProductAsync(product.Id));
    }

    [Fact]
    public async Task With_the_module_off_no_read_returns_the_fields_not_even_as_null_and_the_stored_values_stay()
    {
        await SwitchAsync(true);
        var product = await ProductWithCodesAsync();
        var partner = await PartnerWithIdsAsync();

        await SwitchAsync(false);

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
    public async Task With_the_module_off_a_save_that_does_not_carry_the_fields_leaves_the_stored_values_alone_and_they_come_back_when_it_is_turned_on()
    {
        await SwitchAsync(true);
        var product = await ProductWithCodesAsync();
        var partner = await PartnerWithIdsAsync();
        await SwitchAsync(false);

        var savedProduct = (await _admin.Products.UpdateAsync(product.Id, ProductBuilder.From(fixture.Reference, product with { HsCode = null, SstCode = null }).WithName(Unique.Name("Renamed")).Build())).Data<ProductDto>();
        var savedPartner = (await _admin.Partners.UpdateAsync(partner.Id, new PartnerSaveRequest(partner.Type, Unique.Name("Renamed"), "0999", true))).Data<PartnerDto>();

        Assert.Null(savedProduct.HsCode);
        Assert.Null(savedPartner.Tin);
        Assert.Equal(("8471.30", "ST-01"), await StoredProductAsync(product.Id));
        Assert.Equal(("C2584563200", "W10-1808-32000123"), await StoredPartnerAsync(partner.Id));

        await SwitchAsync(true);

        var back = (await _admin.Products.GetAsync(product.Id)).Data<ProductDto>();
        Assert.Equal(("8471.30", "ST-01"), (back.HsCode, back.SstCode));
        var backPartner = (await _admin.Partners.ListAsync(search: savedPartner.Name)).Data<PagedResult<PartnerDto>>().Items.Single(p => p.Id == partner.Id);
        Assert.Equal(("C2584563200", "W10-1808-32000123"), (backPartner.Tin, backPartner.SstRegNo));
    }

    [Fact]
    public async Task With_the_module_off_the_create_and_update_replies_leave_the_fields_out_too()
    {
        await SwitchAsync(true);
        var product = await ProductWithCodesAsync();
        await SwitchAsync(false);

        var reply = await _admin.Products.UpdateAsync(product.Id, ProductBuilder.From(fixture.Reference, product with { HsCode = null, SstCode = null }).Build());

        reply.ShouldBeOk();
        Assert.False(Has(reply, "hsCode"));
        Assert.False(Has(reply, "sstCode"));
    }

    // ----- helpers -----
    private async Task<(string? HsCode, string? SstCode)> StoredProductAsync(Guid id) =>
        (await fixture.Database.ScalarAsync<string?>("SELECT HsCode FROM Product WHERE PublicId = @id", ("@id", id)),
         await fixture.Database.ScalarAsync<string?>("SELECT SstCode FROM Product WHERE PublicId = @id", ("@id", id)));

    private async Task<(string? Tin, string? SstRegNo)> StoredPartnerAsync(Guid id) =>
        (await fixture.Database.ScalarAsync<string?>("SELECT Tin FROM Partner WHERE PublicId = @id", ("@id", id)),
         await fixture.Database.ScalarAsync<string?>("SELECT SstRegNo FROM Partner WHERE PublicId = @id", ("@id", id)));

    private async Task<List<AuditEntry>> AuditRowsAsync(string entityType, Guid id) =>
        (await _admin.Audit.QueryAsync(entityType: entityType, entityId: id.ToString())).Data<AuditPage>().Items.AsEnumerable().Reverse().ToList();
}
