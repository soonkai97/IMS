using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// PR-01: reversing a receipt takes its cost back out of the moving average. Every test runs through the posting engine, the way the API does,
/// so the snapshot a posted receipt line keeps (<see cref="AverageCostChange"/>) is the one a reversal reads.
/// </summary>
public class ReceiptReversalCostingTests
{
    private readonly PostingEngine _engine = NewEngine();
    private readonly Product _product = NewProduct(id: 5);

    private StockDocument PostReceipt(StockBalance balance, decimal qty, decimal cost, int id = 1)
    {
        var doc = NewReceipt(id, (_product, qty, cost)).Submitted();
        _engine.Post(doc, Balances(balance), NewContext(doc));
        return doc;
    }

    private void PostIssue(StockBalance balance, decimal qty, int id = 90)
    {
        var doc = NewIssue(id, (_product, qty)).Submitted();
        _engine.Post(doc, Balances(balance), NewContext(doc));
    }

    private void Reverse(StockDocument doc, StockBalance balance, bool allowNegative = false) =>
        _engine.Reverse(doc, Balances(balance), NewContext(doc, allowNegative));

    // ----- the reported scenario -----
    [Fact]
    public void ReceiptReversedRightAfterPosting_RestoresQuantityAndAverageExactly()
    {
        var balance = NewBalance(5, 735m, 4.76m);
        var receipt = PostReceipt(balance, 600m, 200m);
        Assert.Equal(1335m, balance.QtyOnHand);
        Assert.True(balance.AvgCost > 92m); // the contamination the QA run saw (92.51)

        Reverse(receipt, balance);

        Assert.Equal(735m, balance.QtyOnHand);
        Assert.Equal(4.76m, balance.AvgCost);
        Assert.Equal(3498.6m, balance.QtyOnHand * balance.AvgCost);
    }

    [Fact]
    public void AfterTheReversal_AnIssueIsValuedAtTheOldAverage_NotAtTheReversedReceiptsCost()
    {
        var balance = NewBalance(5, 735m, 4.76m);
        Reverse(PostReceipt(balance, 600m, 200m), balance);

        var issue = NewIssue(2, (_product, 60m)).Submitted();
        issue.EstimateIssueCosts(new Dictionary<int, decimal> { [5] = balance.AvgCost });

        Assert.Equal(285.6m, issue.TotalAmount); // 60 x 4.76, not 5,550.36
    }

    // ----- later movements -----
    [Fact]
    public void ReceiveReceive_ReverseTheFirst_TheSecondKeepsItsOwnCost()
    {
        var balance = StockBalance.OpenFor(5, 1);
        var first = PostReceipt(balance, 100m, 10m, id: 1);
        PostReceipt(balance, 100m, 20m, id: 2);
        Assert.Equal(15m, balance.AvgCost);

        Reverse(first, balance);

        Assert.Equal(100m, balance.QtyOnHand);
        Assert.Equal(20m, balance.AvgCost);
        Assert.Equal(2000m, balance.QtyOnHand * balance.AvgCost);
    }

    [Fact]
    public void ReceiveReceive_ReverseTheSecond_TheFirstKeepsItsOwnCost()
    {
        var balance = StockBalance.OpenFor(5, 1);
        PostReceipt(balance, 100m, 10m, id: 1);
        var second = PostReceipt(balance, 100m, 20m, id: 2);

        Reverse(second, balance);

        Assert.Equal(100m, balance.QtyOnHand);
        Assert.Equal(10m, balance.AvgCost);
    }

    [Fact]
    public void ReceiveIssueReverse_TheAverageGoesBackAndTheIssuedUnitsStayOut()
    {
        var balance = NewBalance(5, 735m, 4.76m);
        var receipt = PostReceipt(balance, 600m, 200m);
        PostIssue(balance, 60m); // booked at the blended average (about 92.51): that cost stays on the ledger
        Assert.Equal(1275m, balance.QtyOnHand);

        Reverse(receipt, balance);

        Assert.Equal(675m, balance.QtyOnHand); // 735 - 60
        Assert.Equal(4.76m, balance.AvgCost);  // as if the receipt had never happened
        Assert.Equal(3213m, balance.QtyOnHand * balance.AvgCost);
    }

    [Fact]
    public void ReceiveIssueReverse_WhenTheIssuedUnitsAreMoreThanWhatWasThereBefore_IsBlockedByQuantity()
    {
        var balance = NewBalance(5, 10m, 4m);
        var receipt = PostReceipt(balance, 20m, 10m);
        PostIssue(balance, 25m); // 5 left: the receipt cannot come out (20 > 5)

        Assert.Throws<InsufficientStockException>(() => Reverse(receipt, balance));

        Assert.Equal(5m, balance.QtyOnHand);
        Assert.Equal(8m, balance.AvgCost); // untouched
    }

    [Fact]
    public void AReceiptOfTwoLinesOfTheSameProduct_IsUndoneLastLineFirst_AndRestoresTheStartingAverage()
    {
        var balance = NewBalance(5, 10m, 3m);
        var doc = NewReceipt(1, (_product, 10m, 7m), (_product, 30m, 11m)).Submitted();
        _engine.Post(doc, Balances(balance), NewContext(doc));

        _engine.Reverse(doc, Balances(balance), NewContext(doc));

        Assert.Equal(10m, balance.QtyOnHand);
        Assert.Equal(3m, balance.AvgCost);
    }

    [Fact]
    public void ALaterReceiptAtADifferentCost_AndIssuesInBetween_StillLeavesNoNegativeAverage()
    {
        // R1 100 @ 1, R2 10 @ 100, R3 100 @ 1, then 150 issued: the value left (60 x 5.71) is less than what R2 brought in (10 x 100).
        var balance = StockBalance.OpenFor(5, 1);
        PostReceipt(balance, 100m, 1m, id: 1);
        var second = PostReceipt(balance, 10m, 100m, id: 2);
        PostReceipt(balance, 100m, 1m, id: 3);
        PostIssue(balance, 150m);
        var averageNow = balance.AvgCost;

        Reverse(second, balance);

        Assert.Equal(50m, balance.QtyOnHand);
        Assert.Equal(averageNow, balance.AvgCost); // no sound arithmetic: the average stays as it is
        Assert.True(balance.AvgCost > 0);
    }

    // ----- zero-stock edge -----
    [Fact]
    public void TheFirstReceiptOfAProduct_ReversedAgain_LeavesNoStockAndNoCost()
    {
        var balance = StockBalance.OpenFor(5, 1);
        var receipt = PostReceipt(balance, 10m, 5m);

        Reverse(receipt, balance);

        Assert.Equal(0m, balance.QtyOnHand);
        Assert.Equal(0m, balance.AvgCost);
    }

    [Fact]
    public void AReceiptOntoEmptyStockWithALastKnownCost_ReversedAgain_GivesTheLastKnownCostBack()
    {
        var balance = NewBalance(5, 10m, 7m);
        PostIssue(balance, 10m); // empty, last known cost 7
        var receipt = PostReceipt(balance, 4m, 50m);
        Assert.Equal(50m, balance.AvgCost); // zero stock: the new cost wins

        Reverse(receipt, balance);

        Assert.Equal(0m, balance.QtyOnHand);
        Assert.Equal(7m, balance.AvgCost);
    }

    [Fact]
    public void AReceiptOntoNegativeStock_ReversedAgain_GivesTheOldAverageBack()
    {
        var balance = NewBalance(5, 10m, 4m);
        var issue = NewIssue(3, (_product, 15m)).ApprovedWithOverride();
        _engine.Post(issue, Balances(balance), NewContext(issue, allowNegative: true, canOverride: true));
        Assert.Equal(-5m, balance.QtyOnHand);
        var receipt = PostReceipt(balance, 8m, 10m);
        Assert.Equal((3m, 10m), (balance.QtyOnHand, balance.AvgCost));

        Reverse(receipt, balance, allowNegative: true);

        Assert.Equal((-5m, 4m), (balance.QtyOnHand, balance.AvgCost));
    }

    [Fact]
    public void ReversingMoreThanIsLeft_WhenNegativeStockIsAllowed_GoesToTheAverageBeforeThatReceipt()
    {
        var balance = StockBalance.OpenFor(5, 1);
        PostReceipt(balance, 10m, 5m, id: 1);
        var second = PostReceipt(balance, 10m, 9m, id: 2);
        PostIssue(balance, 15m); // 5 left; the second receipt (10) can only come out with negative stock
        var averageNow = balance.AvgCost;

        Reverse(second, balance, allowNegative: true);

        Assert.Equal(-5m, balance.QtyOnHand);
        Assert.Equal(5m, balance.AvgCost); // snapshot: the average had not moved since the second receipt, so it goes back to its value before it
        Assert.NotEqual(averageNow, balance.AvgCost);
    }

    // ----- receipts posted before the snapshot existed (no AverageCostChange) -----
    [Fact]
    public void ALegacyReceipt_WhenItIsTheLatestMovement_IsUndoneByTheArithmeticToThePrecisionOfTheAverage()
    {
        var balance = NewBalance(5, 735m, 4.76m);
        var receipt = PostReceipt(balance, 600m, 200m);
        ForgetTheSnapshot(receipt);

        Reverse(receipt, balance);

        Assert.Equal(735m, balance.QtyOnHand);
        Assert.InRange(balance.AvgCost, 4.76m - 0.000001m, 4.76m + 0.000001m);
    }

    [Fact]
    public void ALegacyReceipt_WithAFollowingReceipt_RemovesItsOwnValueFromTheStock()
    {
        var balance = StockBalance.OpenFor(5, 1);
        var first = PostReceipt(balance, 100m, 10m, id: 1);
        PostReceipt(balance, 100m, 20m, id: 2);
        ForgetTheSnapshot(first);

        Reverse(first, balance);

        Assert.Equal(100m, balance.QtyOnHand);
        Assert.Equal(20m, balance.AvgCost);
    }

    [Fact]
    public void ALegacyReceipt_WhoseCostIsLargerThanTheValueThatRemains_KeepsTheAverage()
    {
        var balance = NewBalance(5, 100m, 1m);
        var receipt = PostReceipt(balance, 10m, 100m);
        PostIssue(balance, 100m);
        ForgetTheSnapshot(receipt);
        var averageNow = balance.AvgCost;

        Reverse(receipt, balance, allowNegative: true);

        Assert.Equal(0m, balance.QtyOnHand);
        Assert.Equal(averageNow, balance.AvgCost);
    }

    // ----- precision -----
    [Fact]
    public void ATinyStockAndAHugeReceipt_ReversedAgain_GetsTheExactAverageBack_NotARoundedOne()
    {
        // The arithmetic alone loses digits here (1,000,001 / 1 times a 10th decimal place); the recorded average does not.
        var balance = NewBalance(5, 1m, 4.1234567891m);
        var receipt = PostReceipt(balance, 1_000_000m, 10.01m);

        Reverse(receipt, balance);

        Assert.Equal(1m, balance.QtyOnHand);
        Assert.Equal(4.1234567891m, balance.AvgCost);
    }

    [Fact]
    public void TheAverageKeepsTenDecimalPlaces()
    {
        var balance = NewBalance(5, 3m, 10m);
        PostReceipt(balance, 4m, 11m); // (30 + 44) / 7

        Assert.Equal(10.5714285714m, balance.AvgCost);
    }

    [Fact]
    public void ReversingAnIssue_PutsTheStockBackAtTheCostItLeftAt_AndLeavesTheAverageUnchanged()
    {
        var balance = NewBalance(5, 3m, 10m);
        PostReceipt(balance, 4m, 11m);
        var average = balance.AvgCost;
        var issue = NewIssue(7, (_product, 5m)).Submitted();
        _engine.Post(issue, Balances(balance), NewContext(issue));

        _engine.Reverse(issue, Balances(balance), NewContext(issue));

        Assert.Equal(7m, balance.QtyOnHand);
        Assert.Equal(average, balance.AvgCost); // exactly, not within a rounding step
    }

    /// <summary>A receipt posted before the snapshot columns existed has none.</summary>
    private static void ForgetTheSnapshot(StockDocument receipt)
    {
        foreach (var line in receipt.Lines)
        {
            line.WithPrivate(nameof(StockDocumentLine.AvgCostBefore), null);
            line.WithPrivate(nameof(StockDocumentLine.AvgCostAfter), null);
        }
    }
}
