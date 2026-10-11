using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>The one definition of low stock, evaluated in memory the same way the database evaluates it.</summary>
public class LowStockRuleTests
{
    private static readonly Func<StockBalance, bool> IsLow = LowStockRule.IsLowBalance.Compile();
    private static readonly Func<Product, bool> CanBeNeverStockedLow = LowStockRule.CanBeNeverStockedLow.Compile();

    private static StockBalance Balance(decimal qty, decimal safety, bool active = true, bool warehouseActive = true)
    {
        var product = Product.Create("SKU-1", "Product", null, NewUom(), safety, active).WithId(1);
        var balance = NewBalance(1, Math.Max(qty, 0m), 2m);
        if (qty < 0) balance.Issue(-qty, allowNegative: true);
        return balance.WithPrivate(nameof(StockBalance.Product), product).WithPrivate(nameof(StockBalance.Warehouse), NewWarehouse(active: warehouseActive));
    }

    [Theory]
    [InlineData(4, 10)]
    [InlineData(-3, 0)]
    public void ABalanceInAClosedWarehouse_IsNeverLow(double qty, double safety)
    {
        Assert.False(IsLow(Balance((decimal)qty, (decimal)safety, warehouseActive: false)));
    }

    [Theory]
    [InlineData(4, 10, true)]      // below the safety level
    [InlineData(0, 10, true)]      // nothing left
    [InlineData(10, 10, true)]     // AT the level is low (design 3.8: on-hand <= safety stock); a receipt must bring it above
    [InlineData(11, 10, false)]    // above the level
    [InlineData(0, 0, false)]      // no safety level, nothing wrong
    [InlineData(5, 0, false)]
    [InlineData(-3, 0, true)]      // negative is low even without a safety level
    [InlineData(-3, 10, true)]
    public void ABalance_IsLowWhenNegativeOrAtOrBelowItsSafetyLevel(double qty, double safety, bool low)
    {
        Assert.Equal(low, IsLow(Balance((decimal)qty, (decimal)safety)));
    }

    [Theory]
    [InlineData(4, 10, true)]
    [InlineData(10, 10, true)]
    [InlineData(11, 10, false)]
    [InlineData(-1, 0, true)]
    [InlineData(5, 0, false)]
    public void TheIsLowFlagOfAStockRow_IsTheVerdictOfTheSameRule(double qty, double safety, bool low)
    {
        // The projection of a stock row asks the rule for its verdict instead of repeating the condition.
        var projection = LowStockRule.Project((b, isLow) => new { b.QtyOnHand, IsLow = isLow }).Compile();
        var balance = Balance((decimal)qty, (decimal)safety);

        Assert.Equal(low, projection(balance).IsLow);
        Assert.Equal(IsLow(balance), projection(balance).IsLow);
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
