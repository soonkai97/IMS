using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>PS-09 / EA-10: a reject, reversal or override reason is at least five characters once trimmed, and is stored trimmed.</summary>
public class ReasonRulesTests
{
    private static readonly DateTime Later = Now.AddHours(1);

    private static StockDocument Submitted(bool requiresOverride = false)
    {
        var doc = NewReceipt(1, (NewProduct(), 5m, 2m));
        doc.Submit(Submitter, requiresApproval: true, Now, requiresOverride: requiresOverride);
        return doc;
    }

    [Theory]
    [InlineData("abc", false)]
    [InlineData("abcd", false)]
    [InlineData("abcde", true)]
    [InlineData("  ab  ", false)]       // spaces do not count
    [InlineData("  abcde  ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReasonRequiredRule_WithAMinimum_CountsTheTrimmedLength(string? reason, bool ok)
    {
        var rule = new ReasonRequiredRule(reason, "Too short.", Limits.ReasonMinLength);

        Assert.Equal(!ok, rule.IsBroken());
    }

    [Theory]
    [InlineData("no")]
    [InlineData("  x  ")]
    public void Reject_WithAShortReason_Throws_AndLeavesTheDocumentSubmitted(string reason)
    {
        var doc = Submitted();

        var ex = Assert.Throws<DomainException>(() => doc.Reject(reason, Approver, allowSelfApproval: false, Later));

        Assert.Equal(Messages.ReasonTooShort, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
    }

    [Fact]
    public void Reject_StoresTheTrimmedReason()
    {
        var doc = Submitted();

        doc.Reject("  price looks wrong  ", Approver, allowSelfApproval: false, Later);

        Assert.Equal("price looks wrong", doc.RejectionReason);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("abcd")]
    public void MarkReversed_WithAShortReason_Throws_AndLeavesTheDocumentPosted(string reason)
    {
        var doc = NewPostedReceipt();

        var ex = Assert.Throws<DomainException>(() => doc.MarkReversed(reason));

        Assert.Equal(Messages.ReasonTooShort, ex.Message);
        Assert.Equal(DocumentStatus.Posted, doc.Status);
    }

    [Fact]
    public void MarkReversed_StoresTheTrimmedReason()
    {
        var doc = NewPostedReceipt();

        doc.MarkReversed("  wrong supplier ");

        Assert.Equal("wrong supplier", doc.ReversalReason);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("   no ")]
    public void Approve_OfAnOverride_WithAShortReason_Throws(string reason)
    {
        var doc = Submitted(requiresOverride: true);

        var ex = Assert.Throws<DomainException>(() => doc.Approve(Approver, allowSelfApproval: false, Later, reason));

        Assert.Equal(Messages.ReasonTooShort, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
    }

    [Fact]
    public void Approve_OfAnOverride_WithoutAReason_StillSaysItIsRequired()
    {
        var doc = Submitted(requiresOverride: true);

        var ex = Assert.Throws<DomainException>(() => doc.Approve(Approver, allowSelfApproval: false, Later, "   "));

        Assert.Equal(Messages.OverrideReasonRequired, ex.Message);
    }

    // ----- EA-11 / PS-10: reject is a review, like approve -----
    [Fact]
    public void Reject_BySubmitter_IsRefusedWhenSelfApprovalIsOff()
    {
        var doc = Submitted();

        var ex = Assert.Throws<DomainException>(() => doc.Reject("not needed any more", Submitter, allowSelfApproval: false, Later));

        Assert.Equal(Messages.SelfRejection, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
    }

    [Fact]
    public void Reject_BySubmitter_IsAllowedWhenSelfApprovalIsOn()
    {
        var doc = Submitted();

        doc.Reject("not needed any more", Submitter, allowSelfApproval: true, Later);

        Assert.Equal(DocumentStatus.Draft, doc.Status);
    }
}
