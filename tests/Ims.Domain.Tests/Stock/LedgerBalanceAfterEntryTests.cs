using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>Every ledger entry carries what its product held right after it (PS-14), set by the posting run when it creates the entry, before the row is inserted.</summary>
public class LedgerBalanceAfterEntryTests
{
    private readonly PostingEngine _engine = NewEngine();

    private static Product P(int id) => NewProduct(id, $"SKU-{id}");

    [Fact]
    public void Posting_RecordsOnEachEntryTheBalanceAfterIt()
    {
        var doc = NewReceipt(1, (P(1), 10m, 2m), (P(2), 5m, 4m)).Submitted();
        var balances = Balances(NewBalance(1, 3m, 1m), StockBalance.OpenFor(2, 1));
        var ctx = NewContext(doc);
        _engine.Post(doc, balances, ctx);


        Assert.Equal(13m, ctx.Entries.Single(e => e.ProductId == 1).BalanceAfter);
        Assert.Equal(5m, ctx.Entries.Single(e => e.ProductId == 2).BalanceAfter);
    }

    [Fact]
    public void TwoLinesOfTheSameProduct_EachShowTheRunningBalance()
    {
        var doc = NewReceipt(1, (P(1), 4m, 1m), (P(1), 6m, 1m)).Submitted();
        var balances = Balances(NewBalance(1, 10m, 1m));
        var ctx = NewContext(doc);
        _engine.Post(doc, balances, ctx);


        Assert.Equal([4m, 6m], ctx.Entries.Select(e => e.QtyDelta));
        Assert.Equal([14m, 20m], ctx.Entries.Select(e => e.BalanceAfter));
        Assert.Equal(balances[1].QtyOnHand, ctx.Entries[^1].BalanceAfter); // the last entry ends at the balance
    }


    [Fact]
    public void AReversal_RecordsTheBalanceAfterItLikeAnyOtherMovement()
    {
        var doc = NewReceipt(1, (P(1), 7m, 3m)).Submitted();
        var balances = Balances(NewBalance(1, 10m, 3m));
        _engine.Post(doc, balances, NewContext(doc));
        doc.MarkReversed("entered twice", Now, Reverser);
        var reversal = NewContext(doc);
        _engine.Reverse(doc, balances, reversal);

        Assert.Equal(10m, Assert.Single(reversal.Entries).BalanceAfter);
    }
}
