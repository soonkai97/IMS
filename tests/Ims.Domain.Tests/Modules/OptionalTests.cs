using System.Text.Json;
using System.Text.Json.Nodes;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Partners;

namespace Ims.Domain.Tests.Modules;

/// <summary>
/// <see cref="Optional{T}"/> tells "the key was not sent" from "the key was sent as null" (PL1.8 API follow-up), on the wire and in the validators that run on a request. A value of the wrong type is a
/// <see cref="JsonException"/> (the API answers it with a 400), never another exception.
/// </summary>
public class OptionalTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly string Partner = """{"type":"Customer","name":"Acme","contact":null,"isActive":true""";

    private static PartnerSaveRequest ReadPartner(string tail) =>
        JsonSerializer.Deserialize<PartnerSaveRequest>(Partner + tail + "}", Web)!;

    private static ProductSaveRequest ReadProduct(string tail) =>
        JsonSerializer.Deserialize<ProductSaveRequest>($$"""{"sku":"S","name":"N","categoryId":null,"baseUomId":"{{Guid.NewGuid()}}","safetyStock":0,"isActive":true,"barcodes":[],"conversions":[]{{tail}}}""", Web)!;

    // ----- the type -----
    [Fact]
    public void Default_is_absent_and_a_null_is_present()
    {
        Optional<string> absent = default;
        Optional<string> none = new((string?)null);
        string? alsoNone = null;
        Optional<string> implicitNull = alsoNone;

        Assert.False(absent.IsPresent);
        Assert.Null(absent.Value);
        Assert.True(none.IsPresent);
        Assert.True(implicitNull.IsPresent);
        Assert.NotEqual(absent, none);
    }

    [Fact]
    public void Or_keeps_the_stored_value_only_when_the_field_is_absent()
    {
        Assert.Equal("stored", default(Optional<string>).Or("stored"));
        Assert.Null(new Optional<string>(null).Or("stored"));
        Assert.Equal("", new Optional<string>("").Or("stored"));
        Assert.Equal("new", ((Optional<string>)"new").Or("stored"));
    }

    // ----- reading -----
    [Fact]
    public void A_missing_key_is_absent_for_all_four_fields()
    {
        var partner = ReadPartner("");
        var product = ReadProduct("");

        Assert.All(new[] { partner.Tin, partner.SstRegNo, product.HsCode, product.SstCode }, f => Assert.False(f.IsPresent));
    }

    [Theory]
    [InlineData("null", null)]
    [InlineData("\"\"", "")]
    [InlineData("\"   \"", "   ")]
    [InlineData("\"C2584563200\"", "C2584563200")]
    public void A_key_that_is_there_is_present_with_the_value_as_sent_untrimmed(string json, string? expected)
    {
        var partner = ReadPartner($",\"tin\":{json}");
        var product = ReadProduct($",\"hsCode\":{json}");

        Assert.True(partner.Tin.IsPresent);
        Assert.Equal(expected, partner.Tin.Value);
        Assert.False(partner.SstRegNo.IsPresent);
        Assert.True(product.HsCode.IsPresent);
        Assert.Equal(expected, product.HsCode.Value);
        Assert.False(product.SstCode.IsPresent);
    }

    [Fact]
    public void Each_key_of_a_pair_is_decided_on_its_own()
    {
        var partner = ReadPartner(",\"sstRegNo\":null");

        Assert.False(partner.Tin.IsPresent);
        Assert.True(partner.SstRegNo.IsPresent);
        Assert.Null(partner.SstRegNo.Value);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1.5")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("{\"a\":1}")]
    [InlineData("[]")]
    [InlineData("[\"8471.30\"]")]
    public void A_value_of_the_wrong_type_is_a_JsonException_not_anything_else(string json)
    {
        Assert.Throws<JsonException>(() => ReadPartner($",\"tin\":{json}"));
        Assert.Throws<JsonException>(() => ReadProduct($",\"sstCode\":{json}"));
    }

    // ----- writing -----
    [Fact]
    public void An_absent_field_is_not_written_and_a_present_null_is()
    {
        var absent = JsonSerializer.SerializeToNode(new PartnerSaveRequest(PartnerType.Customer, "Acme", null, true), Web)!.AsObject();
        var mixed = JsonSerializer.SerializeToNode(new PartnerSaveRequest(PartnerType.Customer, "Acme", null, true, (string?)null, "W10"), Web)!.AsObject();

        Assert.False(absent.ContainsKey("tin"));
        Assert.False(absent.ContainsKey("sstRegNo"));
        Assert.True(mixed.ContainsKey("tin"));
        Assert.Null(mixed["tin"]);
        Assert.Equal("W10", (string?)mixed["sstRegNo"]);
    }

    [Fact]
    public void A_request_survives_a_round_trip_with_its_presence()
    {
        var sent = new ProductSaveRequest("S", "N", null, Guid.NewGuid(), 0, true, [], [], (string?)null, "ST-01");

        var back = JsonSerializer.Deserialize<ProductSaveRequest>(JsonSerializer.Serialize(sent, Web), Web)!;

        Assert.True(back.HsCode.IsPresent);
        Assert.Null(back.HsCode.Value);
        Assert.Equal("ST-01", back.SstCode.Value);
        Assert.Equal(sent.HsCode, back.HsCode);
    }

    // ----- validators see the present value only -----
    [Fact]
    public void The_validators_pass_an_absent_field_and_check_a_present_one_under_the_name_of_the_field()
    {
        var product = new ProductSaveRequestValidator();
        var partner = new PartnerSaveRequestValidator();

        Assert.True(partner.Validate(ReadPartner("")).IsValid);
        Assert.True(product.Validate(ReadProduct("")).IsValid);
        Assert.True(partner.Validate(ReadPartner(",\"tin\":null,\"sstRegNo\":\"   \"")).IsValid);
        Assert.True(product.Validate(ReadProduct(",\"hsCode\":\"8471.30\",\"sstCode\":\"\"")).IsValid);

        Assert.Equal(["Tin"], partner.Validate(ReadPartner(",\"tin\":\"C-1\"")).Errors.Select(e => e.PropertyName));
        Assert.Equal(["SstRegNo"], partner.Validate(ReadPartner(",\"sstRegNo\":\"W/1\"")).Errors.Select(e => e.PropertyName));
        Assert.Equal(["HsCode"], product.Validate(ReadProduct(",\"hsCode\":\"84-71\"")).Errors.Select(e => e.PropertyName));
        Assert.Equal(["SstCode"], product.Validate(ReadProduct(",\"sstCode\":\"ST_1\"")).Errors.Select(e => e.PropertyName));
    }
}
