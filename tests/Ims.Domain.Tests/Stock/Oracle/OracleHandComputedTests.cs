namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// A few answers worked out by hand from the rules (BR-05, BR-02), not read from the engine, so the fixtures cannot have frozen a wrong number: the files and the engine are both
/// held against this arithmetic.
/// </summary>
public class OracleHandComputedTests
{
    private static IReadOnlyList<StepTrace> Run(string scenario) => EngineRunner.Run(OracleScenarios.All.Single(s => s.Name == scenario).Steps);

    [Fact]
    public void TheMovingAverageBlendsReceiptsAndIssuesLeaveItAlone()
    {
        var t = Run("weighted-average");

        Assert.Equal((20m, 3m), (t[1].Balances.Single().QtyOnHand, t[1].Balances.Single().AvgCost));                 // (10 x 2 + 10 x 4) / 20
        Assert.Equal(new TraceLedger(1, -5m, 15m, 3m, "Issue"), Assert.Single(t[2].Ledger));                          // issued at the average, 15 left
        Assert.Equal((20m, 3.75m), (t[3].Balances.Single().QtyOnHand, t[3].Balances.Single().AvgCost));               // (15 x 3 + 5 x 6) / 20
        Assert.Equal((0m, 3.75m), (t[4].Balances.Single().QtyOnHand, t[4].Balances.Single().AvgCost));                // everything issued: the last cost stays
    }

    [Fact]
    public void ACartonIsConvertedToPiecesAndItsPriceIsSpreadOverThem()
    {
        var line = Run("carton-converts-quantity-and-spreads-cost")[0].Lines.Single();

        Assert.Equal(36m, line.BaseQty);                // 3 cartons of 12
        Assert.Equal(0.8333333333m, line.UnitCost);     // 10.00 x 3 / 36, ten places, half away from zero
        Assert.Equal(10m, line.EnteredUnitCost);        // what was typed, per carton
        Assert.Equal(30m, line.Amount);                 // 3 x 10.00, with no division in between
    }

    [Fact]
    public void ReversingTheLatestReceiptPutsTheAverageAndTheLatestLineBack()
    {
        var t = Run("reversing-the-latest-receipt-restores-the-average");

        Assert.Equal(new TraceBalance(1, 10m, 2m, 1), Assert.Single(t[2].Balances));                                   // back to R1: 10 at 2, line 1 is the latest again
        Assert.Equal(new TraceLedger(1, -10m, 10m, 4m, "Reversal"), Assert.Single(t[2].Ledger));                       // the reversal row carries the receipt's own cost
        Assert.Equal(nameof(DocumentStatus.Reversed), t[2].Status);
    }

    [Fact]
    public void ReversingAnIssuePutsStockBackAtTheCostItLeftAt()
    {
        var t = Run("reversing-an-issue-puts-stock-back-at-its-cost");

        Assert.Equal(new TraceLedger(1, -12m, 8m, 4m, "Issue"), Assert.Single(t[2].Ledger));                           // average of 10 at 2 and 10 at 6
        Assert.Equal(new TraceLedger(1, +12m, 20m, 4m, "Reversal"), Assert.Single(t[3].Ledger));
        Assert.Equal(4m, Assert.Single(t[3].Balances).AvgCost);                                                       // the average did not move
    }

    [Fact]
    public void AnIssueNeedsStockUnlessAnOverrideIsAllowedRecordedAndMayBeUsed()
    {
        Assert.StartsWith(nameof(InsufficientStockException), Run("insufficient-stock-is-refused-and-leaves-no-trace")[1].Outcome);

        var t = Run("negative-stock-with-an-override");
        Assert.True(t[1].OverrideUsed);
        Assert.Equal(-3m, Assert.Single(t[1].Balances).QtyOnHand);                                                    // 2 - 5
        Assert.Equal(new TraceLedger(1, -5m, -3m, 5m, "Issue"), Assert.Single(t[1].Ledger));
        Assert.Equal((6m, 8m), (t[3].Balances.Single().QtyOnHand, t[3].Balances.Single().AvgCost));                   // -4 + 10; stock was below zero, so the new cost replaces the old average
    }

    [Fact]
    public void ACostThatFallsExactlyOnAMidpointRoundsAwayFromZero()
    {
        var balance = Run("average-at-an-exact-midpoint-rounds-away-from-zero")[2].Balances.Single();

        // 1 piece at 1.00 / 7 = 0.1428571429 plus 1 piece at 0.01, over 2: 0.07642857145, a tie in the tenth place
        Assert.Equal((2m, 0.0764285715m), (balance.QtyOnHand, balance.AvgCost));
    }

    [Fact]
    public void TakingBackAnEarlierReceiptAfterIssuesCanLeaveTheAverageAboveEveryRemainingCost()
    {
        var t = Run("reversing-an-earlier-receipt-after-issues-can-lift-the-average");

        Assert.Equal((18m, 5m), (t[2].Balances.Single().QtyOnHand, t[2].Balances.Single().AvgCost));                  // 20 units at (2 + 8) / 2, 2 issued at 5
        Assert.Equal((8m, 8.75m), (t[3].Balances.Single().QtyOnHand, t[3].Balances.Single().AvgCost));                // (18 x 5 - 10 x 2) / 8, the documented limit of reversal costing
        Assert.Contains("ReversalBlockedException", t[4].Outcome);                                                    // 8 left cannot give back 10
    }

    [Fact]
    public void ARefusedRunLeavesTheBooksAsTheyWere()
    {
        var t = Run("issue-lines-of-the-same-product-fail-together");

        Assert.Equal(t[0].Balances, t[1].Balances);     // 4 + 4 fit, the third line does not: nothing of the first two stays
        Assert.Empty(t[1].Ledger);
        Assert.Equal(nameof(DocumentStatus.Submitted), t[1].Status);
        Assert.Equal(10m, t[2].Ledger.Sum(e => -e.QtyDelta));      // 4 + 4 + 2 fit
    }
}
