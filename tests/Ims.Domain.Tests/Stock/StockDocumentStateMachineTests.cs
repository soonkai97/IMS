using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>Figure 5: Draft -> Submitted -> (Approved) -> Posted -> Reversed, or Cancelled. Every legal and illegal edge.</summary>
public class StockDocumentStateMachineTests
{
    private static readonly DateTime Later = Now.AddHours(1);

    private static StockDocument InStatus(DocumentStatus status)
    {
        var doc = NewReceipt(1, (NewProduct(), 5m, 2m));
        switch (status)
        {
            case DocumentStatus.Draft: break;
            case DocumentStatus.Submitted: doc.Submit(Submitter, true, Now); break;
            case DocumentStatus.Approved: doc.Submit(Submitter, true, Now); doc.Approve(Approver, false, Now); break;
            case DocumentStatus.Posted: doc.Submit(Submitter, false, Now); doc.MarkPosted(Approver, Now); break;
            case DocumentStatus.Reversed: doc.Submit(Submitter, false, Now); doc.MarkPosted(Approver, Now); doc.MarkReversed("wrong supplier", Now, Reverser); break;
            case DocumentStatus.Cancelled: doc.Cancel("not needed"); break;
        }
        Assert.Equal(status, doc.Status);
        return doc;
    }

    // ----- Submit -----
    [Fact]
    public void Submit_FromDraft_MovesToSubmittedAndStampsSubmitter()
    {
        var doc = InStatus(DocumentStatus.Draft);

        doc.Submit(Submitter, requiresApproval: true, Later);

        Assert.Equal(DocumentStatus.Submitted, doc.Status);
        Assert.Equal(Submitter, doc.SubmittedBy);
        Assert.Equal(Later, doc.SubmittedAt);
        Assert.True(doc.RequiresApproval);
    }

    [Fact]
    public void Submit_WithoutApprovalNeed_RecordsRequiresApprovalFalse()
    {
        var doc = InStatus(DocumentStatus.Draft);

        doc.Submit(Submitter, requiresApproval: false, Later);

        Assert.False(doc.RequiresApproval);
    }

    [Fact]
    public void Submit_Twice_Throws()
    {
        var doc = InStatus(DocumentStatus.Submitted);

        var ex = Assert.Throws<DomainException>(() => doc.Submit(Submitter, true, Later));

        Assert.Equal(Messages.OnlyDraftCanSubmit(DocumentStatus.Submitted), ex.Message);
    }

    [Theory]
    [InlineData(DocumentStatus.Submitted)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void Submit_FromAnyStatusButDraft_Throws(DocumentStatus status)
    {
        var doc = InStatus(status);

        var ex = Assert.Throws<DomainException>(() => doc.Submit(Submitter, false, Later));

        Assert.Equal(Messages.OnlyDraftCanSubmit(status), ex.Message);
        Assert.Equal(status, doc.Status);
    }

    [Fact]
    public void Submit_WithoutLines_Throws()
    {
        var doc = StockDocument.Create(DocumentType.Receipt, NewWarehouse(), null, null, null, Creator, Now);

        var ex = Assert.Throws<DomainException>(() => doc.Submit(Submitter, false, Later));

        Assert.Equal(Messages.NoLines, ex.Message);
        Assert.Equal(DocumentStatus.Draft, doc.Status);
        Assert.Null(doc.SubmittedBy);
    }

    // ----- Approve -----
    [Fact]
    public void Approve_ByAnotherUser_MovesToApproved()
    {
        var doc = InStatus(DocumentStatus.Submitted);

        doc.Approve(Approver, allowSelfApproval: false, Later);

        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.Equal(Approver, doc.ApprovedBy);
        Assert.Equal(Later, doc.ApprovedAt);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void Approve_WhenNotSubmitted_Throws(DocumentStatus status)
    {
        var doc = InStatus(status);

        var ex = Assert.Throws<DomainException>(() => doc.Approve(Approver, false, Later));

        Assert.Equal(Messages.OnlySubmittedCanApprove(status), ex.Message);
    }

    [Fact]
    public void Approve_WhenApprovalNotRequired_Throws()
    {
        var doc = NewSubmittedReceipt(requiresApproval: false);

        var ex = Assert.Throws<DomainException>(() => doc.Approve(Approver, false, Later));

        Assert.Equal(Messages.ApprovalNotNeeded, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
    }

    [Fact]
    public void Approve_BySubmitter_IsBlockedWhenSelfApprovalNotAllowed()
    {
        var doc = InStatus(DocumentStatus.Submitted);

        var ex = Assert.Throws<DomainException>(() => doc.Approve(Submitter, allowSelfApproval: false, Later));

        Assert.Equal(Messages.SelfApproval, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
        Assert.Null(doc.ApprovedBy);
    }

    [Fact]
    public void Approve_BySubmitter_IsAllowedWhenSettingPermits()
    {
        var doc = InStatus(DocumentStatus.Submitted);

        doc.Approve(Submitter, allowSelfApproval: true, Later);

        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.Equal(Submitter, doc.ApprovedBy);
    }

    // ----- Approve: override reason (BR-03) -----
    private static StockDocument SubmittedOverride()
    {
        var doc = NewIssue(2, (NewProduct(), 1m));
        doc.Submit(Submitter, requiresApproval: true, Now, requiresOverride: true);
        return doc;
    }

    [Fact]
    public void Approve_WhenOverrideRequired_StoresTheTrimmedReason()
    {
        var doc = SubmittedOverride();

        doc.Approve(Approver, allowSelfApproval: false, Later, "  customer collecting today  ");

        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.Equal("customer collecting today", doc.NegativeStockOverrideReason);
        Assert.True(doc.RequiresOverride);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Approve_WhenOverrideRequiredWithoutReason_ThrowsAndStaysSubmitted(string? reason)
    {
        var doc = SubmittedOverride();

        var ex = Assert.Throws<DomainException>(() => doc.Approve(Approver, false, Later, reason));

        Assert.Equal(Messages.OverrideReasonRequired, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
        Assert.Null(doc.ApprovedBy);
        Assert.Null(doc.NegativeStockOverrideReason);
    }

    [Fact]
    public void Approve_WhenNoOverrideRequired_NeedsNoReasonAndIgnoresOne()
    {
        var doc = InStatus(DocumentStatus.Submitted);

        doc.Approve(Approver, allowSelfApproval: false, Later, "not needed here");

        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.Null(doc.NegativeStockOverrideReason);
    }

    [Fact]
    public void Approve_BySubmitterOfAnOverride_StillBlockedWhenSelfApprovalNotAllowed()
    {
        var doc = SubmittedOverride();

        var ex = Assert.Throws<DomainException>(() => doc.Approve(Submitter, allowSelfApproval: false, Later, "a good reason"));

        Assert.Equal(Messages.SelfApproval, ex.Message);
    }

    [Fact]
    public void Reject_OfAnOverride_LeavesNoReasonBehind()
    {
        var doc = SubmittedOverride();

        doc.Reject("fix the qty", Approver, allowSelfApproval: false, Later);

        Assert.Null(doc.NegativeStockOverrideReason);
    }

    // ----- Reject -----
    [Fact]
    public void Reject_FromSubmitted_ReturnsToDraftKeepingReasonAndClearingSubmitter()
    {
        var doc = InStatus(DocumentStatus.Submitted);

        doc.Reject("price looks wrong", Approver, allowSelfApproval: false, Later);

        Assert.Equal(DocumentStatus.Draft, doc.Status);
        Assert.Equal("price looks wrong", doc.RejectionReason);
        Assert.Null(doc.SubmittedBy);
        Assert.Null(doc.SubmittedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithoutReason_Throws(string? reason)
    {
        var doc = InStatus(DocumentStatus.Submitted);

        var ex = Assert.Throws<DomainException>(() => doc.Reject(reason!, Approver, allowSelfApproval: false, Later));

        Assert.Equal(Messages.RejectReasonRequired, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
        Assert.Equal(Submitter, doc.SubmittedBy);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void Reject_WhenNotSubmitted_Throws(DocumentStatus status)
    {
        var doc = InStatus(status);

        var ex = Assert.Throws<DomainException>(() => doc.Reject("no", Approver, allowSelfApproval: false, Later));

        Assert.Equal(Messages.OnlySubmittedCanReject(status), ex.Message);
    }

    [Fact]
    public void Resubmit_AfterReject_ClearsRejectionReasonAndAllowsEditing()
    {
        var doc = InStatus(DocumentStatus.Submitted);
        doc.Reject("fix the qty", Approver, allowSelfApproval: false, Later);

        doc.ReplaceLines([NewLine(NewProduct(), DocumentType.Receipt, 7m, 2m)]); // editable again
        doc.Submit(Submitter, true, Later);

        Assert.Equal(DocumentStatus.Submitted, doc.Status);
        Assert.Null(doc.RejectionReason);
    }

    [Fact]
    public void Reject_RecordsWhoReturnedItAndWhen_AndMarksTheDraftReturned()
    {
        var doc = InStatus(DocumentStatus.Submitted);

        doc.Reject("price looks wrong", Approver, allowSelfApproval: false, Later);

        Assert.Equal(Approver, doc.RejectedBy);
        Assert.Equal(Later, doc.RejectedAt);
        Assert.True(doc.IsReturned);
    }

    [Fact]
    public void Reject_ClearsTheSubmitTimeFlags_BecauseADraftHasNoneOfThem()
    {
        var doc = NewReceipt(1, (NewProduct(), 5m, 2m));
        doc.Submit(Submitter, requiresApproval: true, Now, isLarge: true, requiresOverride: true);

        doc.Reject("fix it", Approver, allowSelfApproval: false, Later);

        Assert.False(doc.RequiresApproval);
        Assert.False(doc.IsLarge);
        Assert.False(doc.RequiresOverride);
    }

    [Fact]
    public void Resubmit_AfterReject_ClearsWhoReturnedItAndNoLongerCountsAsReturned()
    {
        var doc = InStatus(DocumentStatus.Submitted);
        doc.Reject("fix the qty", Approver, allowSelfApproval: false, Later);

        doc.Submit(Submitter, true, Later.AddHours(1));

        Assert.Null(doc.RejectedBy);
        Assert.Null(doc.RejectedAt);
        Assert.False(doc.IsReturned);
    }

    [Fact]
    public void IsReturned_IsFalseForADraftThatWasNeverRejected()
    {
        Assert.False(InStatus(DocumentStatus.Draft).IsReturned);
    }

    // ----- Submit flags -----
    [Fact]
    public void Submit_StoresTheLargeAndOverrideFlags()
    {
        var doc = NewReceipt(1, (NewProduct(), 5m, 2m));

        doc.Submit(Submitter, requiresApproval: true, Now, isLarge: true, requiresOverride: true);

        Assert.True(doc.IsLarge);
        Assert.True(doc.RequiresOverride);
    }

    [Fact]
    public void Submit_WithoutApproval_IsNeverLarge()
    {
        var doc = NewReceipt(1, (NewProduct(), 5m, 2m));

        doc.Submit(Submitter, requiresApproval: false, Now, isLarge: true);

        Assert.False(doc.IsLarge);
    }

    // ----- Cancel -----
    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Submitted)]
    public void Cancel_FromDraftOrSubmitted_MovesToCancelledKeepingReason(DocumentStatus status)
    {
        var doc = InStatus(status);

        doc.Cancel("duplicate");

        Assert.Equal(DocumentStatus.Cancelled, doc.Status);
        Assert.Equal("duplicate", doc.CancelReason);
    }

    [Theory]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void Cancel_FromAnyOtherStatus_Throws(DocumentStatus status)
    {
        var doc = InStatus(status);

        var ex = Assert.Throws<DomainException>(() => doc.Cancel("oops"));

        Assert.Equal(Messages.OnlyDraftOrSubmittedCanCancel(status), ex.Message);
        Assert.Equal(status, doc.Status);
    }

    // ----- Post -----
    [Fact]
    public void CanPost_IsTrueForApproved()
    {
        Assert.True(InStatus(DocumentStatus.Approved).CanPost);
    }

    [Fact]
    public void CanPost_IsTrueForSubmittedWithoutApprovalNeed()
    {
        Assert.True(NewSubmittedReceipt(requiresApproval: false).CanPost);
    }

    [Fact]
    public void CanPost_IsFalseForSubmittedThatNeedsApproval()
    {
        Assert.False(NewSubmittedReceipt(requiresApproval: true).CanPost);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void CanPost_IsFalseForOtherStatuses(DocumentStatus status)
    {
        Assert.False(InStatus(status).CanPost);
    }

    [Fact]
    public void MarkPosted_FromApproved_MovesToPostedAndStampsPoster()
    {
        var doc = InStatus(DocumentStatus.Approved);

        doc.MarkPosted(Approver, Later);

        Assert.Equal(DocumentStatus.Posted, doc.Status);
        Assert.Equal(Approver, doc.PostedBy);
        Assert.Equal(Later, doc.PostedAt);
        Assert.Null(doc.NegativeStockOverrideReason);
    }

    [Fact]
    public void MarkPosted_FromSubmittedWithoutApproval_MovesToPosted()
    {
        var doc = NewSubmittedReceipt(requiresApproval: false);

        doc.MarkPosted(Approver, Later);

        Assert.Equal(DocumentStatus.Posted, doc.Status);
    }

    [Fact]
    public void MarkPosted_FromSubmittedThatNeedsApproval_Throws()
    {
        var doc = NewSubmittedReceipt(requiresApproval: true);

        var ex = Assert.Throws<DomainException>(() => doc.MarkPosted(Approver, Later));

        Assert.Equal(Messages.ApprovalRequiredToPost, ex.Message);
        Assert.Equal(DocumentStatus.Submitted, doc.Status);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void MarkPosted_FromOtherStatuses_Throws(DocumentStatus status)
    {
        var doc = InStatus(status);

        var ex = Assert.Throws<DomainException>(() => doc.MarkPosted(Approver, Later));

        Assert.Equal(Messages.CannotPostInStatus(status), ex.Message);
        Assert.Equal(status, doc.Status);
    }

    [Fact]
    public void MarkPosted_KeepsTheOverrideReasonRecordedAtApproval()
    {
        var doc = NewIssue(2, (NewProduct(), 1m)).ApprovedWithOverride("counted by hand");

        doc.MarkPosted(Approver, Later);

        Assert.Equal("counted by hand", doc.NegativeStockOverrideReason);
    }

    [Fact]
    public void EnsureCanPost_OnPostedDocument_Throws()
    {
        var doc = InStatus(DocumentStatus.Posted);

        Assert.Throws<DomainException>(doc.EnsureCanPost);
    }

    // ----- Reverse -----
    [Fact]
    public void MarkReversed_FromPosted_MovesToReversedKeepingReason()
    {
        var doc = InStatus(DocumentStatus.Posted);

        doc.MarkReversed("wrong supplier", Later, Reverser);

        Assert.Equal(DocumentStatus.Reversed, doc.Status);
        Assert.Equal("wrong supplier", doc.ReversalReason);
        Assert.Equal(Later, doc.ReversedAt);
        Assert.Equal(Reverser, doc.ReversedBy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MarkReversed_WithoutARealUser_Throws_AndLeavesTheDocumentPosted(int reversedBy)
    {
        var doc = InStatus(DocumentStatus.Posted);

        Assert.Throws<ArgumentOutOfRangeException>(() => doc.MarkReversed("wrong supplier", Later, reversedBy));

        Assert.Equal(DocumentStatus.Posted, doc.Status);
        Assert.Null(doc.ReversedBy);
    }

    [Fact]
    public void MarkReversed_WithoutAReason_LeavesNoReversalTime()
    {
        var doc = InStatus(DocumentStatus.Posted);

        Assert.Throws<DomainException>(() => doc.MarkReversed(" ", Later, Reverser));

        Assert.Null(doc.ReversedAt);
        Assert.Null(doc.ReversedBy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MarkReversed_WithoutReason_Throws(string? reason)
    {
        var doc = InStatus(DocumentStatus.Posted);

        var ex = Assert.Throws<DomainException>(() => doc.MarkReversed(reason!, Now, Reverser));

        Assert.Equal(Messages.ReverseReasonRequired, ex.Message);
        Assert.Equal(DocumentStatus.Posted, doc.Status);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Submitted)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void MarkReversed_FromAnyStatusButPosted_Throws(DocumentStatus status)
    {
        var doc = InStatus(status);

        var ex = Assert.Throws<DomainException>(() => doc.MarkReversed("reason", Now, Reverser));

        Assert.Equal(Messages.OnlyPostedCanReverse, ex.Message);
        Assert.Equal(status, doc.Status);
    }

    // ----- Editing is only allowed in Draft -----
    [Fact]
    public void EnsureEditable_InDraft_DoesNotThrow()
    {
        InStatus(DocumentStatus.Draft).EnsureEditable();
    }

    [Theory]
    [InlineData(DocumentStatus.Submitted)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void EnsureEditable_OutsideDraft_Throws(DocumentStatus status)
    {
        var doc = InStatus(status);

        var ex = Assert.Throws<DomainException>(doc.EnsureEditable);

        Assert.Equal(Messages.NotEditable(doc.No, status), ex.Message);
    }

    // ----- Terminal states cannot move anywhere -----
    [Fact]
    public void ReversedDocument_CannotBeReversedAgainOrPostedOrCancelled()
    {
        var doc = InStatus(DocumentStatus.Reversed);

        Assert.Throws<DomainException>(() => doc.MarkReversed("again", Now, Reverser));
        Assert.Throws<DomainException>(() => doc.MarkPosted(Approver, Later));
        Assert.Throws<DomainException>(() => doc.Cancel("x"));
        Assert.Equal(DocumentStatus.Reversed, doc.Status);
    }

    [Fact]
    public void CancelledDocument_CannotBeSubmittedOrPosted()
    {
        var doc = InStatus(DocumentStatus.Cancelled);

        Assert.Throws<DomainException>(() => doc.Submit(Submitter, false, Later));
        Assert.Throws<DomainException>(() => doc.MarkPosted(Approver, Later));
        Assert.Equal(DocumentStatus.Cancelled, doc.Status);
    }

    [Fact]
    public void FullHappyPath_WithApproval_DraftToReversed()
    {
        var doc = InStatus(DocumentStatus.Draft);

        doc.Submit(Submitter, true, Now);
        doc.Approve(Approver, false, Now);
        doc.MarkPosted(Approver, Now);
        doc.MarkReversed("mistake", Now, Reverser);

        Assert.Equal(DocumentStatus.Reversed, doc.Status);
    }
}
