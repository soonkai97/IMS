using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

public class ReceiptPostingStrategyTests
{
    private readonly ReceiptPostingStrategy _strategy = new();
    private readonly Product _product = NewProduct(id: 5);

    private (StockDocument Doc, StockDocumentLine Line) ReceiptOf(decimal qty, decimal cost)
    {
        var doc = NewReceipt(1, (_product, qty, cost));
        return (doc, doc.Lines[0]);
    }

    [Fact]
    public void Type_IsReceipt()
    {
        Assert.Equal(DocumentType.Receipt, _strategy.Type);
    }

    [Fact]
    public void Post_WritesPositiveEntryAtLineCostAndRaisesBalance()
    {
        var (doc, line) = ReceiptOf(10m, 3m);
        var balance = StockBalance.OpenFor(5, 1);
        var ctx = NewContext(doc);

        _strategy.Post(line, balance, ctx);

        var entry = Assert.Single(ctx.Entries);
        Assert.Equal(+10m, entry.QtyDelta);
        Assert.Equal(3m, entry.UnitCost);
        Assert.Equal(LedgerReason.Receipt, entry.Reason);
        Assert.Equal(5, entry.ProductId);
        Assert.Equal(doc.Id, entry.SourceDocId);
        Assert.Equal(10m, balance.QtyOnHand);
        Assert.Equal(3m, balance.AvgCost);
    }

    [Fact]
    public void Post_OntoExistingStock_BlendsTheAverageCost()
    {
        var (doc, line) = ReceiptOf(10m, 8m);
        var balance = NewBalance(5, 10m, 4m);

        _strategy.Post(line, balance, NewContext(doc));

        Assert.Equal(20m, balance.QtyOnHand);
        Assert.Equal(6m, balance.AvgCost);
    }

    [Fact]
    public void Post_DoesNotNeedAnyOverride()
    {
        var (doc, line) = ReceiptOf(1m, 1m);
        var ctx = NewContext(doc, allowNegative: false, canOverride: false);

        _strategy.Post(line, StockBalance.OpenFor(5, 1), ctx);

        Assert.False(ctx.OverrideUsed);
    }

    [Fact]
    public void Reverse_TakesTheQuantityOutAtTheLineCostWithReversalReason()
    {
        var (doc, line) = ReceiptOf(10m, 3m);
        var balance = StockBalance.OpenFor(5, 1);
        _strategy.Post(line, balance, NewContext(doc));
        var ctx = NewContext(doc);

        _strategy.Reverse(line, balance, ctx);

        var entry = Assert.Single(ctx.Entries);
        Assert.Equal(-10m, entry.QtyDelta);
        Assert.Equal(3m, entry.UnitCost);
        Assert.Equal(LedgerReason.Reversal, entry.Reason);
        Assert.Equal(0m, balance.QtyOnHand);
    }

    [Fact]
    public void Reverse_WhenStockAlreadyConsumed_AndNegativeNotAllowed_ThrowsInsufficientStock()
    {
        var (doc, line) = ReceiptOf(10m, 3m);
        var balance = NewBalance(5, 4m, 3m); // 6 of the 10 received were issued since
        var ctx = NewContext(doc, allowNegative: false);

        Assert.Throws<InsufficientStockException>(() => _strategy.Reverse(line, balance, ctx));

        Assert.Equal(4m, balance.QtyOnHand);
        Assert.Empty(ctx.Entries);
    }

    [Fact]
    public void Reverse_WhenStockAlreadyConsumed_AndNegativeAllowed_GoesNegative()
    {
        var (doc, line) = ReceiptOf(10m, 3m);
        var balance = NewBalance(5, 4m, 3m);

        _strategy.Reverse(line, balance, NewContext(doc, allowNegative: true));

        Assert.Equal(-6m, balance.QtyOnHand);
    }
}
