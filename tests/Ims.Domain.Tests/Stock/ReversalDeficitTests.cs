using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>PS-01: a reversal that would push stock below zero is found for the whole document, naming every product, before anything moves.</summary>
public class ReversalDeficitTests
{
    private readonly PostingEngine _engine = NewEngine();

    private static Product P(int id) => NewProduct(id, $"SKU-{id}");

    [Fact]
    public void ReversingAReceipt_ListsEveryProductThatWouldGoNegative_InProductOrder()
    {
        var doc = NewReceipt(1, (P(3), 5m, 1m), (P(1), 10m, 1m), (P(2), 4m, 1m)).Submitted();
        doc.MarkPosted(Approver, Now);
        // Stock after later issues: product 1 has 6 of the 10 left, product 2 has all 4, product 3 has 0 of the 5.
        var balances = Balances(NewBalance(1, 6m), NewBalance(2, 4m), NewBalance(3, 0m));

        var deficits = _engine.FindReversalDeficits(doc, balances, Settings(allowNegative: false));

        Assert.Equal([1, 3], deficits.Keys);
        Assert.Equal(-4m, deficits[1]);
        Assert.Equal(-5m, deficits[3]);
    }

    [Fact]
    public void ReversingAReceipt_AddsUpLinesOfTheSameProduct()
    {
        var doc = NewReceipt(1, (P(1), 3m, 1m), (P(1), 4m, 1m)).Submitted();
        doc.MarkPosted(Approver, Now);
        var balances = Balances(NewBalance(1, 5m));

        var deficits = _engine.FindReversalDeficits(doc, balances, Settings());

        Assert.Equal(-2m, Assert.Single(deficits).Value);
    }

    [Fact]
    public void ReversingAReceipt_IsNotBlockedWhenEveryProductHasEnough()
    {
        var doc = NewReceipt(1, (P(1), 3m, 1m)).Submitted();
        doc.MarkPosted(Approver, Now);

        Assert.Empty(_engine.FindReversalDeficits(doc, Balances(NewBalance(1, 3m)), Settings()));
    }

    [Fact]
    public void WhenNegativeStockIsAllowed_NothingIsBlocked()
    {
        var doc = NewReceipt(1, (P(1), 3m, 1m)).Submitted();
        doc.MarkPosted(Approver, Now);

        Assert.Empty(_engine.FindReversalDeficits(doc, Balances(NewBalance(1, 0m)), Settings(allowNegative: true)));
    }

    [Fact]
    public void ReversingAnIssue_PutsStockBack_SoItIsNeverBlocked_EvenOnANegativeBalance()
    {
        var doc = NewIssue(2, (P(1), 4m)).Submitted();
        var balances = Balances(NewBalance(1, 10m, 5m));
        _engine.Post(doc, balances, NewContext(doc));

        Assert.Empty(_engine.FindReversalDeficits(doc, balances, Settings(allowNegative: false)));
    }

    [Fact]
    public void TheMessage_NamesOneProductWithItsBalanceAndUnit()
    {
        var message = Messages.ReversalBlocked("RCV-WH01-2610-0001", [new ReversalShortage(Guid.NewGuid(), "DEMO-001", "PCS", -3m)]);

        Assert.Equal("Can't reverse RCV-WH01-2610-0001. It would make DEMO-001 negative (-3 PCS). Reverse or adjust the later issues first, or ask an Admin.", message);
    }

    [Fact]
    public void TheMessage_ListsEveryProduct_WhenSeveralWouldGoNegative()
    {
        var message = Messages.ReversalBlocked("RCV-1", [new ReversalShortage(Guid.NewGuid(), "A-1", "PCS", -3m), new ReversalShortage(Guid.NewGuid(), "B-2", "BOX", -12.5m)]);

        Assert.Equal("Can't reverse RCV-1. It would make 2 products negative: A-1 (-3 PCS), B-2 (-12.5 BOX). Reverse or adjust the later issues first, or ask an Admin.", message);
    }

    [Fact]
    public void TheException_CarriesTheShortagesAndIsADomainException()
    {
        var shortage = new ReversalShortage(Guid.NewGuid(), "A-1", "PCS", -3m);

        var ex = new ReversalBlockedException("RCV-1", [shortage]);

        Assert.IsAssignableFrom<DomainException>(ex);
        Assert.Equal("RCV-1", ex.DocumentNo);
        Assert.Same(shortage, Assert.Single(ex.Shortages));
    }
}
