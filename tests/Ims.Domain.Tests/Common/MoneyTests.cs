namespace Ims.Domain.Tests.Common;

/// <summary>Money has exactly 2 decimal places and one rounding mode: half a cent and more goes away from zero.</summary>
public class MoneyTests
{
    [Theory]
    [InlineData("0.004", "0.00")]
    [InlineData("0.005", "0.01")]   // the half-cent boundary: to-even would give 0.00
    [InlineData("0.015", "0.02")]
    [InlineData("0.025", "0.03")]   // to-even would give 0.02
    [InlineData("1.005", "1.01")]
    [InlineData("2.675", "2.68")]
    [InlineData("10.0049999999", "10.00")]
    [InlineData("10.0050000001", "10.01")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("-2.675", "-2.68")]
    [InlineData("12", "12.00")]
    [InlineData("12.3", "12.30")]
    public void Round_GoesHalfAwayFromZeroToTwoDecimals(string value, string expected)
    {
        var rounded = Money.Round(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), rounded);
    }

    [Theory]
    [InlineData("0.003", "0.0030")]        // a carton of 1,000 at 3.00: three tenths of a cent a piece, not 0.00
    [InlineData("0.00304", "0.0030")]
    [InlineData("0.00305", "0.0031")]      // the boundary at 4 places goes away from zero
    [InlineData("-0.00305", "-0.0031")]
    [InlineData("3.33333333", "3.3333")]
    [InlineData("12", "12.0000")]
    public void AUnitPrice_IsShownWithFourDecimals_HalfAwayFromZero(string value, string expected)
    {
        var rounded = Money.RoundUnitPrice(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), rounded);
    }

    [Fact]
    public void TheAmountOfAThousandPiecesAtTheShownUnitPrice_IsStillTheRightAmount()
    {
        Assert.Equal(3.00m, Money.Amount(1000m, Money.RoundUnitPrice(0.003m)));
    }

    [Theory]
    [InlineData("-10.005", "-10.01")]  // negative stock at an average of 10.005: -1 piece is worth -10.01, as +1 is worth 10.01
    [InlineData("-0.004", "0.00")]
    public void ANegativeValue_IsRoundedAwayFromZeroToo(string value, string expected)
    {
        var rounded = Money.Round(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), rounded);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("12", true)]
    [InlineData("12.5", true)]
    [InlineData("12.50", true)]
    [InlineData("12.500", true)]   // trailing zeros are not a fraction of a cent
    [InlineData("0.01", true)]
    [InlineData("12.505", false)]
    [InlineData("0.001", false)]
    [InlineData("0.0000000001", false)]
    public void IsExact_MeansNoFractionOfACent(string value, bool exact)
    {
        Assert.Equal(exact, Money.IsExact(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Amount_IsTheQuantityTimesTheCost_RoundedOnce()
    {
        Assert.Equal(0.02m, Money.Amount(1.5m, 0.01m));          // 0.015
        Assert.Equal(10.00m, Money.Amount(3m, 3.333333m));       // a carton price spread over 3 pieces: 9.999999 is 10.00
        Assert.Equal(10.01m, Money.Amount(1m, 10.005m));
    }

    // ----- Money as a value object (DDD-02): the instance API next to the static members above -----

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static string Text(Money money) => money.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Money_IsAValueObject_ComparedByValue()
    {
        Assert.IsAssignableFrom<IValueObject>(Money.Zero);
        Assert.Equal(Money.Rounded(12.5m), Money.Rounded(12.50m));       // 12.5 and 12.50 are the same money
        Assert.Equal(Money.Rounded(12.5m).GetHashCode(), Money.Rounded(12.500m).GetHashCode());
        Assert.True(Money.Rounded(12.5m) == Money.Exact(12.50m, Messages.AmountTooPrecise));
        Assert.True(Money.Rounded(12.5m) != Money.Rounded(12.51m));
        Assert.Equal(Money.Zero, default(Money));
    }

    [Fact]
    public void TheValue_IsKeptAtExactlyTwoDecimalPlaces()
    {
        Assert.Equal("12.50", Text(Money.Rounded(12.5m)));
        Assert.Equal("12.00", Text(Money.Exact(12m, Messages.AmountTooPrecise)));
        Assert.Equal("0.00", Text(Money.Zero));
    }

    [Theory]
    [InlineData("12.505")]
    [InlineData("0.001")]
    [InlineData("0.0000000001")]
    [InlineData("-0.005")]
    public void Exact_RefusesAFractionOfACent_WithTheSentenceOfTheCaller(string value)
    {
        foreach (var sentence in new[] { Messages.CostTooPrecise, Messages.PriceTooPrecise, Messages.AmountTooPrecise })
        {
            var ex = Assert.Throws<DomainException>(() => Money.Exact(D(value), sentence));

            Assert.Equal(sentence, ex.Message);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("12.5")]
    [InlineData("12.500")]
    [InlineData("-3.25")]
    public void Exact_AcceptsUpToTwoDecimals_AndKeepsTheValue(string value)
    {
        Assert.Equal(D(value), Money.Exact(D(value), Messages.CostTooPrecise).Value);
    }

    [Theory]
    [InlineData("0.004")]
    [InlineData("0.005")]
    [InlineData("0.015")]
    [InlineData("0.025")]
    [InlineData("1.005")]
    [InlineData("2.675")]
    [InlineData("10.0049999999")]
    [InlineData("10.0050000001")]
    [InlineData("-0.005")]
    [InlineData("-2.675")]
    [InlineData("-10.005")]
    [InlineData("12.3")]
    public void Rounded_IsHalfAwayFromZero_ThePairOfTheStaticRound(string value)
    {
        // The same cases the static Round and the SQL parity test (MoneyPrecisionTests) use: ROUND on a decimal goes away from zero too.
        Assert.Equal(Money.Round(D(value)), Money.Rounded(D(value)).Value);
        Assert.True(Money.IsExact(Money.Rounded(D(value)).Value));
    }

    [Fact]
    public void Rounded_GoesAwayFromZeroAtTheHalfCent_NotToEven()
    {
        Assert.Equal(0.01m, Money.Rounded(0.005m).Value);
        Assert.Equal(0.03m, Money.Rounded(0.025m).Value);
        Assert.Equal(-0.01m, Money.Rounded(-0.005m).Value);
    }

    [Fact]
    public void Money_AddsAndSubtracts_AndMayGoNegative()
    {
        var a = Money.Rounded(10.10m);
        var b = Money.Rounded(0.20m);

        Assert.Equal(Money.Rounded(10.30m), a + b);
        Assert.Equal(Money.Rounded(9.90m), a - b);
        Assert.Equal(Money.Rounded(-9.90m), b - a);            // a stock value can be negative (BR-03)
        Assert.Equal(a + b, a.Plus(b));
        Assert.Equal(a - b, a.Minus(b));
        Assert.Equal(Money.Rounded(0.30m), Money.Rounded(0.10m) + Money.Rounded(0.20m));
    }

    [Fact]
    public void Money_Sums_ToTheSumOfItsParts()
    {
        var lines = new[] { Money.Rounded(0.10m), Money.Rounded(0.20m), Money.Rounded(0.30m) };

        Assert.Equal(Money.Rounded(0.60m), lines.Aggregate(Money.Zero, (total, line) => total + line));
    }

    [Fact]
    public void Money_IsOrdered()
    {
        var small = Money.Rounded(1.00m);
        var big = Money.Rounded(1.01m);

        Assert.True(small < big);
        Assert.True(big > small);
        Assert.True(small <= Money.Rounded(1m));
        Assert.True(big >= Money.Rounded(1.01m));
        Assert.True(small.CompareTo(big) < 0);
        Assert.Equal(0, small.CompareTo(Money.Rounded(1.0m)));
        Assert.Equal(new[] { small, big }, new[] { big, small }.Order().ToArray());
    }

    [Fact]
    public void Of_IsTheQuantityTimesTheCost_RoundedOnce_AndEqualsTheStaticAmount()
    {
        Assert.Equal(Money.Rounded(0.02m), Money.Of(1.5m, 0.01m));          // 0.015
        Assert.Equal(Money.Rounded(10.00m), Money.Of(3m, 3.333333m));       // 9.999999 is 10.00
        Assert.Equal(Money.Rounded(10.01m), Money.Of(1m, 10.005m));
        Assert.Equal(Money.Rounded(3.00m), Money.Of(1000m, Money.RoundUnitPrice(0.003m)));
        Assert.Equal(Money.Amount(7.25m, 1.3333m), Money.Of(7.25m, 1.3333m).Value);
    }

    [Fact]
    public void TheLineLimit_IsTheCapOfALineAmount()
    {
        var atLimit = Money.Rounded(Limits.MaxLineAmount);
        var over = atLimit + Money.Rounded(0.01m);

        Assert.True(atLimit.IsWithinLineLimit);
        Assert.False(over.IsWithinLineLimit);
        Assert.Equal(atLimit, atLimit.EnsureWithinLineLimit());
        Assert.Equal(Messages.LineAmountTooLarge, Assert.Throws<DomainException>(() => over.EnsureWithinLineLimit()).Message);
    }

    [Fact]
    public void TheStaticMembersAndConstants_KeepTheirMeaning()
    {
        Assert.Equal(2, Money.Decimals);
        Assert.Equal(4, Money.UnitPriceDecimals);
        Assert.Equal(MidpointRounding.AwayFromZero, Money.Rounding);
        Assert.Equal(12.35m, Money.Round(12.345m));
        Assert.Equal(12.3457m, Money.Round(12.34567m, 4));
        Assert.Equal(0.0031m, Money.RoundUnitPrice(0.00305m));
        Assert.True(Money.IsExact(12.50m));
        Assert.Equal(10.01m, Money.Amount(1m, 10.005m));
    }
}
