using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

public class StockBalanceTests
{
    [Fact]
    public void OpenFor_StartsAtZero()
    {
        var balance = StockBalance.OpenFor(productId: 3, warehouseId: 4);

        Assert.Equal(3, balance.ProductId);
        Assert.Equal(4, balance.WarehouseId);
        Assert.Equal(0m, balance.QtyOnHand);
        Assert.Equal(0m, balance.AvgCost);
    }

    // ----- Receive: moving weighted-average cost (BR-05) -----
    [Fact]
    public void Receive_FirstReceipt_SetsAverageToThatCost()
    {
        var balance = StockBalance.OpenFor(1, 1);

        balance.Receive(10m, 5m);

        Assert.Equal(10m, balance.QtyOnHand);
        Assert.Equal(5m, balance.AvgCost);
    }

    [Fact]
    public void Receive_SeveralReceiptsAtDifferentCosts_KeepsMovingWeightedAverage()
    {
        var balance = StockBalance.OpenFor(1, 1);

        balance.Receive(10m, 5m);  // 10 @ 5            -> avg 5
        balance.Receive(10m, 7m);  // 20 @ (50+70)/20    -> avg 6
        balance.Receive(5m, 9m);   // 25 @ (120+45)/25   -> avg 6.6

        Assert.Equal(25m, balance.QtyOnHand);
        Assert.Equal(6.6m, balance.AvgCost);
    }

    [Fact]
    public void Receive_AtTheCurrentAverage_DoesNotMoveTheAverage()
    {
        var balance = NewBalance(1, 10m, 4m);

        balance.Receive(6m, 4m);

        Assert.Equal(4m, balance.AvgCost);
    }

    [Fact]
    public void Receive_RoundsAverageToConfiguredDecimals()
    {
        var balance = NewBalance(1, 3m, 10m);

        balance.Receive(4m, 11m); // (30 + 44) / 7 = 10.571428571...

        Assert.Equal(Math.Round(74m / 7m, Limits.CostDecimals), balance.AvgCost);
        Assert.Equal(10.5714285714m, balance.AvgCost);
    }

    [Fact]
    public void Receive_AfterStockHitsZero_ResetsAverageToNewCost()
    {
        var balance = NewBalance(1, 10m, 4m);
        balance.Issue(10m, allowNegative: false);

        balance.Receive(5m, 9m);

        Assert.Equal(5m, balance.QtyOnHand);
        Assert.Equal(9m, balance.AvgCost);
    }

    [Fact]
    public void Receive_WhileStockIsNegative_TakesNewCostEvenIfStillNegativeAfterwards()
    {
        var balance = NewBalance(1, 10m, 4m);
        balance.Issue(15m, allowNegative: true); // -5

        balance.Receive(3m, 6m);                 // -2, still negative

        Assert.Equal(-2m, balance.QtyOnHand);
        Assert.Equal(6m, balance.AvgCost);
    }

    [Fact]
    public void Receive_FromNegativeToPositive_UsesNewCostNotABlend()
    {
        var balance = NewBalance(1, 10m, 4m);
        balance.Issue(15m, allowNegative: true); // -5 @ 4

        balance.Receive(8m, 10m);                // +3

        Assert.Equal(3m, balance.QtyOnHand);
        Assert.Equal(10m, balance.AvgCost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Receive_NonPositiveQuantity_Throws(int qty)
    {
        var balance = NewBalance(1, 5m, 2m);

        var ex = Assert.Throws<DomainException>(() => balance.Receive(qty, 2m));

        Assert.Equal(Messages.ReceiveQtyPositive, ex.Message);
        Assert.Equal(5m, balance.QtyOnHand);
    }

    // ----- Issue -----
    [Fact]
    public void Issue_ReturnsCurrentAverageCostAndReducesQuantity()
    {
        var balance = NewBalance(1, 10m, 4.5m);

        var cost = balance.Issue(4m, allowNegative: false);

        Assert.Equal(4.5m, cost);
        Assert.Equal(6m, balance.QtyOnHand);
    }

    [Fact]
    public void Issue_DoesNotChangeTheAverage()
    {
        var balance = NewBalance(1, 10m, 4.5m);

        balance.Issue(9m, allowNegative: false);

        Assert.Equal(4.5m, balance.AvgCost);
    }

    [Fact]
    public void Issue_ExactlyWhatIsOnHand_IsAllowedAndLeavesZero()
    {
        var balance = NewBalance(1, 10m, 4m);

        balance.Issue(10m, allowNegative: false);

        Assert.Equal(0m, balance.QtyOnHand);
    }

    [Fact]
    public void Issue_MoreThanOnHand_ThrowsInsufficientStockAndLeavesBalanceUntouched()
    {
        var balance = NewBalance(productId: 7, qty: 3m, avgCost: 2m, warehouseId: 9);

        var ex = Assert.Throws<InsufficientStockException>(() => balance.Issue(3.5m, allowNegative: false));

        Assert.Equal(7, ex.ProductId);
        Assert.Equal(9, ex.WarehouseId);
        Assert.Equal(3m, ex.Available);
        Assert.Equal(3.5m, ex.Requested);
        Assert.Equal(Messages.InsufficientStock(7, 9, 3m, 3.5m), ex.Message);
        Assert.Equal(3m, balance.QtyOnHand);
    }

    [Fact]
    public void Issue_FromEmptyBalance_ThrowsInsufficientStock()
    {
        var balance = StockBalance.OpenFor(1, 1);

        Assert.Throws<InsufficientStockException>(() => balance.Issue(1m, allowNegative: false));
    }

    [Fact]
    public void Issue_MoreThanOnHand_WhenNegativeAllowed_GoesBelowZeroAtCurrentAverage()
    {
        var balance = NewBalance(1, 3m, 2m);

        var cost = balance.Issue(5m, allowNegative: true);

        Assert.Equal(-2m, balance.QtyOnHand);
        Assert.Equal(2m, cost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Issue_NonPositiveQuantity_Throws(int qty)
    {
        var balance = NewBalance(1, 5m, 2m);

        var ex = Assert.Throws<DomainException>(() => balance.Issue(qty, allowNegative: true));

        Assert.Equal(Messages.IssueQtyPositive, ex.Message);
    }

    [Fact]
    public void InsufficientStockException_IsADomainException()
    {
        Assert.IsAssignableFrom<DomainException>(new InsufficientStockException(1, 1, 0m, 1m));
    }
}
