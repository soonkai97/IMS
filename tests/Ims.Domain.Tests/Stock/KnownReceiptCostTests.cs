using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>A receipt saved by someone who may not enter cost takes the last known cost of each product, so it cannot drag the average towards zero.</summary>
public class KnownReceiptCostTests
{
    private static Product P(int id) => NewProduct(id, $"SKU-{id}");

    [Fact]
    public void EachLineAtTheAverageTakesTheAverageCostOfItsProduct_OrZeroForAProductNeverStocked_AndATypedCostIsLeftAlone()
    {
        var doc = StockDocument.Create(DocumentType.Receipt, NewWarehouse(), NewSupplier(), null, null, Creator, Now).WithId(1);
        doc.AddLines([
            StockDocumentLine.Create(P(1), DocumentType.Receipt, 4m, null, null, null, costFromAverage: true).WithId(1),
            StockDocumentLine.Create(P(2), DocumentType.Receipt, 10m, null, null, null, costFromAverage: true).WithId(2),
            StockDocumentLine.Create(P(3), DocumentType.Receipt, 2m, null, 7m, null).WithId(3)]);

        doc.ApplyKnownReceiptCosts(new Dictionary<int, decimal> { [1] = 2.5m, [3] = 99m });

        Assert.Equal([2.5m, 0m, 7m], doc.Lines.Select(l => l.UnitCost));
        Assert.Equal([10m, 0m, 14m], doc.Lines.Select(l => l.Amount));
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
