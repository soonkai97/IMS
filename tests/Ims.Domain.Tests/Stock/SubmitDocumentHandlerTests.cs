using Ims.Application;
using Ims.Application.Features.Documents;
using static Ims.Domain.Tests.Stock.ShortStockTable;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// DDD-09, characterization: the short-stock decision of <c>SubmitDocumentHandler</c> (with the real <c>StockAvailability</c> reading the balances) as it is on <c>main</c>, before it moves into
/// <c>StockAvailabilityPolicy</c>. For every kind of document, setting, quantity on hand and rule answer: the same outcome, the same flags on the document, and the same steps in the same order
/// (the settings and the balances are read for an issue only; a refusal audits and saves nothing). Green before and after.
/// </summary>
public class SubmitDocumentHandlerTests
{
    public static TheoryData<int> Cases() => new(Enumerable.Range(0, All().Count()));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Submitting_IsRefusedFlaggedOrPlainAsTheTableWrites(int index)
    {
        var c = All().ElementAt(index);
        var doc = c.Document();
        var journal = new Journal();
        var balances = new FakeRepository<StockBalance>(c.OnHand > 0 ? [NewBalance(1, c.OnHand, 2m)] : [], journal, readNote: "balances");
        var checks = new NoLinkChecks(journal);
        var access = new OpenAccess();
        var handler = new SubmitDocumentHandler(new FakeRepository<StockDocument>([doc], journal), new FakeApprovals(journal, c.RulesRequireApproval), checks, new StockAvailability(balances), checks, checks, checks,
            new FakeSettings(journal, allowNegative: c.NegativeAllowed), access, new FakeReader(), access, new FakeUser(Submitter), new FakeClock(), new RecordingWork(journal), new RecordingAudit(journal));

        var thrown = await Record.ExceptionAsync(() => handler.HandleAsync(new SubmitDocumentCommand(doc.PublicId), CancellationToken.None));

        var reads = c.Type == DocumentType.Issue ? new[] { "settings", "balances" } : [];
        if (c.Expected == Outcome.Refused)
        {
            var refusal = Assert.IsType<InsufficientStockException>(thrown);
            Assert.Equal((1, doc.WarehouseId, c.OnHand, Needed), (refusal.ProductId, refusal.WarehouseId, refusal.Available, refusal.Requested));
            Assert.Equal(DocumentStatus.Draft, doc.Status);
            Assert.Equal(reads, journal);
            return;
        }

        Assert.Null(thrown);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
        Assert.Equal((c.Expected == Outcome.Override, c.GoesToAnApprover), (doc.RequiresOverride, doc.RequiresApproval));
        var valuation = new[] { "valuation", "assess", $"audit {AuditActions.DocumentSubmitted}", "save" };
        Assert.Equal([.. reads, .. valuation], journal);
    }

    [Fact]
    public async Task ProductsAreAddedUpAcrossLinesAndOnlyTheOnesShortAreRefused()
    {
        var doc = NewIssue(ReturnableTable.Id, (NewProduct(1), 3m), (NewProduct(1), 3m), (NewProduct(2, "SKU-2"), 1m));
        var journal = new Journal();
        var balances = new FakeRepository<StockBalance>([NewBalance(1, 5m, 2m), NewBalance(2, 9m, 2m)], journal, readNote: "balances");
        var checks = new NoLinkChecks(journal);
        var access = new OpenAccess();
        var handler = new SubmitDocumentHandler(new FakeRepository<StockDocument>([doc], journal), new FakeApprovals(journal), checks, new StockAvailability(balances), checks, checks, checks,
            new FakeSettings(journal), access, new FakeReader(), access, new FakeUser(Submitter), new FakeClock(), new RecordingWork(journal), new RecordingAudit(journal));

        var thrown = Assert.IsType<InsufficientStockException>(await Record.ExceptionAsync(() => handler.HandleAsync(new SubmitDocumentCommand(doc.PublicId), CancellationToken.None)));

        Assert.Equal((1, 5m, 6m), (thrown.ProductId, thrown.Available, thrown.Requested)); // 3 + 3 needed of product 1, and product 2 (1 of 9) is fine
    }
}
