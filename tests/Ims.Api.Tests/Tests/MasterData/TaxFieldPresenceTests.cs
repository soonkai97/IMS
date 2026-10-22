using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Partners;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.MasterData;

/// <summary>
/// PL1.5 acceptance 4 as closed by the API follow-up of PL1.8 (ADR modules-and-tracking-dimensions 4.3), what is left after the accounting module was removed (RemoveAccountingModule, part A): a
/// tax-ready key that is NOT in the body keeps the stored value, a blank or null one is nothing, and a value is a 400 naming the key (there is no module to switch the fields on). The bodies are raw
/// JSON on purpose: a request object cannot show a key that is missing, and a missing key is exactly what a web bundle from before PL1.8, a script or a stale form sends. The tests of the module
/// being on (null clears, a value replaces, create stores) went with the module; part B deletes the fields, the presence machinery and this class. Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class TaxFieldPresenceTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Hs = "8471.30";
    private const string Sst = "ST-01";
    private const string Tin = "C2584563200";
    private const string Reg = "W10-1808-32000123";

    private ApiClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AsAsync(Roles.Admin);

    public Task DisposeAsync() => Task.CompletedTask;

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

    /// <summary>A product that holds tax codes the way an installation that had the module on would: written straight to the table, because the API no longer takes them.</summary>
    private async Task<(ProductDto Dto, ProductSaveRequest Request)> StoredProductAsync()
    {
        var request = new ProductBuilder(fixture.Reference).Build();
        var dto = (await _admin.Products.CreateAsync(request)).Data<ProductDto>();
        await fixture.Database.ExecuteAsync("UPDATE Product SET HsCode = @h, SstCode = @s WHERE PublicId = @id", ("@h", Hs), ("@s", Sst), ("@id", dto.Id));
        return (dto, request);
    }

    private async Task<PartnerDto> StoredPartnerAsync()
    {
        var partner = (await _admin.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Customer, Unique.Name("Presence"), "0123", true))).Data<PartnerDto>();
        await fixture.Database.ExecuteAsync("UPDATE Partner SET Tin = @t, SstRegNo = @r WHERE PublicId = @id", ("@t", Tin), ("@r", Reg), ("@id", partner.Id));
        return partner;
    }

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

    // ----- absent keeps -----
    [Fact]
    public async Task A_save_from_a_client_that_never_sends_the_keys_keeps_the_stored_values_of_a_product_and_a_partner()
    {
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();

        var savedProduct = (await PutProductAsync(product.Id, Body(Product(Unique.Name("Renamed"))))).ShouldBeOk().Read<ProductDto>();
        (await PutPartnerAsync(partner.Id, Body(Partner(Unique.Name("Renamed"))))).ShouldBeOk();

        Assert.Equal((Hs, Sst), await ProductInDbAsync(product.Id));
        Assert.Equal((Tin, Reg), await PartnerInDbAsync(partner.Id));
        Assert.NotEqual(product.Name, savedProduct.Name); // the rest of the save did happen
    }

    [Fact]
    public async Task A_request_object_without_the_fields_sends_no_keys_and_keeps_the_stored_values()
    {
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
        Assert.Equal((Hs, Sst), await ProductInDbAsync(product.Id));
    }

    // ----- a null or blank is nothing, a value is a 400 naming that key -----
    [Fact]
    public async Task An_absent_key_or_a_blank_one_is_nothing_and_a_value_is_a_400_naming_that_key()
    {
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();

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

    // A key that binds is present, and a present value is a 400 now; a key that does not bind is no key at all, and the save goes through.
    [Theory]
    [InlineData("\"HsCode\":\"9999\"", HttpStatusCode.BadRequest)]                             // PascalCase binds like camelCase: present
    [InlineData("\"hsCode\":\"1111\",\"hsCode\":\"2222\"", HttpStatusCode.BadRequest)]         // a duplicate key: the last one wins, and it is present
    [InlineData("\"hs_code\":\"9999\",\"hscode \":\"9999\"", HttpStatusCode.OK)]                // a misspelt key is no key: the stored value is kept
    public async Task The_spelling_of_a_key_decides_whether_it_is_present(string members, HttpStatusCode expected)
    {
        var (product, _) = await StoredProductAsync();

        var answer = await PutRawAsync($"/api/products/{product.Id}", Body(Product()), members);

        answer.ShouldBe(expected);
        Assert.Equal((Hs, Sst), await ProductInDbAsync(product.Id));
    }

    // ----- a stored value that no longer passes the rules -----
    [Fact]
    public async Task A_stored_value_that_breaks_the_current_shape_does_not_stop_a_save_that_never_sent_the_key()
    {
        var (product, _) = await StoredProductAsync();
        var partner = await StoredPartnerAsync();
        // Written by something other than the API (a script, or rules that were tighter or looser once).
        await fixture.Database.ExecuteAsync("UPDATE Product SET HsCode = N'84-71', SstCode = N'ST_01' WHERE PublicId = @id", ("@id", product.Id));
        await fixture.Database.ExecuteAsync("UPDATE Partner SET Tin = N'C-1', SstRegNo = N'W/1' WHERE PublicId = @id", ("@id", partner.Id));

        (await PutProductAsync(product.Id, Body(Product(Unique.Name("Renamed"))))).ShouldBeOk();
        (await PutPartnerAsync(partner.Id, Body(Partner(Unique.Name("Renamed"))))).ShouldBeOk();

        Assert.Equal(("84-71", "ST_01"), await ProductInDbAsync(product.Id));
        Assert.Equal(("C-1", "W/1"), await PartnerInDbAsync(partner.Id));
    }
}
