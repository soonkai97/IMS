using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>A receipt saved by someone who may not enter cost takes the last known cost of each product, so it cannot drag the average towards zero.</summary>
public class KnownReceiptCostTests
{
    private static Product P(int id) => NewProduct(id, $"SKU-{id}");

    [Fact]
    public void EachLineTakesTheAverageCostOfItsProduct_OrZeroForAProductNeverStocked()
    {
        var doc = NewReceipt(1, (P(1), 4m, 99m), (P(2), 10m, 7m));

        doc.ApplyKnownReceiptCosts(new Dictionary<int, decimal> { [1] = 2.5m });

        Assert.Equal([2.5m, 0m], doc.Lines.Select(l => l.UnitCost));
    }

    [Fact]
    public void AnIssue_IsLeftAlone_AndADocumentThatIsNoLongerADraft_CannotBeRepriced()
    {
        var issue = NewIssue(2, (P(1), 4m));
        var submitted = NewReceipt(1, (P(1), 4m, 3m));
        submitted.Submit(Submitter, requiresApproval: false, Now);

        issue.ApplyKnownReceiptCosts(new Dictionary<int, decimal> { [1] = 5m });

        Assert.Equal(0m, issue.TotalAmount);
        Assert.Throws<DomainException>(() => submitted.ApplyKnownReceiptCosts(new Dictionary<int, decimal> { [1] = 5m }));
        Assert.Equal(12m, submitted.TotalAmount);
    }

    [Fact]
    public void ReceivingAtTheAverageCost_LeavesTheAverageUnchanged()
    {
        var balance = StockBalance.OpenFor(1, 1);
        balance.Receive(10m, 4m);

        balance.Receive(5m, balance.AvgCost);

        Assert.Equal((4m, 15m), (balance.AvgCost, balance.QtyOnHand));
    }
}
