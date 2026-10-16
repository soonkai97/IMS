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

    // ----- Recall (the submitter takes a Submitted document back to Draft) -----
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Recall_FromSubmitted_ReturnsToAnOrdinaryDraftWithNothingOfTheSubmissionLeft(bool requiresApproval, bool isLarge, bool requiresOverride)
    {
        var doc = NewReceipt(1, (NewProduct(), 5m, 2m));
        doc.AssignNumber("RCV-WH01-2601-0001");
        doc.Submit(Submitter, requiresApproval, Now, isLarge, requiresOverride);

        doc.Recall();

        Assert.Equal(DocumentStatus.Draft, doc.Status);
        Assert.Null(doc.SubmittedBy);
        Assert.Null(doc.SubmittedAt);
        Assert.False(doc.RequiresApproval);
        Assert.False(doc.IsLarge);
        Assert.False(doc.RequiresOverride);
        Assert.False(doc.IsReturned);          // a recall is not a rejection: no "Returned" badge
        Assert.Null(doc.RejectionReason);
        Assert.Null(doc.RejectedBy);
        Assert.Equal("RCV-WH01-2601-0001", doc.No);
        Assert.Single(doc.Lines);
        Assert.Null(doc.ApprovedBy);
    }

    [Fact]
    public void Recall_LeavesADraftThatCanBeEditedAndSubmittedAgainWithTheRulesAssessedAgain()
    {
        var doc = NewReceipt(1, (NewProduct(), 500m, 2m));
        doc.Submit(Submitter, requiresApproval: true, Now, isLarge: true);
        doc.Recall();

        doc.ReplaceLines([NewLine(NewProduct(), DocumentType.Receipt, 5m, 2m, lineId: 1)]);
        doc.Submit(Approver, requiresApproval: false, Later);

        Assert.Equal(DocumentStatus.Submitted, doc.Status);
        Assert.False(doc.RequiresApproval);   // the new, smaller quantity needs no approval
        Assert.False(doc.IsLarge);
        Assert.Equal(Approver, doc.SubmittedBy);
        Assert.Equal(Later, doc.SubmittedAt);
        Assert.True(doc.CanPost);
    }

    [Fact]
    public void Recall_OfAReturnedDraftIsRefusedBecauseItIsAlreadyADraft()
    {
        var doc = InStatus(DocumentStatus.Submitted);
        doc.Reject("price looks wrong", Approver, allowSelfApproval: false, Later);

        var ex = Assert.Throws<DomainException>(doc.Recall);

        Assert.Equal(Messages.OnlySubmittedCanRecall(doc.No, DocumentStatus.Draft), ex.Message);
        Assert.True(doc.IsReturned);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void Recall_FromAnyStatusButSubmitted_ThrowsAndChangesNothing(DocumentStatus status)
    {
        var doc = InStatus(status);
        var approvedBy = doc.ApprovedBy;
        var submittedBy = doc.SubmittedBy;

        var ex = Assert.Throws<DomainException>(doc.Recall);

        Assert.Equal(Messages.OnlySubmittedCanRecall(doc.No, status), ex.Message);
        Assert.Equal(status, doc.Status);
        Assert.Equal((approvedBy, submittedBy), (doc.ApprovedBy, doc.SubmittedBy));
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
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Submitted)]
    public void Cancel_FromDraftOrSubmitted_NeedsNoReason(DocumentStatus status)
    {
        var doc = InStatus(status);

        doc.Cancel(null);

        Assert.Equal(DocumentStatus.Cancelled, doc.Status);
        Assert.Null(doc.CancelReason);
    }

    [Fact]
    public void Cancel_FromApproved_MovesToCancelledKeepingTheTrimmedReasonAndTheApprovalAsHistory()
    {
        var doc = InStatus(DocumentStatus.Approved);

        doc.Cancel("  customer withdrew the order ");

        Assert.Equal(DocumentStatus.Cancelled, doc.Status);
        Assert.Equal("customer withdrew the order", doc.CancelReason);
        Assert.Equal(Approver, doc.ApprovedBy);
        Assert.NotNull(doc.ApprovedAt);
        Assert.Null(doc.PostedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_FromApproved_WithoutAReason_ThrowsAndStaysApproved(string? reason)
    {
        var doc = InStatus(DocumentStatus.Approved);

        var ex = Assert.Throws<DomainException>(() => doc.Cancel(reason));

        Assert.Equal(Messages.CancelApprovedReasonRequired, ex.Message);
        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.Null(doc.CancelReason);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("abcd")]
    [InlineData("  abcd  ")]
    public void Cancel_FromApproved_WithAReasonUnderTheMinimum_ThrowsAndStaysApproved(string reason)
    {
        var doc = InStatus(DocumentStatus.Approved);

        var ex = Assert.Throws<DomainException>(() => doc.Cancel(reason));

        Assert.Equal(Messages.ReasonTooShort, ex.Message);
        Assert.Equal(DocumentStatus.Approved, doc.Status);
    }

    [Fact]
    public void Cancel_FromApproved_AcceptsAReasonOfExactlyTheMinimumLength()
    {
        var doc = InStatus(DocumentStatus.Approved);

        doc.Cancel(new string('a', Limits.ReasonMinLength));

        Assert.Equal(DocumentStatus.Cancelled, doc.Status);
    }

    [Theory]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void Cancel_FromAnyOtherStatus_Throws(DocumentStatus status)
    {
        var doc = InStatus(status);

        var ex = Assert.Throws<DomainException>(() => doc.Cancel("oops, wrong one"));

        Assert.Equal(Messages.OnlyDraftSubmittedOrApprovedCanCancel(status), ex.Message);
        Assert.Equal(status, doc.Status);
    }

    // ----- The whole table: every status against Recall and Cancel -----
    [Theory]
    [InlineData(DocumentStatus.Draft, false, true)]
    [InlineData(DocumentStatus.Submitted, true, true)]
    [InlineData(DocumentStatus.Approved, false, true)]
    [InlineData(DocumentStatus.Posted, false, false)]
    [InlineData(DocumentStatus.Reversed, false, false)]
    [InlineData(DocumentStatus.Cancelled, false, false)]
    public void Recall_AndCancel_AreLegalExactlyWhereFigure5SaysSo(DocumentStatus status, bool recallLegal, bool cancelLegal)
    {
        Assert.Equal(recallLegal, Succeeds(InStatus(status), d => d.Recall()));
        Assert.Equal(cancelLegal, Succeeds(InStatus(status), d => d.Cancel("a good enough reason")));
    }

    [Fact]
    public void ARecalledDocument_CannotBeApprovedOrPostedUntilItIsSubmittedAgain()
    {
        var doc = InStatus(DocumentStatus.Submitted);
        doc.Recall();

        Assert.Throws<DomainException>(() => doc.MarkPosted(Approver, Later));
        Assert.Throws<DomainException>(() => doc.Approve(Approver, false, Later));

        doc.Submit(Submitter, requiresApproval: false, Later);
        doc.MarkPosted(Approver, Later);
        Assert.Equal(DocumentStatus.Posted, doc.Status);
    }

    private static bool Succeeds(StockDocument doc, Action<StockDocument> act)
    {
        try
        {
            act(doc);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
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
