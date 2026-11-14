using Ims.Application;
using Ims.Application.Features.Documents;
using static Ims.Domain.Tests.Stock.SelfReviewTable;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// DDD-09, characterization: <c>ApproveDocumentHandler</c> and <c>RejectDocumentHandler</c> as they are on <c>main</c>, before "setting and not a return" moves into <c>StockDocument</c>.
/// For every setting, kind of document and reviewer: the same answer (the document moves, or the domain refuses in the sentence of the rule) and the same steps (a refusal writes no audit row
/// and saves nothing). Green before and after. The 403 for a reviewer outside their limit is not this rule and stays in the handler, asked first.
/// </summary>
public class ReviewDocumentHandlerTests
{
    public static TheoryData<int> Cases() => new(Enumerable.Range(0, All().Count()));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Approving_IsAllowedOrRefusedAsTheTableWrites(int index)
    {
        var c = All().ElementAt(index);
        var doc = Submitted(c.Kind);
        var journal = new Journal();
        var access = new OpenAccess();
        var handler = new ApproveDocumentHandler(new FakeRepository<StockDocument>([doc], journal), new FakeApprovals(journal), new FakeSettings(journal, allowSelfApproval: c.SettingOn),
            access, new FakeReader(), access, new FakeUser(c.Reviewer), new FakeClock(), new RecordingWork(journal), new RecordingAudit(journal));

        var thrown = await Record.ExceptionAsync(() => handler.HandleAsync(new ApproveDocumentCommand(doc.PublicId, new ApproveRequest(null)), CancellationToken.None));

        if (c.Allowed)
        {
            Assert.Null(thrown);
            Assert.Equal(DocumentStatus.Approved, doc.Status);
            Assert.Equal(["settings", $"audit {AuditActions.DocumentApproved}", "save"], journal);
        }
        else
        {
            Assert.Equal(Messages.SelfApproval, Assert.IsType<DomainException>(thrown).Message);
            Assert.Equal(DocumentStatus.Submitted, doc.Status);
            Assert.Equal(["settings"], journal);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Rejecting_IsAllowedOrRefusedAsTheTableWrites(int index)
    {
        var c = All().ElementAt(index);
        var doc = Submitted(c.Kind);
        var journal = new Journal();
        var access = new OpenAccess();
        var handler = new RejectDocumentHandler(new FakeRepository<StockDocument>([doc], journal), new FakeApprovals(journal), new FakeSettings(journal, allowSelfApproval: c.SettingOn),
            access, new FakeReader(), access, new FakeUser(c.Reviewer), new FakeClock(), new RecordingWork(journal), new RecordingAudit(journal));

        var thrown = await Record.ExceptionAsync(() => handler.HandleAsync(new RejectDocumentCommand(doc.PublicId, new RejectRequest("count it again")), CancellationToken.None));

        if (c.Allowed)
        {
            Assert.Null(thrown);
            Assert.Equal((DocumentStatus.Draft, "count it again"), (doc.Status, doc.RejectionReason));
            Assert.Equal(["settings", $"audit {AuditActions.DocumentRejected}", "save"], journal);
        }
        else
        {
            Assert.Equal(Messages.SelfRejection, Assert.IsType<DomainException>(thrown).Message);
            Assert.Equal(DocumentStatus.Submitted, doc.Status);
            Assert.Equal(["settings"], journal);
        }
    }
}
