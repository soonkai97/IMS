using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Partners;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.MasterData;

/// <summary>
/// PL1.5 acceptance 4 as closed by the API follow-up of PL1.8 (ADR modules-and-tracking-dimensions 4.3): a tax-ready key that is NOT in the body keeps the stored value, an explicit null or a blank clears it,
/// a value replaces it, and each key of a pair is decided on its own. The bodies are raw JSON on purpose: a request object cannot show a key that is missing, and a missing key is exactly what a web bundle
/// from before PL1.8, a script or a stale form sends. Real SQL Server; every test starts and ends with no module row.
/// </summary>
[Collection(ApiCollection.Name)]
public class TaxFieldPresenceTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Hs = "8471.30";
    private const string Sst = "ST-01";
    private const string Tin = "C2584563200";
    private const string Reg = "W10-1808-32000123";

    private ApiClient _admin = null!;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _admin = await fixture.AsAsync(Roles.Admin);
    }

    public Task DisposeAsync() => ResetAsync();

    private Task ResetAsync() => fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    private async Task SwitchAsync(bool enabled) =>
        (await _admin.Modules.SwitchAsync(ModuleCodes.Accounting, enabled, $"Tax presence test {Guid.NewGuid():N}"[..24])).ShouldBeOk();

    // ----- bodies -----
    /// <summary>The JSON of a request, with the tax keys taken out (an old client) or set as given: <c>null</c> in the dictionary is a JSON null, a key not listed is absent.</summary>
    private static JsonObject Body(object request, params (string Key, string? Value)[] tax)
    {
        var body = JsonSerializer.SerializeToNode(request, request.GetType(), JsonDefaults.Options)!.AsObject();
        foreach (var key in new[] { "hsCode", "sstCode", "tin", "sstRegNo" }) body.Remove(key);
        foreach (var (key, value) in tax) body[key] = value is null ? null : JsonValue.Create(value);
        return body;
    }

    private ProductSaveRequest Product(string? name = null) => new ProductBuilder(fixture.Reference).WithName(name ?? Unique.Name("Presence")).Build();

    private static PartnerSaveRequest Partner(string? name = null) => new(PartnerType.Customer, name ?? Unique.Name("Presence"), "0123", true);

    private async Task<(ProductDto Dto, ProductSaveRequest Request)> StoredProductAsync()
    {
        var request = new ProductBuilder(fixture.Reference).WithTaxCodes(Hs, Sst).Build();
        return ((await _admin.Products.CreateAsync(request)).Data<ProductDto>(), request);
    }

    private async Task<PartnerDto> StoredPartnerAsync() =>
        (await _admin.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Customer, Unique.Name("Presence"), "0123", true, Tin, Reg))).Data<PartnerDto>();

    private Task<ApiResponse> PutProductAsync(Guid id, JsonObject body) => _admin.PutAsync($"/api/products/{id}", body);

    private Task<ApiResponse> PutPartnerAsync(Guid id, JsonObject body) => _admin.PutAsync($"/api/partners/{id}", body);

    private async Task<(string? Hs, string? Sst)> ProductInDbAsync(Guid id) =>
        (await fixture.Database.ScalarAsync<string?>("SELECT HsCode FROM Product WHERE PublicId = @id", ("@id", id)),
         await fixture.Database.ScalarAsync<string?>("SELECT SstCode FROM Product WHERE PublicId = @id", ("@id", id)));

    private async Task<(string? Tin, string? Reg)> PartnerInDbAsync(Guid id) =>
        (await fixture.Database.ScalarAsync<string?>("SELECT Tin FROM Partner WHERE PublicId = @id", ("@id", id)),
         await fixture.Database.ScalarAsync<string?>("SELECT SstRegNo FROM Partner WHERE PublicId = @id", ("@id", id)));

    private static JsonObject With(JsonObject body, string key, JsonNode? value)
    {
        body[key] = value;
        return body;
    }

    // ----- module on: absent keeps -----
    [Fact]
    public async Task A_save_from_a_client_that_never_sends_the_keys_keeps_the_stored_values_of_a_product_and_a_partner()
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();

        var savedProduct = (await PutProductAsync(product.Id, Body(Product(Unique.Name("Renamed"))))).ShouldBeOk().Read<ProductDto>();
        var savedPartner = (await PutPartnerAsync(partner.Id, Body(Partner(Unique.Name("Renamed"))))).ShouldBeOk().Read<PartnerDto>();

        Assert.Equal((Hs, Sst), (savedProduct.HsCode, savedProduct.SstCode));
        Assert.Equal((Tin, Reg), (savedPartner.Tin, savedPartner.SstRegNo));
        Assert.Equal((Hs, Sst), await ProductInDbAsync(product.Id));
        Assert.Equal((Tin, Reg), await PartnerInDbAsync(partner.Id));
        Assert.NotEqual(product.Name, savedProduct.Name); // the rest of the save did happen
    }

    [Fact]
    public async Task A_request_object_without_the_fields_sends_no_keys_and_keeps_the_stored_values()
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();

        // The convenience constructors of the tests: the short one leaves the fields absent, a null passed as a value is present.
        var asAbsent = new PartnerSaveRequest(partner.Type, partner.Name, "0999", true);
        Assert.False(asAbsent.Tin.IsPresent);
        Assert.False(JsonSerializer.SerializeToNode(asAbsent, JsonDefaults.Options)!.AsObject().ContainsKey("tin"));
        (await _admin.Partners.UpdateAsync(partner.Id, asAbsent)).ShouldBeOk();
        Assert.Equal((Tin, Reg), await PartnerInDbAsync(partner.Id));

        string? none = null;
        var asNull = new PartnerSaveRequest(partner.Type, partner.Name, "0999", true, none, Reg);
        Assert.True(asNull.Tin.IsPresent);
        Assert.True(JsonSerializer.SerializeToNode(asNull, JsonDefaults.Options)!.AsObject().ContainsKey("tin"));
        (await _admin.Partners.UpdateAsync(partner.Id, asNull)).ShouldBeOk();
        Assert.Equal((null, Reg), await PartnerInDbAsync(partner.Id));
        Assert.Equal((Hs, Sst), await ProductInDbAsync(product.Id));
    }

    // ----- module on: null / blank clears -----
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_explicit_null_or_blank_clears_only_the_key_that_was_sent(string? cleared)
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();

        var savedProduct = (await PutProductAsync(product.Id, Body(Product(), ("hsCode", cleared)))).ShouldBeOk().Read<ProductDto>();
        var savedPartner = (await PutPartnerAsync(partner.Id, Body(Partner(), ("sstRegNo", cleared)))).ShouldBeOk().Read<PartnerDto>();

        Assert.Equal((null, Sst), (savedProduct.HsCode, savedProduct.SstCode));
        Assert.Equal((null, Sst), await ProductInDbAsync(product.Id));
        Assert.Equal((Tin, null), (savedPartner.Tin, savedPartner.SstRegNo));
        Assert.Equal((Tin, null), await PartnerInDbAsync(partner.Id));
    }

    [Fact]
    public async Task Both_keys_as_null_clear_both_as_before()
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();

        (await PutProductAsync(product.Id, Body(Product(), ("hsCode", null), ("sstCode", null)))).ShouldBeOk();
        (await PutPartnerAsync(partner.Id, Body(Partner(), ("tin", null), ("sstRegNo", null)))).ShouldBeOk();

        Assert.Equal((null, null), await ProductInDbAsync(product.Id));
        Assert.Equal((null, null), await PartnerInDbAsync(partner.Id));
    }

    // ----- module on: a value replaces, pairs are independent -----
    [Fact]
    public async Task A_value_replaces_its_field_after_trimming_and_leaves_the_other_key_of_the_pair_alone()
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();

        (await PutProductAsync(product.Id, Body(Product(), ("sstCode", "  ST-99 ")))).ShouldBeOk();
        (await PutPartnerAsync(partner.Id, Body(Partner(), ("tin", "C99")))).ShouldBeOk();

        Assert.Equal((Hs, "ST-99"), await ProductInDbAsync(product.Id));
        Assert.Equal(("C99", Reg), await PartnerInDbAsync(partner.Id));
    }

    [Fact]
    public async Task A_malformed_value_in_one_key_is_a_400_naming_it_and_changes_nothing_even_when_the_other_key_is_absent()
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();

        var onProduct = (await PutProductAsync(product.Id, Body(Product(), ("hsCode", "84-71")))).ShouldBeProblem(HttpStatusCode.BadRequest);
        var onPartner = (await PutPartnerAsync(partner.Id, Body(Partner(), ("sstRegNo", "W/1")))).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.NotEmpty(onProduct.ErrorsFor("hsCode"));
        Assert.Empty(onProduct.ErrorsFor("sstCode"));
        Assert.NotEmpty(onPartner.ErrorsFor("sstRegNo"));
        Assert.Equal((Hs, Sst), await ProductInDbAsync(product.Id));
        Assert.Equal((Tin, Reg), await PartnerInDbAsync(partner.Id));
    }

    // ----- module on: create -----
    [Fact]
    public async Task A_create_without_the_keys_stores_none_and_with_a_null_stores_none_and_with_a_value_stores_it()
    {
        await SwitchAsync(true);

        var absent = (await _admin.PostAsync("/api/products", Body(Product()))).ShouldBeOk().Read<ProductDto>();
        var explicitNull = (await _admin.PostAsync("/api/products", Body(Product(), ("hsCode", null), ("sstCode", null)))).ShouldBeOk().Read<ProductDto>();
        var valued = (await _admin.PostAsync("/api/products", Body(Product(), ("hsCode", Hs)))).ShouldBeOk().Read<ProductDto>();
        var partner = (await _admin.PostAsync("/api/partners", Body(Partner(), ("sstRegNo", Reg)))).ShouldBeOk().Read<PartnerDto>();

        Assert.Equal((null, null), await ProductInDbAsync(absent.Id));
        Assert.Equal((null, null), await ProductInDbAsync(explicitNull.Id));
        Assert.Equal((Hs, null), await ProductInDbAsync(valued.Id));
        Assert.Equal((null, Reg), await PartnerInDbAsync(partner.Id));
    }

    // ----- module off -----
    [Fact]
    public async Task With_the_module_off_an_absent_key_or_a_blank_one_is_nothing_and_a_value_is_a_400_naming_that_key()
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();
        await SwitchAsync(false);

        (await PutProductAsync(product.Id, Body(Product()))).ShouldBeOk();                                       // absent
        (await PutProductAsync(product.Id, Body(Product(), ("hsCode", "")))).ShouldBeOk();                        // blank: ignored, not a clear
        (await PutPartnerAsync(partner.Id, Body(Partner(), ("tin", null), ("sstRegNo", "  ")))).ShouldBeOk();    // null and blank
        var onProduct = (await PutProductAsync(product.Id, Body(Product(), ("sstCode", "ST-09")))).ShouldBeProblem(HttpStatusCode.BadRequest);
        var onPartner = (await PutPartnerAsync(partner.Id, Body(Partner(), ("tin", "C99")))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _admin.PostAsync("/api/products", Body(Product()))).ShouldBeOk();
        (await _admin.PostAsync("/api/partners", Body(Partner()))).ShouldBeOk();

        Assert.Equal(["'sstCode' is not available while the accounting module is off."], onProduct.ErrorsFor("sstCode"));
        Assert.Empty(onProduct.ErrorsFor("hsCode"));
        Assert.NotEmpty(onPartner.ErrorsFor("tin"));
        Assert.Equal((Hs, Sst), await ProductInDbAsync(product.Id));
        Assert.Equal((Tin, Reg), await PartnerInDbAsync(partner.Id));
    }

    // ----- audit -----
    [Fact]
    public async Task A_save_that_leaves_the_keys_out_writes_no_audit_row_when_nothing_else_changed_and_never_mentions_the_fields_when_something_did()
    {
        await SwitchAsync(true);
        var (product, request) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();

        (await PutProductAsync(product.Id, Body(request))).ShouldBeOk(); // same values, no tax keys
        (await PutPartnerAsync(partner.Id, Body(new PartnerSaveRequest(partner.Type, partner.Name, partner.Contact, true)))).ShouldBeOk();
        Assert.DoesNotContain(await AuditRowsAsync(nameof(Domain.Product), product.Id), e => e.Action == AuditActions.RowUpdated);
        Assert.DoesNotContain(await AuditRowsAsync(nameof(Domain.Partner), partner.Id), e => e.Action == AuditActions.RowUpdated);

        (await PutProductAsync(product.Id, Body(request with { Name = Unique.Name("Renamed") }))).ShouldBeOk();
        (await PutPartnerAsync(partner.Id, Body(new PartnerSaveRequest(partner.Type, Unique.Name("Renamed"), partner.Contact, true)))).ShouldBeOk();
        var productUpdate = Assert.Single(await AuditRowsAsync(nameof(Domain.Product), product.Id), e => e.Action == AuditActions.RowUpdated);
        var partnerUpdate = Assert.Single(await AuditRowsAsync(nameof(Domain.Partner), partner.Id), e => e.Action == AuditActions.RowUpdated);
        Assert.All(new[] { productUpdate.Before, productUpdate.After }, json => Assert.DoesNotContain("HsCode", json));
        Assert.All(new[] { partnerUpdate.Before, partnerUpdate.After }, json => Assert.DoesNotContain("Tin", json));
        Assert.Equal((Hs, Sst), await ProductInDbAsync(product.Id));
    }

    // ----- wrong types -----
    [Theory]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[\"8471.30\"]")]
    public async Task A_value_of_the_wrong_type_is_a_400_not_a_500_and_changes_nothing(string json)
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();
        var node = JsonNode.Parse(json);

        var onProduct = await PutProductAsync(product.Id, With(Body(Product()), "hsCode", node));
        var onPartner = await PutPartnerAsync(partner.Id, With(Body(Partner()), "tin", JsonNode.Parse(json)));
        var onCreate = await _admin.PostAsync("/api/partners", With(Body(Partner()), "sstRegNo", JsonNode.Parse(json)));

        onProduct.ShouldBeProblem(HttpStatusCode.BadRequest);
        onPartner.ShouldBeProblem(HttpStatusCode.BadRequest);
        onCreate.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal((Hs, Sst), await ProductInDbAsync(product.Id));
        Assert.Equal((Tin, Reg), await PartnerInDbAsync(partner.Id));
    }

    private async Task<List<AuditEntry>> AuditRowsAsync(string entityType, Guid id) =>
        (await _admin.Audit.QueryAsync(entityType: entityType, entityId: id.ToString())).Data<AuditPage>().Items.AsEnumerable().Reverse().ToList();

    // ----- how the key is spelled -----
    private Task<ApiResponse> PutRawAsync(string url, JsonObject body, string extraMembers) =>
        _admin.SendAsync(HttpMethod.Put, url, customize: r => r.Content = new StringContent(body.ToJsonString().TrimEnd('}') + "," + extraMembers + "}", System.Text.Encoding.UTF8, "application/json"));

    [Theory]
    [InlineData("\"HsCode\":\"9999\"", "9999", Sst)]                                  // PascalCase binds like camelCase: present
    [InlineData("\"hsCode\":\"1111\",\"hsCode\":\"2222\"", "2222", Sst)]              // a duplicate key: the last one wins
    [InlineData("\"hs_code\":\"9999\",\"hscode \":\"9999\"", Hs, Sst)]                 // a misspelt key is no key: the stored value is kept
    public async Task The_spelling_of_a_key_decides_whether_it_is_present(string members, string expectedHs, string expectedSst)
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();

        (await PutRawAsync($"/api/products/{product.Id}", Body(Product()), members)).ShouldBeOk();

        Assert.Equal((expectedHs, expectedSst), await ProductInDbAsync(product.Id));
    }

    // ----- a stored value that no longer passes the rules -----
    [Fact]
    public async Task A_stored_value_that_breaks_the_current_shape_does_not_stop_a_save_that_never_sent_the_key()
    {
        await SwitchAsync(true);
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();
        // Written by something other than the API (a script, or rules that were tighter or looser once).
        await fixture.Database.ExecuteAsync("UPDATE Product SET HsCode = N'84-71', SstCode = N'ST_01' WHERE PublicId = @id", ("@id", product.Id));
        await fixture.Database.ExecuteAsync("UPDATE Partner SET Tin = N'C-1', SstRegNo = N'W/1' WHERE PublicId = @id", ("@id", partner.Id));

        (await PutProductAsync(product.Id, Body(Product(Unique.Name("Renamed"))))).ShouldBeOk();
        (await PutPartnerAsync(partner.Id, Body(Partner(Unique.Name("Renamed"))))).ShouldBeOk();

        Assert.Equal(("84-71", "ST_01"), await ProductInDbAsync(product.Id));
        Assert.Equal(("C-1", "W/1"), await PartnerInDbAsync(partner.Id));
        // A key that IS sent is still checked, and one valid key does not excuse the other being sent bad.
        (await PutProductAsync(product.Id, Body(Product(), ("hsCode", "84-71")))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await PutProductAsync(product.Id, Body(Product(), ("hsCode", Hs)))).ShouldBeOk();
        Assert.Equal((Hs, "ST_01"), await ProductInDbAsync(product.Id));
    }
}
