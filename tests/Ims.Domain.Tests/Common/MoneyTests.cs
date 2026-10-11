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

    [Fact]
    public void TheRoundingMode_IsAwayFromZero_AndOnlyHere()
    {
        Assert.Equal(MidpointRounding.AwayFromZero, Money.Rounding);
        Assert.Equal(2, Money.Decimals);
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
}
