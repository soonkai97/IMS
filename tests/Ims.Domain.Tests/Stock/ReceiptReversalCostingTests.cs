using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// PR-01: reversing a receipt takes its cost back out of the moving average. Every test runs through the posting engine, the way the API does, so the
/// snapshot a posted receipt line keeps (<see cref="AverageCostChange"/>) and the balance's marker of the latest stock-in are the ones a reversal reads.
/// Document ids give each line a distinct id (as the database does): the marker compares line ids.
/// </summary>
public class ReceiptReversalCostingTests
{
    private readonly PostingEngine _engine = NewEngine();
    private readonly Product _product = NewProduct(id: 5);

    private StockDocument PostReceipt(StockBalance balance, decimal qty, decimal cost, int id = 1, bool allowNegative = false)
    {
        var doc = NewReceipt(id, (_product, qty, cost));
        GiveDistinctLineIds(doc);
        doc.Submitted();
        _engine.Post(doc, Balances(balance), NewContext(doc, allowNegative));
        return doc;
    }

    private static void GiveDistinctLineIds(StockDocument doc)
    {
        for (var i = 0; i < doc.Lines.Count; i++) doc.Lines[i].WithId(doc.Id * 100 + i + 1);
    }

    private void PostIssue(StockBalance balance, decimal qty, int id = 90)
    {
        var doc = NewIssue(id, (_product, qty));
        GiveDistinctLineIds(doc);
        doc.Submitted();
        _engine.Post(doc, Balances(balance), NewContext(doc));
    }

    private void Reverse(StockDocument doc, StockBalance balance, bool allowNegative = false) =>
        _engine.Reverse(doc, Balances(balance), NewContext(doc, allowNegative));

    private static decimal Value(StockBalance balance) => balance.QtyOnHand * balance.AvgCost;

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
        Assert.Equal(3498.6m, Value(balance));
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
        Assert.Equal(2000m, Value(balance));
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
        Assert.Equal(3213m, Value(balance));
    }

    [Fact]
    public void ReceiveIssueReverse_WhenTheIssuedUnitsAreMoreThanWhatWasThereBefore_IsBlockedByQuantity()
    {
        var balance = NewBalance(5, 10m, 4m);
        var receipt = PostReceipt(balance, 20m, 10m);
        PostIssue(balance, 25m); // 5 left: the receipt cannot come out (20 > 5)

        Assert.Throws<InsufficientStockException>(() => Reverse(receipt, balance));

        Assert.Equal(5m, balance.QtyOnHand);
        Assert.Equal(8m, balance.AvgCost); // untouched: (10 x 4 + 20 x 10) / 30
    }

    [Fact]
    public void AReceiptOfTwoLinesOfTheSameProduct_IsUndoneLastLineFirst_AndRestoresTheStartingAverage()
    {
        var balance = NewBalance(5, 10m, 3m);
        var doc = NewReceipt(1, (_product, 10m, 7m), (_product, 30m, 11m));
        GiveDistinctLineIds(doc);
        doc.Submitted();
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
        Assert.True(averageNow > 5m && averageNow < 6m);

        Reverse(second, balance);

        Assert.Equal(50m, balance.QtyOnHand);
        Assert.Equal(averageNow, balance.AvgCost); // no sound arithmetic (the value left is negative): the last known cost stays
    }

    // ----- review B1: an equal average proves nothing about what came in since -----
    [Fact]
    public void TwoReceiptsAtTheSameCost_ReverseTheFirst_KeepsTheSecondAtThatCost()
    {
        var balance = StockBalance.OpenFor(5, 1);
        var first = PostReceipt(balance, 100m, 10m, id: 1);
        PostReceipt(balance, 100m, 10m, id: 2); // the average does not move

        Reverse(first, balance);

        Assert.Equal((100m, 10m, 1000m), (balance.QtyOnHand, balance.AvgCost, Value(balance)));
    }

    [Fact]
    public void AStaffReceiptAtTheAverage_AfterTheReceipt_IsStillAStockInThatMovesTheMarker()
    {
        // Staff receipts take the average as their cost, so their snapshot is Before == After.
        var balance = NewBalance(5, 50m, 5m);
        var first = PostReceipt(balance, 100m, 10m, id: 1); // 150 @ 8.3333333333
        PostReceipt(balance, 40m, balance.AvgCost, id: 2);  // 190 @ the same

        Reverse(first, balance);

        Assert.Equal(90m, balance.QtyOnHand);
        Assert.Equal(583.3333m, Math.Round(Value(balance), 4)); // (50 x 5 + 100 x 10 + 40 x 8.33) - 1000
        Assert.NotEqual(5m, balance.AvgCost);                    // the old code gave 5: the 40 staff-received units were not worth 5
    }

    [Fact]
    public void ThreeReceipts_TheThirdAtTheCurrentAverage_ReverseTheSecond_UsesTheArithmetic()
    {
        var balance = StockBalance.OpenFor(5, 1);
        PostReceipt(balance, 100m, 10m, id: 1);
        var second = PostReceipt(balance, 100m, 20m, id: 2); // average 15
        PostReceipt(balance, 200m, 15m, id: 3);              // average stays 15

        Reverse(second, balance);

        Assert.Equal(300m, balance.QtyOnHand);
        Assert.InRange(Value(balance), 3999.99999m, 4000.00001m); // 100 @ 10 + 200 @ 15, to the 10 places of the average
        Assert.Equal(Math.Round(4000m / 300m, 10), balance.AvgCost);
    }

    [Fact]
    public void AReversedIssueIsAStockInToo_SoAnEarlierReceiptIsNotTheLatestAnyMore()
    {
        var balance = StockBalance.OpenFor(5, 1);
        var first = PostReceipt(balance, 100m, 10m, id: 1);
        var second = PostReceipt(balance, 100m, 20m, id: 2); // 200 @ 15
        var issue = NewIssue(90, (_product, 100m));
        GiveDistinctLineIds(issue);
        issue.Submitted();
        _engine.Post(issue, Balances(balance), NewContext(issue)); // 100 @ 15, booked at 15
        Reverse(second, balance);                                   // second is the latest receipt: average back to 10, 0 left
        _engine.Reverse(issue, Balances(balance), NewContext(issue)); // the 100 units come back at 15: 100 @ 15

        Assert.Equal((100m, 15m), (balance.QtyOnHand, balance.AvgCost));
        Reverse(first, balance, allowNegative: true); // first is not the latest stock-in: its 100 x 10 leave the value

        Assert.Equal(0m, balance.QtyOnHand);
        Assert.Equal(15m, balance.AvgCost); // nothing left to value: the last known cost stays
    }

    [Fact]
    public void ReversingTheLatestTwiceInARow_WalksBackThroughTheReceipts()
    {
        var balance = StockBalance.OpenFor(5, 1);
        var first = PostReceipt(balance, 100m, 10m, id: 1);
        var second = PostReceipt(balance, 100m, 20m, id: 2);
        var third = PostReceipt(balance, 50m, 40m, id: 3);

        Reverse(third, balance);
        Assert.Equal((200m, 15m), (balance.QtyOnHand, balance.AvgCost));
        Reverse(second, balance);
        Assert.Equal((100m, 10m), (balance.QtyOnHand, balance.AvgCost));
        Reverse(first, balance);
        Assert.Equal((0m, 0m), (balance.QtyOnHand, balance.AvgCost));
    }

    [Fact]
    public void AReversalThatIsNotTheLatest_MakesTheLaterSnapshotsStale_SoTheyAreNotRestored()
    {
        var balance = StockBalance.OpenFor(5, 1);
        var first = PostReceipt(balance, 100m, 10m, id: 1);
        var second = PostReceipt(balance, 100m, 20m, id: 2);
        Reverse(first, balance);                 // 100 @ 20; the second's recorded average (10 before) still contains the first

        Reverse(second, balance, allowNegative: true);

        Assert.Equal(0m, balance.QtyOnHand);
        Assert.Equal(20m, balance.AvgCost);      // not the stale 10
    }

    // ----- zero-stock edge, negative stock -----
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
    public void AFirstReceiptWhoseUnitsWereIssuedUnderNegativeStock_KeepsTheLastKnownCostInsteadOfZero()
    {
        var balance = StockBalance.OpenFor(5, 1);
        var receipt = PostReceipt(balance, 10m, 5m);
        PostIssue(balance, 10m);

        Reverse(receipt, balance, allowNegative: true);

        Assert.Equal(-10m, balance.QtyOnHand);
        Assert.Equal(5m, balance.AvgCost); // not 0: the product has a known cost
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
        GiveDistinctLineIds(issue);
        _engine.Post(issue, Balances(balance), NewContext(issue, allowNegative: true, canOverride: true));
        Assert.Equal(-5m, balance.QtyOnHand);
        var receipt = PostReceipt(balance, 8m, 10m, allowNegative: true);
        Assert.Equal((3m, 10m), (balance.QtyOnHand, balance.AvgCost));

        Reverse(receipt, balance, allowNegative: true);

        Assert.Equal((-5m, 4m), (balance.QtyOnHand, balance.AvgCost));
    }

    [Fact]
    public void NegativeStock_TwoReceiptsAtTheSameCost_ReverseTheFirst_UsesTheArithmetic()
    {
        var balance = NewBalance(5, 10m, 4m);
        var issue = NewIssue(3, (_product, 15m)).ApprovedWithOverride();
        GiveDistinctLineIds(issue);
        _engine.Post(issue, Balances(balance), NewContext(issue, allowNegative: true, canOverride: true)); // -5
        var first = PostReceipt(balance, 8m, 10m, id: 1, allowNegative: true);  // 3 @ 10
        PostReceipt(balance, 8m, 10m, id: 2, allowNegative: true);              // 11 @ 10

        Reverse(first, balance, allowNegative: true);

        Assert.Equal((3m, 10m), (balance.QtyOnHand, balance.AvgCost)); // 11 - 8 left, all of it at 10
    }

    // ----- receipts posted before the snapshot existed (no AverageCostChange) -----
    [Fact]
    public void ALegacyReceipt_WhenItIsTheLatestMovement_IsUndoneByTheArithmeticToWithinTheRoundingOfTheOldAverage()
    {
        var balance = NewBalance(5, 735m, 4.76m);
        var receipt = PostReceipt(balance, 600m, 200m);
        ForgetTheSnapshot(receipt, balance);

        Reverse(receipt, balance);

        Assert.Equal(735m, balance.QtyOnHand);
        Assert.InRange(balance.AvgCost, 4.76m - 0.000001m, 4.76m + 0.000001m);
    }

    [Fact]
    public void ALegacyReceipt_WithExtremeRatios_CanBeOffByMoreThanTheRounding_BecauseItsAverageWasStoredWithSixDecimals()
    {
        // The averages of that time were rounded to 6 decimals, and a receipt a million times the stock multiplies that rounding by a million.
        var balance = NewBalance(5, 1m, 4.1234567m);
        var receipt = PostReceipt(balance, 1_000_000m, 10.01m);
        balance.WithPrivate(nameof(StockBalance.AvgCost), Math.Round(balance.AvgCost, 6)); // what the old column held
        ForgetTheSnapshot(receipt, balance);

        Reverse(receipt, balance);

        Assert.Equal(1m, balance.QtyOnHand);
        var error = Math.Abs(balance.AvgCost - 4.1234567m);
        Assert.InRange(error, 0m, 1m);                       // bounded, but not to the 10 places the column keeps
        Assert.True(error > 0.0000001m, "the claim of exactness is only made for receipts that carry a snapshot");
    }

    [Fact]
    public void ALegacyReceipt_WithAFollowingReceipt_RemovesItsOwnValueFromTheStock()
    {
        var balance = StockBalance.OpenFor(5, 1);
        var first = PostReceipt(balance, 100m, 10m, id: 1);
        PostReceipt(balance, 100m, 20m, id: 2);
        ForgetTheSnapshot(first, balance);

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
        ForgetTheSnapshot(receipt, balance);
        var averageBefore = balance.AvgCost;

        Reverse(receipt, balance, allowNegative: true);

        Assert.Equal(0m, balance.QtyOnHand);
        Assert.Equal(averageBefore, balance.AvgCost);
    }

    // ----- precision and limits -----
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
    public void TheAverageKeepsTenDecimalPlaces_RoundedHalfAwayFromZero()
    {
        var balance = NewBalance(5, 3m, 10m);
        PostReceipt(balance, 4m, 11m); // (30 + 44) / 7

        Assert.Equal(10.5714285714m, balance.AvgCost);
    }

    [Fact]
    public void TheAverageAfterAReversalNeverExceedsWhatTheColumnCanHold()
    {
        // 1,000,000,000 units at 1,000,000,000: a value of 1e18 shared by 0.0001 remaining units would be far beyond decimal(22,10).
        var balance = NewBalance(5, 0.0002m, 1m);
        var first = PostReceipt(balance, 0.0001m, 500_000_000m, id: 1);
        PostReceipt(balance, 1_000_000_000m, 999_999_999m, id: 2);
        PostIssue(balance, 1_000_000_000m);

        Reverse(first, balance);

        Assert.True(balance.AvgCost <= Limits.MaxCostPerBaseUnit);
    }

    [Fact]
    public void ReversingAnIssue_PutsTheStockBackAtTheCostItLeftAt_AndLeavesTheAverageUnchanged()
    {
        var balance = NewBalance(5, 3m, 10m);
        PostReceipt(balance, 4m, 11m);
        var average = balance.AvgCost;
        var issue = NewIssue(7, (_product, 5m));
        GiveDistinctLineIds(issue);
        issue.Submitted();
        _engine.Post(issue, Balances(balance), NewContext(issue));

        _engine.Reverse(issue, Balances(balance), NewContext(issue));

        Assert.Equal(7m, balance.QtyOnHand);
        Assert.Equal(average, balance.AvgCost); // exactly, not within a rounding step
    }

    [Fact]
    public void ACostBeyondTheLimitOfAUnitOfMeasure_IsADomainError_NotADatabaseOverflow()
    {
        var product = NewProduct(id: 6, cartonFactor: 0.000001m);

        var ex = Assert.Throws<DomainException>(() => StockDocumentLine.Create(product, DocumentType.Receipt, 1m, CartonId, 900_000_000m, null));

        Assert.Equal(Messages.CostTooLargeForUnit, ex.Message);
    }

    [Fact]
    public void AUnitCostAboveTheLimit_IsRefused()
    {
        var ex = Assert.Throws<DomainException>(() => StockDocumentLine.Create(_product, DocumentType.Receipt, 1m, null, Limits.MaxUnitCost + 1m, null));

        Assert.Equal(Messages.CostTooLarge, ex.Message);
        Assert.Equal(Limits.MaxUnitCost, StockDocumentLine.Create(_product, DocumentType.Receipt, 1m, null, Limits.MaxUnitCost, null).UnitCost);
    }

    /// <summary>A receipt posted before the snapshot existed has none, and the balance carries no marker for it.</summary>
    private static void ForgetTheSnapshot(StockDocument receipt, StockBalance balance)
    {
        foreach (var line in receipt.Lines)
        {
            line.WithPrivate(nameof(StockDocumentLine.AvgCostBefore), null);
            line.WithPrivate(nameof(StockDocumentLine.AvgCostAfter), null);
            line.WithPrivate(nameof(StockDocumentLine.PreviousLineId), null);
        }
        balance.WithPrivate(nameof(StockBalance.LastReceiptLineId), null);
    }
}
