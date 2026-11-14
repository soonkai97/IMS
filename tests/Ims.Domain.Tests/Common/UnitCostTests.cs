using System.Globalization;

namespace Ims.Domain.Tests.Common;

/// <summary>
/// A cost per base unit is never negative, never above <see cref="Limits.MaxCostPerBaseUnit"/> (the column is <c>decimal(22,10)</c>), and keeps <see cref="Limits.CostDecimals"/> (10) places inside the system, half away
/// from zero. It is compared by value (2.5 and 2.50 are the same cost). <c>PerBaseUnit</c> is the cost of an entered unit spread over the base units it holds (BR-05), <c>Blend</c> the moving weighted average
/// (BR-05) that <see cref="StockBalance.Receive"/> keeps: both must give, bit for bit, what the decimal code gave before the type existed.
/// </summary>
public class UnitCostTests
{
    private static decimal Dec(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    private static bool SameBits(decimal a, decimal b) => decimal.GetBits(a).SequenceEqual(decimal.GetBits(b));

    // ---- Of ----

    [Theory]
    [InlineData("0")]
    [InlineData("2.5")]
    [InlineData("0.0030")]
    [InlineData("3.3333333333")]                  // ten decimals
    [InlineData("999999999999")]                  // Limits.MaxCostPerBaseUnit
    [InlineData("0.0000000001")]                  // the smallest step
    public void Of_AcceptsACostWithinTheColumn(string text)
    {
        Assert.Equal(Dec(text), UnitCost.Of(Dec(text)).Value);
    }

    [Theory]
    [InlineData("1.00000000005", "1.0000000001")]   // an eleventh decimal rounds half away from zero, as SQL Server converts into the column
    [InlineData("1.00000000004", "1")]
    [InlineData("0.12345678905", "0.1234567891")]
    public void Of_KeepsTenDecimalsHalfAwayFromZero(string given, string kept)
    {
        Assert.Equal(Dec(kept), UnitCost.Of(Dec(given)).Value);
    }

    [Theory]
    [InlineData("-0.0000000001")]
    [InlineData("-1")]
    public void Of_RefusesANegativeCost(string text)
    {
        var ex = Assert.Throws<DomainException>(() => UnitCost.Of(Dec(text)));

        Assert.Equal(Messages.CostNegative, ex.Message);
    }

    [Theory]
    [InlineData("1000000000000")]                 // one above Limits.MaxCostPerBaseUnit
    [InlineData("999999999999.0000000001")]
    public void Of_RefusesACostAboveTheColumn(string text)
    {
        var ex = Assert.Throws<DomainException>(() => UnitCost.Of(Dec(text)));

        Assert.Equal(Messages.CostTooLargeForUnit, ex.Message);
    }

    [Fact]
    public void Of_RoundsBeforeItLooksAtTheSign_SoATinyNegativeThatRoundsToZeroIsStillNegative()
    {
        // -0.00000000004 rounds to 0 at ten places: the column would hold 0, so it is not a negative cost.
        Assert.Equal(0m, UnitCost.Of(-0.00000000004m).Value);
    }

    [Fact]
    public void TheDefault_IsZero()
    {
        Assert.Equal(UnitCost.Zero, default(UnitCost));
        Assert.True(UnitCost.Zero.IsZero);
        Assert.Equal(0m, UnitCost.Zero.Value);
    }

    [Fact]
    public void Costs_AreComparedByValue_NotByScale()
    {
        Assert.Equal(UnitCost.Of(2.5m), UnitCost.Of(2.50m));
        Assert.Equal(UnitCost.Of(2.5m).GetHashCode(), UnitCost.Of(2.50m).GetHashCode());
        Assert.NotEqual(UnitCost.Of(2.5m), UnitCost.Of(2.5000000001m));
        Assert.True(UnitCost.Of(2m) < UnitCost.Of(2.0000000001m));
        Assert.True(UnitCost.Of(3m) >= UnitCost.Of(3.0m));
        Assert.Equal(-1, UnitCost.Of(1m).CompareTo(UnitCost.Of(2m)));
    }

    [Theory]
    [InlineData("3.33333333335", "3.3333")]       // shown at 4 places (Money.UnitPriceDecimals), half away from zero
    [InlineData("0.003", "0.003")]
    [InlineData("0.00305", "0.0031")]
    [InlineData("10", "10")]
    public void Shown_IsTheUnitPriceAFramePersonSees(string cost, string shown)
    {
        Assert.Equal(Dec(shown), UnitCost.Of(Dec(cost)).Shown);
        Assert.Equal(Money.RoundUnitPrice(Dec(cost)), UnitCost.Of(Dec(cost)).Shown);
    }

    [Fact]
    public void Of_ReturnsTheValueAtItsOwnScale_NoZerosAreAdded()
    {
        // The stored cost and the one in memory must agree bit for bit with what the decimal code kept.
        Assert.True(SameBits(2.5m, UnitCost.Of(2.5m).Value));
        Assert.True(SameBits(2.50m, UnitCost.Of(2.50m).Value));
        Assert.True(SameBits(3.3333333333m, UnitCost.Of(3.3333333333m).Value));
    }

    // ---- PerBaseUnit: BR-05, a cost per entered unit spread over the base units ----

    [Theory]
    [InlineData("10", "1", "1", "10")]            // base unit entered: unchanged
    [InlineData("120", "3", "36", "10")]          // 3 cartons at 120 are 36 pieces at 10
    [InlineData("10", "1", "3", "3.3333333333")]  // rounded to ten decimals
    [InlineData("5", "2", "1", "10")]             // the entered unit is smaller than the base: the cost per base unit goes up
    [InlineData("0", "4", "8", "0")]
    [InlineData("3", "1", "1000", "0.003")]       // a carton of a thousand pieces at 3.00
    [InlineData("3.33", "2", "14", "0.4757142857")]   // the oracle's carton of seven (carton-of-seven-has-no-exact-piece-cost)
    public void PerBaseUnit_SpreadsTheEnteredCostOverTheBaseUnits(string cost, string enteredQty, string baseQty, string expected)
    {
        var perBase = UnitCost.PerBaseUnit(Dec(cost), Dec(enteredQty), Dec(baseQty));

        Assert.Equal(Dec(expected), perBase.Value);
    }

    [Fact]
    public void PerBaseUnit_IsExactlyTheOldExpression_OverAGrid()
    {
        var mismatches = new List<string>();
        foreach (var cost in new[] { 0m, 0.01m, 1m, 2.5m, 3.33m, 10m, 99.99m, 120m, 1_000_000_000m })
            foreach (var entered in new[] { 0.5m, 1m, 2m, 3m, 7m, 12.5m, 100_000m })
                foreach (var baseQty in new[] { 0.0001m, 1m, 3m, 7m, 14m, 36m, 1000m, 1_200_000m })
                {
                    var expected = Math.Round(cost * entered / baseQty, Limits.CostDecimals, Limits.CostRounding);
                    if (expected > Limits.MaxCostPerBaseUnit)
                    {
                        Assert.Throws<DomainException>(() => UnitCost.PerBaseUnit(cost, entered, baseQty));
                        continue;
                    }
                    if (!SameBits(expected, UnitCost.PerBaseUnit(cost, entered, baseQty).Value)) mismatches.Add($"{cost} x {entered} / {baseQty}");
                }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void PerBaseUnit_RefusesACostThatDoesNotFitTheColumn_WithTheUnitSentence()
    {
        // 1,000,000,000 per carton of 0.0001 pieces: 1e13 a piece.
        var ex = Assert.Throws<DomainException>(() => UnitCost.PerBaseUnit(1_000_000_000m, 1m, 0.0001m));

        Assert.Equal(Messages.CostTooLargeForUnit, ex.Message);
    }

    [Fact]
    public void CostingPolicy_BaseUnitCost_ReturnsThePerBaseUnitValueAsADecimal()
    {
        Assert.Equal(UnitCost.PerBaseUnit(120m, 3m, 36m).Value, CostingPolicy.BaseUnitCost(DocumentType.Receipt, 120m, 3m, 36m));
        Assert.Equal(0m, CostingPolicy.BaseUnitCost(DocumentType.Issue, 120m, 3m, 36m));
        Assert.Equal(0m, CostingPolicy.BaseUnitCost(DocumentType.Receipt, null, 3m, 36m));
        Assert.Equal(0m, CostingPolicy.BaseUnitCost(DocumentType.Receipt, 120m, 3m, 0m));
        Assert.Throws<DomainException>(() => CostingPolicy.BaseUnitCost(DocumentType.Receipt, 1_000_000_000m, 1m, 0.0001m));
    }

    // ---- Blend: the moving weighted average of StockBalance.Receive (BR-05) ----

    [Fact]
    public void Blend_IsTheWeightedAverage_RoundedToTenDecimals()
    {
        var average = UnitCost.Blend(Quantity.Signed(7m), UnitCost.Of(1m), Quantity.Positive(3m), UnitCost.Of(2m));

        Assert.Equal(Math.Round(13m / 10m, Limits.CostDecimals), average.Value);
        Assert.Equal(Dec("1.3"), average.Value);
        Assert.Equal(Dec("1.0571428571"), UnitCost.Blend(Quantity.Signed(3m), UnitCost.Of(1m), Quantity.Positive(4m), UnitCost.Of(1.1m)).Value);   // average-rounds-half-away-from-zero, step 2
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Blend_IntoEmptyOrNegativeStock_TakesTheNewCost(string onHand)
    {
        var average = UnitCost.Blend(Quantity.Signed(Dec(onHand)), UnitCost.Of(9m), Quantity.Positive(4m), UnitCost.Of(2m));

        Assert.Equal(2m, average.Value);
    }

    [Fact]
    public void Blend_WhenTheReceiptDoesNotBringTheStockAboveZero_TakesTheNewCost()
    {
        // -10 on hand, 4 in: still -6, the old average means nothing.
        var average = UnitCost.Blend(Quantity.Signed(-10m), UnitCost.Of(9m), Quantity.Positive(4m), UnitCost.Of(2m));

        Assert.Equal(2m, average.Value);
    }

    [Fact]
    public void Blend_AtTheAverageItself_LeavesItExactly()
    {
        // The return of an issue comes back at the average: deriving it again would drift in the last place.
        var average = UnitCost.Of(1.0571428571m);

        var blended = UnitCost.Blend(Quantity.Signed(7m), average, Quantity.Positive(3m), UnitCost.Of(1.0571428571m));

        Assert.True(SameBits(1.0571428571m, blended.Value));
    }

    [Fact]
    public void Blend_AtTheAverageWithAnotherScale_KeepsTheAverageAsItWas()
    {
        var blended = UnitCost.Blend(Quantity.Signed(7m), UnitCost.Of(2.5m), Quantity.Positive(3m), UnitCost.Of(2.50m));

        Assert.True(SameBits(2.5m, blended.Value));
    }

    [Fact]
    public void Blend_NeverAsksForMoreThanTheColumn_BecauseAnAverageOfTwoCostsIsBetweenThem()
    {
        var max = UnitCost.Of(Limits.MaxCostPerBaseUnit);

        var blended = UnitCost.Blend(Quantity.Signed(1m), max, Quantity.Positive(1m), UnitCost.Of(Limits.MaxCostPerBaseUnit - 1m));

        Assert.True(blended <= max);
    }
}
