using System.Globalization;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Common;

/// <summary>
/// A conversion factor is above zero and is compared at <see cref="Limits.FactorScale"/> (6) places, the scale of its column <c>decimal(18,6)</c>: two factors that round to the same value are the same factor.
/// <see cref="ConversionFactor.Scaled(decimal)"/> is the rule <c>Product</c> held as a private helper (it decides whether a save changes a conversion that is in use, so it must not move by a digit).
/// </summary>
public class ConversionFactorTests
{
    private static decimal Dec(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    private static bool SameBits(decimal a, decimal b) => decimal.GetBits(a).SequenceEqual(decimal.GetBits(b));

    /// <summary>The helper <c>Product</c> held before DDD-05, copied as it was.</summary>
    private static decimal ProductScaled(decimal factor) => Math.Round(factor, Limits.FactorScale, MidpointRounding.AwayFromZero);

    private static IEnumerable<decimal> Factors() =>
        [-12m, -0.0000005m, 0m, 0.0000004m, 0.0000005m, 0.0000015m, 0.000001m, 0.5m, 0.666667m, 0.6666666666m, 0.6666665m, 1m, 12m, 12.0000004m, 12.0000005m, 12.0000006m, 11.9999995m, 24.1234565m, 480m, 1_000_000m, 99_999_999_999.9999995m];

    // ---- Of ----

    [Theory]
    [InlineData("12")]
    [InlineData("0.666667")]
    [InlineData("0.000001")]                       // the smallest step
    [InlineData("12.0000004")]                     // beyond the stored scale, but above zero: as the product accepted it
    public void Of_AcceptsAFactorAboveZero(string text)
    {
        Assert.Equal(Dec(text), ConversionFactor.Of(Dec(text)).Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-0.000001")]
    public void Of_RefusesZeroAndLess_WithTheFactorSentence(string text)
    {
        var ex = Assert.Throws<DomainException>(() => ConversionFactor.Of(Dec(text)));

        Assert.Equal(Messages.ConversionFactorInvalid, ex.Message);
    }

    [Fact]
    public void Of_KeepsTheFactorAsGiven_SoTheArithmeticOfAConversionDoesNotMove()
    {
        Assert.True(SameBits(12.0000004m, ConversionFactor.Of(12.0000004m).Value));
        Assert.True(SameBits(0.50m, ConversionFactor.Of(0.50m).Value));
    }

    [Fact]
    public void Factors_AreComparedByTheirValueAtTheStoredScale_ThroughSameAs()
    {
        Assert.Equal(ConversionFactor.Of(12m), ConversionFactor.Of(12.0m));
        Assert.True(ConversionFactor.Of(12m).SameAs(ConversionFactor.Of(12.0000004m)));          // rounds to the stored 12.000000
        Assert.False(ConversionFactor.Of(12m).SameAs(ConversionFactor.Of(12.0000005m)));         // half away from zero: 12.000001
        Assert.False(ConversionFactor.Of(12m).SameAs(ConversionFactor.Of(12.000001m)));
    }

    // ---- Scaled: the stored form ----

    [Fact]
    public void Scaled_IsTheOldProductHelper_ForEveryFactor_ScaleIncluded()
    {
        var wrong = Factors().Where(f => !SameBits(ProductScaled(f), ConversionFactor.Scaled(f))).Select(f => f.ToString(CultureInfo.InvariantCulture)).ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void Scaled_NeverThrows_ForAFactorThatIsNotValid()
    {
        // Product.ChangedConversionUnits looks at definitions that nobody has validated yet.
        Assert.Equal(0m, ConversionFactor.Scaled(0m));
        Assert.Equal(-12m, ConversionFactor.Scaled(-12m));
    }

    [Fact]
    public void Scaled_OfAnInstance_IsTheStaticRule()
    {
        foreach (var f in Factors().Where(f => f > 0m))
        {
            Assert.True(SameBits(ConversionFactor.Scaled(f), ConversionFactor.Of(f).Stored), f.ToString(CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("0.0000005", "0.000001")]          // the boundary goes away from zero; to-even would give 0.000000
    [InlineData("0.0000015", "0.000002")]
    [InlineData("0.0000025", "0.000003")]          // to-even would give 0.000002
    [InlineData("12.0000004", "12")]
    [InlineData("-0.0000005", "-0.000001")]
    public void Scaled_RoundsHalfAwayFromZeroToSixDecimals(string given, string expected)
    {
        Assert.Equal(Dec(expected), ConversionFactor.Scaled(Dec(given)));
    }

    // ---- Product: the decision "is this another factor?" is the same ----

    [Fact]
    public void Product_ChangedConversionUnits_DecidesAsTheOldHelperDid_OverEveryPairOfFactors()
    {
        var wrong = new List<string>();
        foreach (var stored in Factors().Where(f => f > 0m))
        {
            var product = NewProduct(cartonFactor: stored);
            foreach (var wanted in Factors())
            {
                var changed = product.ChangedConversionUnits([new ConversionDefinition(CartonId, PcsId, wanted)]).Count > 0;
                var expected = ProductScaled(wanted) != ProductScaled(stored);
                if (changed != expected) wrong.Add($"stored {stored}, wanted {wanted}: changed={changed}");
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void Product_SetConversions_LeavesAFactorThatRoundsToTheStoredOneAlone()
    {
        var product = NewProduct(cartonFactor: 12m);
        var inUse = new Dictionary<int, string> { [CartonId] = "CARTON" };

        product.SetConversions([new ConversionDefinition(CartonId, PcsId, 12.0000004m)], inUse);

        Assert.Equal(12m, product.Conversions.Single().Factor);
        Assert.Throws<DomainException>(() => product.SetConversions([new ConversionDefinition(CartonId, PcsId, 12.0000005m)], inUse));
    }

    // ---- Quantity.Times(ConversionFactor) ----

    [Fact]
    public void Times_AFactor_IsTimesTheDecimalFactor_BitForBit()
    {
        var wrong = new List<string>();
        foreach (var qty in new[] { 0m, 0.0001m, 1m, 2.5m, 3m, 7.3333m, 100m, 123456.7891m, -4m, -0.0001m })
        foreach (var factor in Factors().Where(f => f > 0m))
        {
            decimal? expected = null;
            try { expected = Quantity.Signed(qty).Times(factor).Value; } catch (DomainException) { /* too large: the factor overload must refuse too */ }

            if (expected is null)
            {
                Assert.Throws<DomainException>(() => Quantity.Signed(qty).Times(ConversionFactor.Of(factor)));
            }
            else if (!SameBits(expected.Value, Quantity.Signed(qty).Times(ConversionFactor.Of(factor)).Value))
            {
                wrong.Add($"{qty} x {factor}");
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void Times_AFactor_RoundsOnceToFourDecimals_HalfAwayFromZero()
    {
        Assert.Equal(0.0002m, Quantity.Signed(0.0001m).Times(ConversionFactor.Of(1.5m)).Value);        // 0.00015 goes away from zero
        Assert.Equal(-0.0002m, Quantity.Signed(-0.0001m).Times(ConversionFactor.Of(1.5m)).Value);
        Assert.Equal(40m, Quantity.Signed(2m).Times(ConversionFactor.Of(20m)).Value);
    }
}
