using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

public class IssuePostingStrategyTests
{
    private readonly IssuePostingStrategy _strategy = new();
    private readonly Product _product = NewProduct(id: 5);

    /// <summary>An issue; with a reason it has been submitted as an override and approved with that reason (as the approval step does).</summary>
    private (StockDocument Doc, StockDocumentLine Line) IssueOf(decimal qty, string? approvedReason = null)
    {
        var doc = NewIssue(2, (_product, qty));
        if (approvedReason is not null) doc.ApprovedWithOverride(approvedReason);
        return (doc, doc.Lines[0]);
    }

    [Fact]
    public void Type_IsIssue()
    {
        Assert.Equal(DocumentType.Issue, _strategy.Type);
    }

    [Fact]
    public void Post_WithEnoughStock_WritesNegativeEntryAtAverageCost()
    {
        var (doc, line) = IssueOf(4m);
        var balance = NewBalance(5, 10m, 6.5m);
        var ctx = NewContext(doc);

        _strategy.Post(line, balance, ctx);

        var entry = Assert.Single(ctx.Entries);
        Assert.Equal(-4m, entry.QtyDelta);
        Assert.Equal(6.5m, entry.UnitCost);
        Assert.Equal(LedgerReason.Issue, entry.Reason);
        Assert.Equal(5, entry.ProductId);
        Assert.Equal(doc.WarehouseId, entry.WarehouseId);
        Assert.Equal(doc.Id, entry.SourceDocId);
        Assert.Equal(Approver, entry.CreatedBy);
        Assert.Equal(Now, entry.OccurredAt);
        Assert.Equal(6m, balance.QtyOnHand);
        Assert.False(ctx.OverrideUsed);
    }

    [Fact]
    public void Post_BooksTheAverageCostOntoTheLine()
    {
        var (doc, line) = IssueOf(1m);
        Assert.Equal(0m, line.UnitCost);

        _strategy.Post(line, NewBalance(5, 3m, 2.25m), NewContext(doc));

        Assert.Equal(2.25m, line.UnitCost);
    }

    [Fact]
    public void Post_ExactlyWhatIsOnHand_IsNotAnOverride()
    {
        var (doc, line) = IssueOf(3m, "x");
        var ctx = NewContext(doc, allowNegative: true, canOverride: true);

        _strategy.Post(line, NewBalance(5, 3m, 1m), ctx);

        Assert.False(ctx.OverrideUsed);
    }

    [Fact]
    public void Post_Insufficient_NegativeSettingOff_ThrowsInsufficientStockAndChangesNothing()
    {
        var (doc, line) = IssueOf(8m, "urgent");
        var balance = NewBalance(5, 3m, 2m);
        var ctx = NewContext(doc, allowNegative: false, canOverride: true);

        var ex = Assert.Throws<InsufficientStockException>(() => _strategy.Post(line, balance, ctx));

        Assert.Equal(3m, ex.Available);
        Assert.Equal(8m, ex.Requested);
        Assert.Equal(3m, balance.QtyOnHand);
        Assert.Empty(ctx.Entries);
        Assert.False(ctx.OverrideUsed);
    }

    [Fact]
    public void Post_Insufficient_SettingOnButCallerCannotOverride_ThrowsDomainException()
    {
        var (doc, line) = IssueOf(8m, "urgent");
        var balance = NewBalance(5, 3m, 2m);
        var ctx = NewContext(doc, allowNegative: true, canOverride: false);

        var ex = Assert.Throws<DomainException>(() => _strategy.Post(line, balance, ctx));

        Assert.Equal(Messages.NegativeNeedsSupervisor, ex.Message);
        Assert.Equal(3m, balance.QtyOnHand);
        Assert.Empty(ctx.Entries);
    }

    [Fact]
    public void Post_Insufficient_NoReasonRecordedAtApproval_ThrowsDomainException()
    {
        // Stock fell short after the document was submitted, so no approver ever gave a reason; the poster cannot supply one.
        var (doc, line) = IssueOf(8m);
        var balance = NewBalance(5, 3m, 2m);
        var ctx = NewContext(doc, allowNegative: true, canOverride: true);

        var ex = Assert.Throws<DomainException>(() => _strategy.Post(line, balance, ctx));

        Assert.Equal(Messages.NegativeNeedsReason, ex.Message);
        Assert.Equal(3m, balance.QtyOnHand);
        Assert.False(ctx.OverrideUsed);
    }

    [Fact]
    public void Post_Insufficient_OverrideAllowedWithReason_GoesNegativeAndFlagsOverride()
    {
        var (doc, line) = IssueOf(8m, "customer waiting");
        var balance = NewBalance(5, 3m, 2m);
        var ctx = NewContext(doc, allowNegative: true, canOverride: true);

        _strategy.Post(line, balance, ctx);

        Assert.True(ctx.OverrideUsed);
        Assert.Equal(-5m, balance.QtyOnHand);
        var entry = Assert.Single(ctx.Entries);
        Assert.Equal(-8m, entry.QtyDelta);
        Assert.Equal(2m, entry.UnitCost);
    }

    [Fact]
    public void Post_FromNothingOnHand_OverrideBooksCostZero()
    {
        var (doc, line) = IssueOf(2m, "no stock record yet");
        var ctx = NewContext(doc, allowNegative: true, canOverride: true);

        _strategy.Post(line, StockBalance.OpenFor(5, 1), ctx);

        Assert.Equal(0m, Assert.Single(ctx.Entries).UnitCost);
    }

    [Fact]
    public void Reverse_PutsStockBackAtTheCostItLeftAt()
    {
        var (doc, line) = IssueOf(4m);
        var balance = NewBalance(5, 10m, 6.5m);
        _strategy.Post(line, balance, NewContext(doc));
        var ctx = NewContext(doc);

        _strategy.Reverse(line, balance, ctx);

        var entry = Assert.Single(ctx.Entries);
        Assert.Equal(+4m, entry.QtyDelta);
        Assert.Equal(6.5m, entry.UnitCost);
        Assert.Equal(LedgerReason.Reversal, entry.Reason);
        Assert.Equal(10m, balance.QtyOnHand);
    }

    [Fact]
    public void Reverse_AfterOtherReceiptsChangedTheAverage_ReceivesAtOriginalIssueCost()
    {
        var (doc, line) = IssueOf(4m);
        var balance = NewBalance(5, 10m, 6m);
        _strategy.Post(line, balance, NewContext(doc));   // issued at 6, 6 left @ 6
        balance.Receive(6m, 12m);                          // avg is now 9

        _strategy.Reverse(line, balance, NewContext(doc));

        Assert.Equal(16m, balance.QtyOnHand);
        // (12 * 9 + 4 * 6) / 16 = 8.25
        Assert.Equal(8.25m, balance.AvgCost);
    }
}
