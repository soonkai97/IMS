using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

public class PostingEngineTests
{
    private readonly PostingEngine _engine = NewEngine();

    private static Product P(int id) => NewProduct(id, $"SKU-{id}");

    // ----- Post: receipts -----
    [Fact]
    public void Post_Receipt_AppliesEveryLineAndMarksPosted()
    {
        var doc = NewReceipt(1, (P(1), 10m, 2m), (P(2), 5m, 4m)).Submitted();
        var balances = Balances(StockBalance.OpenFor(1, 1), StockBalance.OpenFor(2, 1));
        var ctx = NewContext(doc);

        _engine.Post(doc, balances, ctx);

        Assert.Equal(DocumentStatus.Posted, doc.Status);
        Assert.Equal(Approver, doc.PostedBy);
        Assert.Equal(Now, doc.PostedAt);
        Assert.Equal(10m, balances[1].QtyOnHand);
        Assert.Equal(5m, balances[2].QtyOnHand);
        Assert.Equal(2, ctx.Entries.Count);
        Assert.All(ctx.Entries, e => Assert.Equal(LedgerReason.Receipt, e.Reason));
        Assert.All(ctx.Entries, e => Assert.True(e.QtyDelta > 0));
    }

    [Fact]
    public void Post_AppliesLinesInAscendingProductIdOrder()
    {
        var doc = NewReceipt(1, (P(30), 1m, 1m), (P(10), 1m, 1m), (P(20), 1m, 1m)).Submitted();
        var balances = Balances(StockBalance.OpenFor(10, 1), StockBalance.OpenFor(20, 1), StockBalance.OpenFor(30, 1));
        var ctx = NewContext(doc);

        _engine.Post(doc, balances, ctx);

        Assert.Equal([10, 20, 30], ctx.Entries.Select(e => e.ProductId));
    }

    [Fact]
    public void Post_TwoLinesOfTheSameProduct_AreOrderedByLineId()
    {
        var doc = NewReceipt(1, (P(1), 1m, 10m), (P(1), 3m, 20m)).Submitted();
        var ctx = NewContext(doc);

        _engine.Post(doc, Balances(StockBalance.OpenFor(1, 1)), ctx);

        Assert.Equal([1m, 3m], ctx.Entries.Select(e => e.QtyDelta));
        Assert.Equal([10m, 20m], ctx.Entries.Select(e => e.UnitCost));
    }

    [Fact]
    public void Post_NeedingApprovalButNotApproved_ThrowsApprovalRequired()
    {
        var doc = NewReceipt(1, (P(1), 1m, 1m)).Submitted(requiresApproval: true);

        var ex = Assert.Throws<DomainException>(() => _engine.Post(doc, Balances(StockBalance.OpenFor(1, 1)), NewContext(doc)));

        Assert.Equal(Messages.ApprovalRequiredToPost, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
    }

    [Fact]
    public void Post_ApprovedDocument_Posts()
    {
        var doc = NewApprovedReceipt();
        var balances = Balances(StockBalance.OpenFor(1, 1));

        _engine.Post(doc, balances, NewContext(doc));

        Assert.Equal(DocumentStatus.Posted, doc.Status);
    }

    // ----- Post: issues & negative stock overrides -----
    [Fact]
    public void Post_Issue_BooksAverageCostAndWritesNegativeEntries()
    {
        var doc = NewIssue(2, (P(1), 4m), (P(2), 1m)).Submitted();
        var balances = Balances(NewBalance(1, 10m, 5m), NewBalance(2, 3m, 7m));
        var ctx = NewContext(doc);

        _engine.Post(doc, balances, ctx);

        Assert.Equal(DocumentStatus.Posted, doc.Status);
        Assert.Equal([-4m, -1m], ctx.Entries.Select(e => e.QtyDelta));
        Assert.Equal([5m, 7m], ctx.Entries.Select(e => e.UnitCost));
        Assert.Equal([5m, 7m], doc.Lines.Select(l => l.UnitCost));
        Assert.Equal(6m, balances[1].QtyOnHand);
        Assert.Equal(2m, balances[2].QtyOnHand);
        Assert.Null(doc.NegativeStockOverrideReason);
    }

    [Fact]
    public void Post_Issue_InsufficientAndSettingOff_ThrowsInsufficientStock()
    {
        var doc = NewIssue(2, (P(1), 4m)).Submitted();

        Assert.Throws<InsufficientStockException>(() =>
            _engine.Post(doc, Balances(NewBalance(1, 1m, 5m)), NewContext(doc, allowNegative: false, canOverride: true, overrideReason: "r")));

        Assert.Equal(DocumentStatus.Submitted, doc.Status);
    }

    [Fact]
    public void Post_Issue_InsufficientSettingOnButCallerCannotOverride_ThrowsDomainException()
    {
        var doc = NewIssue(2, (P(1), 4m)).Submitted();

        var ex = Assert.Throws<DomainException>(() =>
            _engine.Post(doc, Balances(NewBalance(1, 1m, 5m)), NewContext(doc, allowNegative: true, canOverride: false, overrideReason: "r")));

        Assert.Equal(Messages.NegativeNeedsSupervisor, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
    }

    [Fact]
    public void Post_Issue_InsufficientOverrideWithoutReason_ThrowsDomainException()
    {
        var doc = NewIssue(2, (P(1), 4m)).Submitted();

        var ex = Assert.Throws<DomainException>(() =>
            _engine.Post(doc, Balances(NewBalance(1, 1m, 5m)), NewContext(doc, allowNegative: true, canOverride: true, overrideReason: null)));

        Assert.Equal(Messages.NegativeNeedsReason, ex.Message);
    }

    [Fact]
    public void Post_Issue_OverrideWithReason_PostsAndStoresTrimmedReasonOnDocument()
    {
        var doc = NewIssue(2, (P(1), 4m)).Submitted();
        var balances = Balances(NewBalance(1, 1m, 5m));
        var ctx = NewContext(doc, allowNegative: true, canOverride: true, overrideReason: "  customer collecting today  ");

        _engine.Post(doc, balances, ctx);

        Assert.Equal(DocumentStatus.Posted, doc.Status);
        Assert.True(ctx.OverrideUsed);
        Assert.Equal("customer collecting today", doc.NegativeStockOverrideReason);
        Assert.Equal(-3m, balances[1].QtyOnHand);
    }

    [Fact]
    public void Post_Issue_ReasonSuppliedButNoOverrideNeeded_ReasonIsNotStored()
    {
        var doc = NewIssue(2, (P(1), 1m)).Submitted();
        var ctx = NewContext(doc, allowNegative: true, canOverride: true, overrideReason: "just in case");

        _engine.Post(doc, Balances(NewBalance(1, 5m, 1m)), ctx);

        Assert.False(ctx.OverrideUsed);
        Assert.Null(doc.NegativeStockOverrideReason);
    }

    [Fact]
    public void Post_Issue_OneLineSufficientOneNot_TheShortLineDecidesTheOverride()
    {
        var doc = NewIssue(2, (P(1), 1m), (P(2), 9m)).Submitted();
        var balances = Balances(NewBalance(1, 5m, 1m), NewBalance(2, 1m, 1m));
        var ctx = NewContext(doc, allowNegative: true, canOverride: true, overrideReason: "ok");

        _engine.Post(doc, balances, ctx);

        Assert.True(ctx.OverrideUsed);
        Assert.Equal("ok", doc.NegativeStockOverrideReason);
    }

    // ----- Unsupported types -----
    [Fact]
    public void Post_NoStrategyForType_Throws()
    {
        var engine = new PostingEngine([new ReceiptPostingStrategy()]);
        var doc = NewIssue(2, (P(1), 1m)).Submitted();

        var ex = Assert.Throws<DomainException>(() => engine.Post(doc, Balances(NewBalance(1, 1m, 1m)), NewContext(doc)));

        Assert.Equal(Messages.PostingNotSupported(DocumentType.Issue), ex.Message);
    }

    [Fact]
    public void Reverse_NoStrategyForType_Throws()
    {
        var engine = new PostingEngine([new IssuePostingStrategy()]);
        var doc = NewPostedReceipt();

        var ex = Assert.Throws<DomainException>(() => engine.Reverse(doc, Balances(NewBalance(1, 5m, 2m)), NewContext(doc)));

        Assert.Equal(Messages.PostingNotSupported(DocumentType.Receipt), ex.Message);
    }

    [Fact]
    public void Constructor_TwoStrategiesForTheSameType_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PostingEngine([new ReceiptPostingStrategy(), new ReceiptPostingStrategy()]));
    }

    // ----- Reverse -----
    [Fact]
    public void Reverse_Receipt_ProducesOppositeEntriesAtTheSameCostInProductOrder()
    {
        var doc = NewReceipt(1, (P(2), 5m, 4m), (P(1), 10m, 2m)).Submitted();
        var balances = Balances(StockBalance.OpenFor(1, 1), StockBalance.OpenFor(2, 1));
        var post = NewContext(doc);
        _engine.Post(doc, balances, post);
        doc.MarkReversed("wrong supplier");
        var reversal = NewContext(doc);

        _engine.Reverse(doc, balances, reversal);

        Assert.Equal([1, 2], reversal.Entries.Select(e => e.ProductId));
        Assert.All(reversal.Entries, e => Assert.Equal(LedgerReason.Reversal, e.Reason));
        foreach (var original in post.Entries)
        {
            var opposite = reversal.Entries.Single(e => e.ProductId == original.ProductId);
            Assert.Equal(-original.QtyDelta, opposite.QtyDelta);
            Assert.Equal(original.UnitCost, opposite.UnitCost);
        }
        Assert.Equal(0m, balances[1].QtyOnHand);
        Assert.Equal(0m, balances[2].QtyOnHand);
    }

    [Fact]
    public void Reverse_Issue_ProducesPositiveEntriesAtTheBookedCost()
    {
        var doc = NewIssue(2, (P(1), 4m)).Submitted();
        var balances = Balances(NewBalance(1, 10m, 5m));
        _engine.Post(doc, balances, NewContext(doc));
        doc.MarkReversed("customer returned goods");
        var reversal = NewContext(doc);

        _engine.Reverse(doc, balances, reversal);

        var entry = Assert.Single(reversal.Entries);
        Assert.Equal(+4m, entry.QtyDelta);
        Assert.Equal(5m, entry.UnitCost);
        Assert.Equal(LedgerReason.Reversal, entry.Reason);
        Assert.Equal(10m, balances[1].QtyOnHand);
    }

    [Fact]
    public void PostThenReverse_NetsTheLedgerToZero()
    {
        var doc = NewReceipt(1, (P(1), 7m, 3m), (P(2), 2m, 9m)).Submitted();
        var balances = Balances(StockBalance.OpenFor(1, 1), StockBalance.OpenFor(2, 1));
        var post = NewContext(doc);
        _engine.Post(doc, balances, post);
        doc.MarkReversed("duplicate");
        var reversal = NewContext(doc);

        _engine.Reverse(doc, balances, reversal);

        var net = post.Entries.Concat(reversal.Entries).GroupBy(e => e.ProductId).ToDictionary(g => g.Key, g => g.Sum(e => e.QtyDelta));
        Assert.All(net.Values, v => Assert.Equal(0m, v));
    }
}
