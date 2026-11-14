namespace Ims.Domain.Tests.Common;

/// <summary>
/// A quantity has at most four decimals (the column is decimal(18,4)), never passes <see cref="Limits.MaxBaseQuantity"/>, and rounds half away from zero where a conversion makes a fifth decimal (SQL Server converts
/// the same way). It is compared by value: 12.5 and 12.50 are the same quantity. Negative stock is allowed (BR-03), so a balance or a delta is a signed quantity, while a line is positive.
/// </summary>
public class QuantityTests
{
    private static decimal Dec(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    // ---- Signed: a balance or a delta ----

    [Theory]
    [InlineData("0")]
    [InlineData("0.0001")]
    [InlineData("-0.0001")]
    [InlineData("12.5")]
    [InlineData("-4")]
    [InlineData("99999999999999")]   // Limits.MaxBaseQuantity
    [InlineData("-99999999999999")]
    public void Signed_AcceptsZeroNegativeAndPositive_UpToTheLimit(string text)
    {
        Assert.Equal(Dec(text), Quantity.Signed(Dec(text)).Value);
    }

    [Theory]
    [InlineData("100000000000000")]            // one above Limits.MaxBaseQuantity
    [InlineData("-100000000000000")]
    [InlineData("0.00001")]                    // a fifth decimal
    [InlineData("1.23456")]
    [InlineData("-0.00005")]
    public void Signed_RefusesATooLargeValueOrOneWithTooManyDecimals(string text)
    {
        var ex = Assert.Throws<DomainException>(() => Quantity.Signed(Dec(text)));

        Assert.Equal(Messages.QtyTooLarge, ex.Message);
    }

    // ---- Balance: the stock on hand, over the whole decimal(18,4) column ----

    [Theory]
    [InlineData("0")]
    [InlineData("-0.0001")]
    [InlineData("99999999999999")]             // Limits.MaxBaseQuantity
    [InlineData("99999999999999.5")]           // above a line, inside the column
    [InlineData("-99999999999999.5")]
    [InlineData("99999999999999.9999")]        // Limits.MaxBalanceQuantity
    [InlineData("-99999999999999.9999")]
    public void Balance_AcceptsAnySignOverTheWholeColumn(string text)
    {
        Assert.Equal(Dec(text), Quantity.Balance(Dec(text)).Value);
    }

    [Theory]
    [InlineData("100000000000000")]            // one above the column
    [InlineData("-100000000000000")]
    [InlineData("99999999999999.99995")]       // a fifth decimal, even where it would round to the top
    [InlineData("0.00001")]
    [InlineData("-1.23456")]
    public void Balance_RefusesAValueBeyondTheColumnOrWithTooManyDecimals(string text)
    {
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.Balance(Dec(text))).Message);
    }

    [Fact]
    public void Balance_TheColumnRangeIsTheLineLimitPlusTheDecimals()
    {
        Assert.Equal(Limits.MaxBaseQuantity + 0.9999m, Limits.MaxBalanceQuantity);
        Assert.Equal(1.5m, Quantity.Balance(1.5000000m).Value);
    }

    // The line bound is the base limit, and it stays so: the wider range of a stored BALANCE (the whole decimal(18,4) column) does not widen what a line or a delta may hold.
    [Fact]
    public void TheBoundOfALineQuantity_IsTheBaseLimit_AndTheBalanceRangeDoesNotWidenIt()
    {
        Assert.Equal(99_999_999_999_999m, Limits.MaxBaseQuantity);
        Assert.Equal(Limits.MaxBaseQuantity, Quantity.Positive(Limits.MaxBaseQuantity).Value);
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.Positive(99_999_999_999_999.0001m)).Message);
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.Signed(99_999_999_999_999.5m)).Message);
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.Signed(-99_999_999_999_999.5m)).Message);
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.Rounded(99_999_999_999_999.5m)).Message);
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.Signed(Limits.MaxBaseQuantity).Plus(Quantity.Signed(0.5m))).Message);
    }

    [Fact]
    public void Signed_TrailingZerosAreNotDecimals()
    {
        Assert.Equal(1.5m, Quantity.Signed(1.5000000m).Value);
    }

    // ---- Positive: a line ----

    [Theory]
    [InlineData("0.0001")]
    [InlineData("3")]
    [InlineData("99999999999999")]
    public void Positive_AcceptsAQuantityAboveZero(string text)
    {
        Assert.Equal(Dec(text), Quantity.Positive(Dec(text)).Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.0001")]
    [InlineData("-5")]
    public void Positive_RefusesZeroAndNegative_WithTheLineSentence(string text)
    {
        var ex = Assert.Throws<DomainException>(() => Quantity.Positive(Dec(text)));

        Assert.Equal(Messages.QtyPositive, ex.Message);
    }

    [Fact]
    public void Positive_TheCallerMayPassItsOwnSentence_SoEachCallSiteKeepsItsMessage()
    {
        var ex = Assert.Throws<DomainException>(() => Quantity.Positive(0m, Messages.ReceiveQtyPositive));

        Assert.Equal(Messages.ReceiveQtyPositive, ex.Message);
    }

    [Theory]
    [InlineData("100000000000000")]
    [InlineData("1.00001")]
    public void Positive_RefusesATooLargeValueOrOneWithTooManyDecimals(string text)
    {
        var ex = Assert.Throws<DomainException>(() => Quantity.Positive(Dec(text)));

        Assert.Equal(Messages.QtyTooLarge, ex.Message);
    }

    // ---- NonNegative: a threshold or a safety level ----

    [Theory]
    [InlineData("0")]
    [InlineData("0.0001")]
    [InlineData("250")]
    public void NonNegative_AcceptsZeroAndAbove(string text)
    {
        Assert.Equal(Dec(text), Quantity.NonNegative(Dec(text), Messages.SafetyStockNegative).Value);
    }

    [Fact]
    public void NonNegative_RefusesANegative_WithTheSentenceOfTheCaller()
    {
        var safety = Assert.Throws<DomainException>(() => Quantity.NonNegative(-0.0001m, Messages.SafetyStockNegative));
        var threshold = Assert.Throws<DomainException>(() => Quantity.NonNegative(-1m, Messages.ThresholdNegative));

        Assert.Equal(Messages.SafetyStockNegative, safety.Message);
        Assert.Equal(Messages.ThresholdNegative, threshold.Message);
    }

    [Fact]
    public void NonNegative_RefusesATooLargeValueOrOneWithTooManyDecimals()
    {
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.NonNegative(100_000_000_000_000m, Messages.SafetyStockNegative)).Message);
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.NonNegative(0.00001m, Messages.SafetyStockNegative)).Message);
    }

    // ---- Rounded: a computed value is stored at four decimals, half away from zero ----

    [Theory]
    [InlineData("0.00004", "0")]
    [InlineData("0.00005", "0.0001")]   // to-even would give 0
    [InlineData("-0.00005", "-0.0001")]
    [InlineData("0.00015", "0.0002")]
    [InlineData("0.00025", "0.0003")]   // to-even would give 0.0002
    [InlineData("2.00004999", "2")]
    [InlineData("2.00005001", "2.0001")]
    [InlineData("-2.00005001", "-2.0001")]
    [InlineData("7.5", "7.5")]
    public void Rounded_GoesHalfAwayFromZeroToFourDecimals(string text, string expected)
    {
        Assert.Equal(Dec(expected), Quantity.Rounded(Dec(text)).Value);
    }

    [Fact]
    public void Rounded_RefusesAValueThatIsTooLargeAfterRounding()
    {
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.Rounded(99_999_999_999_999.99999m)).Message);
    }

    // ---- Value semantics ----

    [Fact]
    public void ItIsComparedByValue_NotByScale()
    {
        Assert.Equal(Quantity.Signed(12.5m), Quantity.Signed(12.50m));
        Assert.True(Quantity.Signed(12.5m) == Quantity.Signed(12.5000m));
        Assert.Equal(Quantity.Signed(12.5m).GetHashCode(), Quantity.Signed(12.5000m).GetHashCode());
        Assert.NotEqual(Quantity.Signed(12.5m), Quantity.Signed(12.5001m));
    }

    [Fact]
    public void TheDefaultQuantity_IsZero()
    {
        Assert.Equal(Quantity.Zero, default);
        Assert.Equal(0m, Quantity.Zero.Value);
        Assert.True(Quantity.Zero.IsZero);
    }

    [Fact]
    public void ItKnowsItsSign()
    {
        Assert.True(Quantity.Signed(0.0001m).IsPositive);
        Assert.True(Quantity.Signed(-0.0001m).IsNegative);
        Assert.False(Quantity.Zero.IsPositive);
        Assert.False(Quantity.Zero.IsNegative);
    }

    [Fact]
    public void ItIsOrderedByValue()
    {
        var small = Quantity.Signed(-3m);
        var large = Quantity.Signed(2.5m);

        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= Quantity.Signed(-3.0m));
        Assert.True(large >= Quantity.Signed(2.50m));
        Assert.True(small.CompareTo(large) < 0);
        Assert.Equal(0, large.CompareTo(Quantity.Signed(2.5m)));
    }

    [Fact]
    public void ToString_ShowsTheValueWithoutCulture()
    {
        Assert.Equal("12.5", Quantity.Signed(12.5000m).ToString());
        Assert.Equal("-0.0001", Quantity.Signed(-0.0001m).ToString());
    }

    // ---- Adding and taking away (a balance moves by a delta; negative stock is allowed, BR-03) ----

    [Fact]
    public void PlusAndMinus_WorkOnSignedQuantities_AndMayGoBelowZero()
    {
        var onHand = Quantity.Signed(3m);

        Assert.Equal(Quantity.Signed(5.25m), onHand.Plus(Quantity.Signed(2.25m)));
        Assert.Equal(Quantity.Signed(-1.5m), onHand.Minus(Quantity.Signed(4.5m)));
        Assert.Equal(Quantity.Zero, onHand.Minus(onHand));
    }

    [Fact]
    public void PlusAndMinus_RefuseAResultBeyondTheLimit()
    {
        var top = Quantity.Signed(Limits.MaxBaseQuantity);

        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => top.Plus(Quantity.Signed(1m))).Message);
        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Quantity.Signed(-Limits.MaxBaseQuantity).Minus(Quantity.Signed(1m))).Message);
    }

    // ---- Times: an entered quantity becomes a base quantity ----

    [Theory]
    [InlineData("3", "12", "36")]
    [InlineData("0.5", "12", "6")]
    [InlineData("1", "0.666667", "0.6667")]    // 0.666667
    [InlineData("3", "0.666667", "2")]         // 2.000001
    [InlineData("0.5", "0.666667", "0.3333")]  // 0.3333335, rounded at the fourth decimal
    [InlineData("0.0002", "0.5", "0.0001")]    // exactly the smallest step
    [InlineData("0.0001", "0.5", "0.0001")]    // 0.00005: half away from zero, not to even
    [InlineData("-0.0001", "0.5", "-0.0001")]
    [InlineData("0.0001", "0.4999", "0")]      // less than the smallest step becomes nothing; the caller refuses it
    [InlineData("0", "12", "0")]
    public void Times_MultipliesByTheFactor_RoundedToFourDecimals_HalfAwayFromZero(string qty, string factor, string expected)
    {
        Assert.Equal(Dec(expected), Quantity.Signed(Dec(qty)).Times(Dec(factor)).Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Times_RefusesAFactorThatIsNotAboveZero(string factor)
    {
        var ex = Assert.Throws<DomainException>(() => Quantity.Positive(1m).Times(Dec(factor)));

        Assert.Equal(Messages.ConversionFactorInvalid, ex.Message);
    }

    [Fact]
    public void Times_RefusesAResultBeyondTheLimit()
    {
        var ex = Assert.Throws<DomainException>(() => Quantity.Positive(1_000_000_000m).Times(1_000_000m));

        Assert.Equal(Messages.QtyTooLarge, ex.Message);
    }

    [Fact]
    public void Converted_RoundsTheProductOnce_WhateverTheScaleOfTheEnteredQuantity()
    {
        // 0.00005 x 12 = 0.0006; rounding the entered quantity first (to 0.0001) would give 0.0012.
        Assert.Equal(0.0006m, Quantity.Converted(0.00005m, 12m).Value);
        Assert.Equal(-0.0006m, Quantity.Converted(-0.00005m, 12m).Value);
    }
}
