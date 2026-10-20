using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Modules;

/// <summary>PL1.5: the shape rules of the tax-ready fields (characters and length, trimming, blank means none) and the two aggregates that hold them. No checksum, no lookup, no case change.</summary>
public class TaxFieldShapesTests
{
    [Fact]
    public void A_typical_value_passes_and_nothing_is_always_fine()
    {
        Assert.Null(TaxFieldShapes.HsCodeError("8471.30"));
        Assert.Null(TaxFieldShapes.SstCodeError("SST-01a"));
        Assert.Null(TaxFieldShapes.TinError("C2584563200"));
        Assert.Null(TaxFieldShapes.SstRegNoError("W10-1808-32000123"));
        Assert.All(new Func<string?, string?>[] { TaxFieldShapes.HsCodeError, TaxFieldShapes.SstCodeError, TaxFieldShapes.TinError, TaxFieldShapes.SstRegNoError }, error => Assert.Null(error(null)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  8471.30 ", "8471.30")]
    [InlineData("abc", "abc")] // case is kept as typed
    public void Clean_trims_the_ends_and_turns_blank_into_none(string? typed, string? stored) => Assert.Equal(stored, TaxFieldShapes.Clean(typed));

    [Theory]
    [InlineData("8471.30", true)]
    [InlineData("0101", true)]
    [InlineData("8471.30.00.00", true)]      // dots anywhere: shape only
    [InlineData("1234567890123456", true)]   // 16 characters
    [InlineData("12345678901234567", false)] // 17
    [InlineData("84-71", false)]             // hyphen is not allowed in an HS code
    [InlineData("84A71", false)]             // letters are not allowed
    [InlineData("84 71", false)]             // inner space
    [InlineData("８４７１", false)]           // full-width digits are not ASCII digits
    public void HsCode_is_digits_and_dots_up_to_16(string clean, bool ok) => Assert.Equal(ok, TaxFieldShapes.HsCodeError(clean) is null);

    [Theory]
    [InlineData("ST-01", true)]
    [InlineData("a1-B2", true)]
    [InlineData("12345678901234567890", true)]   // 20
    [InlineData("123456789012345678901", false)] // 21
    [InlineData("ST_01", false)]
    [InlineData("ST.01", false)]
    [InlineData("ST 01", false)]
    [InlineData("É01", false)]                   // a letter outside A-Z
    public void SstCode_is_letters_digits_and_hyphen_up_to_20(string clean, bool ok) => Assert.Equal(ok, TaxFieldShapes.SstCodeError(clean) is null);

    [Theory]
    [InlineData("C2584563200", true)]
    [InlineData("IG12345678090", true)]
    [InlineData("12345678901234567890", true)]   // 20
    [InlineData("123456789012345678901", false)] // 21
    [InlineData("C25-84563", false)]             // no hyphen in a TIN
    [InlineData("C2584 563", false)]
    [InlineData("C2584.563", false)]
    public void Tin_is_letters_and_digits_up_to_20(string clean, bool ok) => Assert.Equal(ok, TaxFieldShapes.TinError(clean) is null);

    [Theory]
    [InlineData("W10-1808-32000123", true)]
    [InlineData("123456789012345678901234567890", true)]   // 30
    [InlineData("1234567890123456789012345678901", false)] // 31
    [InlineData("W10/1808", false)]
    [InlineData("W10_1808", false)]
    public void SstRegNo_is_letters_digits_and_hyphen_up_to_30(string clean, bool ok) => Assert.Equal(ok, TaxFieldShapes.SstRegNoError(clean) is null);

    [Fact]
    public void No_message_repeats_the_value_because_a_TIN_can_be_a_persons_and_the_reason_of_a_rejected_request_is_logged()
    {
        const string secret = "SECRET-TIN-9988!";

        Assert.DoesNotContain(secret, TaxFieldShapes.TinError(secret));
        Assert.DoesNotContain("9988", TaxFieldShapes.TinError(secret));
        Assert.DoesNotContain(secret, TaxFieldShapes.SstRegNoError(secret + secret));
        Assert.DoesNotContain(secret, Assert.Throws<DomainException>(() => Partner.Create(PartnerType.Customer, "P").SetTaxIdentifiers(secret, null)).Message);
    }

    // ----- Product -----
    [Fact]
    public void A_new_product_has_no_tax_codes()
    {
        var product = Product.Create("SKU-1", "Item", null, NewUom(), 0m);

        Assert.Null(product.HsCode);
        Assert.Null(product.SstCode);
    }

    [Fact]
    public void SetTaxCodes_stores_the_trimmed_values_and_blank_clears()
    {
        var product = Product.Create("SKU-1", "Item", null, NewUom(), 0m);

        product.SetTaxCodes(" 8471.30 ", "  ST-01");
        Assert.Equal(("8471.30", "ST-01"), (product.HsCode, product.SstCode));

        product.SetTaxCodes("8471.30", " ");
        Assert.Equal(("8471.30", null), (product.HsCode, product.SstCode));

        product.SetTaxCodes(null, null);
        Assert.Equal((null, null), (product.HsCode, product.SstCode));
    }

    [Fact]
    public void A_bad_value_is_refused_and_nothing_changes()
    {
        var product = Product.Create("SKU-1", "Item", null, NewUom(), 0m);
        product.SetTaxCodes("8471.30", "ST-01");

        Assert.Equal(Messages.SstCodeInvalid, Assert.Throws<DomainException>(() => product.SetTaxCodes("9999", "ST_01")).Message);
        Assert.Equal(Messages.HsCodeInvalid, Assert.Throws<DomainException>(() => product.SetTaxCodes("A", "ST-02")).Message);

        Assert.Equal(("8471.30", "ST-01"), (product.HsCode, product.SstCode)); // not half-applied
    }

    [Fact]
    public void Update_does_not_touch_the_tax_codes_because_a_save_that_does_not_carry_them_must_keep_them()
    {
        var product = Product.Create("SKU-1", "Item", null, NewUom(), 0m);
        product.SetTaxCodes("8471.30", "ST-01");

        product.Update("SKU-1", "Renamed", null, 3m, true);

        Assert.Equal(("8471.30", "ST-01"), (product.HsCode, product.SstCode));
    }

    // ----- Partner -----
    [Fact]
    public void A_new_partner_has_no_tax_identifiers()
    {
        var partner = Partner.Create(PartnerType.Customer, "Acme");

        Assert.Null(partner.Tin);
        Assert.Null(partner.SstRegNo);
    }

    [Fact]
    public void SetTaxIdentifiers_stores_the_trimmed_values_and_blank_clears()
    {
        var partner = Partner.Create(PartnerType.Customer, "Acme");

        partner.SetTaxIdentifiers(" C2584563200 ", "W10-1808-32000123");
        Assert.Equal(("C2584563200", "W10-1808-32000123"), (partner.Tin, partner.SstRegNo));

        partner.SetTaxIdentifiers("", null);
        Assert.Equal((null, null), (partner.Tin, partner.SstRegNo));
    }

    [Fact]
    public void A_bad_partner_value_is_refused_and_nothing_changes()
    {
        var partner = Partner.Create(PartnerType.Customer, "Acme");
        partner.SetTaxIdentifiers("C2584563200", "W10");

        Assert.Equal(Messages.TinInvalid, Assert.Throws<DomainException>(() => partner.SetTaxIdentifiers("C-1", "W11")).Message);
        Assert.Equal(Messages.SstRegNoInvalid, Assert.Throws<DomainException>(() => partner.SetTaxIdentifiers("C2", "W/11")).Message);

        Assert.Equal(("C2584563200", "W10"), (partner.Tin, partner.SstRegNo));
    }

    [Fact]
    public void Partner_Update_does_not_touch_the_tax_identifiers()
    {
        var partner = Partner.Create(PartnerType.Customer, "Acme");
        partner.SetTaxIdentifiers("C2584563200", "W10");

        partner.Update(PartnerType.Customer, "Acme Ltd", "contact", true);

        Assert.Equal(("C2584563200", "W10"), (partner.Tin, partner.SstRegNo));
    }
}
