using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>The one definition of low stock, evaluated in memory the same way the database evaluates it.</summary>
public class LowStockRuleTests
{
    private static readonly Func<StockBalance, bool> IsLow = LowStockRule.IsLowBalance.Compile();
    private static readonly Func<Product, bool> CanBeNeverStockedLow = LowStockRule.CanBeNeverStockedLow.Compile();

    private static StockBalance Balance(decimal qty, decimal safety, bool active = true)
    {
        var product = Product.Create("SKU-1", "Product", null, NewUom(), safety, active).WithId(1);
        var balance = NewBalance(1, Math.Max(qty, 0m), 2m);
        if (qty < 0) balance.Issue(-qty, allowNegative: true);
        return balance.WithPrivate(nameof(StockBalance.Product), product);
    }

    [Theory]
    [InlineData(4, 10, true)]      // below the safety level
    [InlineData(0, 10, true)]      // nothing left
    [InlineData(10, 10, false)]    // equal to the level is not below it
    [InlineData(11, 10, false)]
    [InlineData(0, 0, false)]      // no safety level, nothing wrong
    [InlineData(5, 0, false)]
    [InlineData(-3, 0, true)]      // negative is low even without a safety level
    [InlineData(-3, 10, true)]
    public void ABalance_IsLowWhenNegativeOrBelowItsSafetyLevel(double qty, double safety, bool low)
    {
        Assert.Equal(low, IsLow(Balance((decimal)qty, (decimal)safety)));
    }

    [Theory]
    [InlineData(4, 10)]
    [InlineData(-3, 0)]
    [InlineData(0, 10)]
    public void AnInactiveProduct_IsNeverLow(double qty, double safety)
    {
        Assert.False(IsLow(Balance((decimal)qty, (decimal)safety, active: false)));
    }

    [Theory]
    [InlineData(10, true, true)]
    [InlineData(0, true, false)]    // no safety level: never stocked is not low
    [InlineData(10, false, false)]  // inactive
    public void AProductThatWasNeverStocked_IsLowOnlyWhenActiveWithASafetyLevel(double safety, bool active, bool low)
    {
        var product = Product.Create("SKU-1", "Product", null, NewUom(), (decimal)safety, active);

        Assert.Equal(low, CanBeNeverStockedLow(product));
    }
}
