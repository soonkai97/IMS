namespace Ims.Domain.Tests.Stock;

/// <summary>
/// Status x role x requiresApproval x large x who created / submitted it. The single source of truth for allowedActions
/// (PRD 3.1, UX 7.2, BR-07): the ownership rules of Warehouse staff and Clerk, and the reviewer a document is routed to.
/// </summary>
public class DocumentActionPolicyTests
{
    private const int Me = 42;
    private const int SomeoneElse = 43;

    private static DocumentActionContext Doc(DocumentStatus status, bool requiresApproval = false, bool isLarge = false,
        int createdBy = SomeoneElse, int? submittedBy = SomeoneElse) =>
        new(status, requiresApproval, isLarge, createdBy, submittedBy);

    private static string[] Actions(string role, DocumentStatus status, bool requiresApproval = false, bool isLarge = false,
        int createdBy = SomeoneElse, int? submittedBy = SomeoneElse, bool allowSelf = false, bool withinLimit = true) =>
        DocumentActionPolicy.AllowedActions(role, Me, Doc(status, requiresApproval, isLarge, createdBy, submittedBy), allowSelf, withinLimit).Order().ToArray();

    private static string[] Sorted(params string[] actions) => actions.Order().ToArray();

    // ----- Draft: Admin and Supervisor act on any draft; Warehouse staff and Clerk only on their own (UX 7.2) -----
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    public void Draft_AdminAndSupervisor_MayEditSubmitAndCancelAnyDraft(string role)
    {
        Assert.Equal(Sorted(DocumentActions.Edit, DocumentActions.Submit, DocumentActions.Cancel), Actions(role, DocumentStatus.Draft, createdBy: SomeoneElse));
    }

    [Theory]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public void Draft_StaffAndClerk_MayEditSubmitAndCancelTheirOwnDraft(string role)
    {
        Assert.Equal(Sorted(DocumentActions.Edit, DocumentActions.Submit, DocumentActions.Cancel), Actions(role, DocumentStatus.Draft, createdBy: Me, submittedBy: null));
    }

    [Theory]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public void Draft_StaffAndClerk_HaveNoActionOnSomeoneElsesDraft(string role)
    {
        Assert.Empty(Actions(role, DocumentStatus.Draft, createdBy: SomeoneElse, submittedBy: null));
    }

    [Fact]
    public void Draft_Owner_CannotDoAnything()
    {
        Assert.Empty(Actions(Roles.Owner, DocumentStatus.Draft, createdBy: Me));
    }

    // ----- Submitted, approval needed: the reviewer the document is routed to -----
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    public void SubmittedNeedingApproval_AdminAndSupervisor_MayApproveRejectAndCancel(string role)
    {
        Assert.Equal(
            Sorted(DocumentActions.Approve, DocumentActions.Reject, DocumentActions.Cancel),
            Actions(role, DocumentStatus.Submitted, requiresApproval: true));
    }

    [Fact]
    public void SubmittedNeedingApproval_OwnerOnALargeDocument_MayApproveAndRejectButNotCancel()
    {
        Assert.Equal(
            Sorted(DocumentActions.Approve, DocumentActions.Reject),
            Actions(Roles.Owner, DocumentStatus.Submitted, requiresApproval: true, isLarge: true));
    }

    [Fact]
    public void SubmittedNeedingApproval_OwnerOnANonLargeDocument_IsViewOnly()
    {
        Assert.Empty(Actions(Roles.Owner, DocumentStatus.Submitted, requiresApproval: true, isLarge: false));
    }

    [Fact]
    public void SubmittedNeedingApproval_SupervisorOnALargeDocument_MayOnlyCancel()
    {
        // Owner-level documents are not the supervisor's to approve or reject (PS-07): the API would answer 403.
        Assert.Equal(Sorted(DocumentActions.Cancel), Actions(Roles.Supervisor, DocumentStatus.Submitted, requiresApproval: true, isLarge: true));
    }

    [Fact]
    public void SubmittedNeedingApproval_AdminOnALargeDocument_MayApproveAndReject()
    {
        Assert.Contains(DocumentActions.Approve, Actions(Roles.Admin, DocumentStatus.Submitted, requiresApproval: true, isLarge: true));
    }

    [Fact]
    public void SubmittedNeedingApproval_ApproverWhoseRoleIsBelowTheRulesInForce_IsNotOfferedApproveOrReject()
    {
        var actions = Actions(Roles.Admin, DocumentStatus.Submitted, requiresApproval: true, withinLimit: false);

        Assert.DoesNotContain(DocumentActions.Approve, actions);
        Assert.DoesNotContain(DocumentActions.Reject, actions);
    }

    // ----- Submitted: Warehouse staff and Clerk may only recall or cancel what they submitted -----
    [Theory]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public void SubmittedNeedingApproval_StaffAndClerk_MayRecallAndCancelOnlyWhatTheySubmitted(string role)
    {
        Assert.Equal(Sorted(DocumentActions.Recall, DocumentActions.Cancel), Actions(role, DocumentStatus.Submitted, requiresApproval: true, submittedBy: Me));
        Assert.Empty(Actions(role, DocumentStatus.Submitted, requiresApproval: true, createdBy: Me, submittedBy: SomeoneElse));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    public void SubmittedNeedingApproval_AdminAndSupervisor_MayRecallOnlyWhatTheySubmitted(string role)
    {
        Assert.Contains(DocumentActions.Recall, Actions(role, DocumentStatus.Submitted, requiresApproval: true, submittedBy: Me));
        Assert.DoesNotContain(DocumentActions.Recall, Actions(role, DocumentStatus.Submitted, requiresApproval: true, submittedBy: SomeoneElse));
    }

    [Fact]
    public void SubmittedNeedingApproval_Owner_CanNeverRecallBecauseTheOwnerCreatesNothing()
    {
        Assert.DoesNotContain(DocumentActions.Recall, Actions(Roles.Owner, DocumentStatus.Submitted, requiresApproval: true, isLarge: true, submittedBy: Me));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Owner)]
    public void SubmittedNeedingApproval_OwnSubmission_CannotApproveOrReject(string role)
    {
        var actions = Actions(role, DocumentStatus.Submitted, requiresApproval: true, isLarge: role == Roles.Owner, submittedBy: Me, allowSelf: false);

        Assert.DoesNotContain(DocumentActions.Approve, actions);
        Assert.DoesNotContain(DocumentActions.Reject, actions);
    }

    [Fact]
    public void SubmittedNeedingApproval_OwnSubmission_StillMayRecallAndCancel()
    {
        var actions = Actions(Roles.Supervisor, DocumentStatus.Submitted, requiresApproval: true, submittedBy: Me);

        Assert.Equal(Sorted(DocumentActions.Recall, DocumentActions.Cancel), actions);
    }

    [Fact]
    public void SubmittedNeedingApproval_OwnSubmission_AllowedWhenSettingPermitsSelfApproval()
    {
        var actions = Actions(Roles.Supervisor, DocumentStatus.Submitted, requiresApproval: true, submittedBy: Me, allowSelf: true);

        Assert.Contains(DocumentActions.Approve, actions);
        Assert.Contains(DocumentActions.Reject, actions);
    }

    [Fact]
    public void SubmittedNeedingApproval_NobodyMayPostYet()
    {
        foreach (var role in Roles.All)
            Assert.DoesNotContain(DocumentActions.Post, Actions(role, DocumentStatus.Submitted, requiresApproval: true));
    }

    // ----- Submitted, no approval needed -----
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    public void SubmittedWithoutApproval_PostingRoles_MayPostAnyDocumentInScope(string role)
    {
        Assert.Contains(DocumentActions.Post, Actions(role, DocumentStatus.Submitted, requiresApproval: false, submittedBy: SomeoneElse));
    }

    [Theory]
    [InlineData(Roles.Clerk)]
    [InlineData(Roles.Owner)]
    public void SubmittedWithoutApproval_NonPostingRoles_CannotPost(string role)
    {
        Assert.DoesNotContain(DocumentActions.Post, Actions(role, DocumentStatus.Submitted, requiresApproval: false));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Owner)]
    public void SubmittedWithoutApproval_NoApproveOrRejectForAnyone(string role)
    {
        var actions = Actions(role, DocumentStatus.Submitted, requiresApproval: false);

        Assert.DoesNotContain(DocumentActions.Approve, actions);
        Assert.DoesNotContain(DocumentActions.Reject, actions);
    }

    [Fact]
    public void SubmittedWithoutApproval_PostingIsNotBlockedBySelfSubmission()
    {
        var actions = Actions(Roles.WarehouseStaff, DocumentStatus.Submitted, requiresApproval: false, submittedBy: Me);

        Assert.Equal(Sorted(DocumentActions.Post, DocumentActions.Recall, DocumentActions.Cancel), actions);
    }

    [Fact]
    public void SubmittedWithoutApproval_AClerkWhoSubmittedItMayRecallAndCancelButNotPost()
    {
        Assert.Equal(Sorted(DocumentActions.Recall, DocumentActions.Cancel), Actions(Roles.Clerk, DocumentStatus.Submitted, requiresApproval: false, submittedBy: Me));
    }

    [Fact]
    public void SubmittedWithoutApproval_SomeoneWhoDidNotSubmitItCannotRecallItEvenIfTheyMayPostIt()
    {
        Assert.Equal(Sorted(DocumentActions.Post, DocumentActions.Cancel), Actions(Roles.Supervisor, DocumentStatus.Submitted, requiresApproval: false, submittedBy: SomeoneElse));
        Assert.Equal(Sorted(DocumentActions.Post), Actions(Roles.WarehouseStaff, DocumentStatus.Submitted, requiresApproval: false, submittedBy: SomeoneElse));
    }

    // ----- Approved: post by those who may; cancel by the reviewer the document is routed to -----
    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, false)] // PRD 3.1: staff post only documents that never needed approval
    [InlineData(Roles.Clerk, false)]
    [InlineData(Roles.Owner, false)]
    public void Approved_OnlyAdminAndSupervisorMayPost(string role, bool mayPost)
    {
        var actions = Actions(role, DocumentStatus.Approved, requiresApproval: true);

        Assert.Equal(mayPost, actions.Contains(DocumentActions.Post));
        Assert.DoesNotContain(DocumentActions.Edit, actions);
        Assert.DoesNotContain(DocumentActions.Recall, actions); // an approval is someone else's decision: the creator cannot pull it back alone
    }

    [Theory]
    [InlineData(Roles.Admin, false, true)]
    [InlineData(Roles.Supervisor, false, true)]
    [InlineData(Roles.Owner, false, false)]       // the Owner reviews only large documents
    [InlineData(Roles.WarehouseStaff, false, false)]
    [InlineData(Roles.Clerk, false, false)]
    [InlineData(Roles.Admin, true, true)]
    [InlineData(Roles.Supervisor, true, false)]   // large: not the supervisor's
    [InlineData(Roles.Owner, true, true)]         // the Owner may cancel although the Owner cannot post
    [InlineData(Roles.WarehouseStaff, true, false)]
    [InlineData(Roles.Clerk, true, false)]
    public void Approved_OnlyTheReviewerTheDocumentIsRoutedToMayCancel(string role, bool isLarge, bool mayCancel)
    {
        var actions = Actions(role, DocumentStatus.Approved, requiresApproval: true, isLarge: isLarge);

        Assert.Equal(mayCancel, actions.Contains(DocumentActions.Cancel));
    }

    [Fact]
    public void Approved_TheOwnerOfALargeDocumentMayOnlyCancel_AndAnApproverWhoSubmittedItIsNotExcluded()
    {
        Assert.Equal(Sorted(DocumentActions.Cancel), Actions(Roles.Owner, DocumentStatus.Approved, requiresApproval: true, isLarge: true));
        Assert.Equal(Sorted(DocumentActions.Post, DocumentActions.Cancel),
            Actions(Roles.Supervisor, DocumentStatus.Approved, requiresApproval: true, createdBy: Me, submittedBy: Me));
    }

    [Fact]
    public void Approved_TheCreatorWhoCannotReviewGetsNothing()
    {
        Assert.Empty(Actions(Roles.Clerk, DocumentStatus.Approved, requiresApproval: true, createdBy: Me, submittedBy: Me));
        Assert.Empty(Actions(Roles.WarehouseStaff, DocumentStatus.Approved, requiresApproval: true, createdBy: Me, submittedBy: Me));
    }

    // ----- Posted / Reversed / Cancelled -----
    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void Posted_OnlyAdminAndSupervisorMayReverse(string role, bool mayReverse)
    {
        var actions = Actions(role, DocumentStatus.Posted);

        Assert.Equal(mayReverse ? Sorted(DocumentActions.Reverse) : [], actions);
    }

    [Theory]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void TerminalStatuses_OfferNoActionToAnyRole(DocumentStatus status)
    {
        foreach (var role in Roles.All)
            Assert.Empty(Actions(role, status));
    }

    [Fact]
    public void UnknownRole_GetsNothingInAnyStatus()
    {
        foreach (var status in Enum.GetValues<DocumentStatus>())
            Assert.Empty(Actions("Nobody", status, requiresApproval: true));
    }

    // ----- The guards the command handlers use agree with the action list -----
    [Theory]
    [InlineData(Roles.Admin, SomeoneElse, true)]
    [InlineData(Roles.Supervisor, SomeoneElse, true)]
    [InlineData(Roles.WarehouseStaff, Me, true)]
    [InlineData(Roles.WarehouseStaff, SomeoneElse, false)]
    [InlineData(Roles.Clerk, Me, true)]
    [InlineData(Roles.Clerk, SomeoneElse, false)]
    [InlineData(Roles.Owner, Me, false)]
    public void MayEditOrSubmit_FollowsOwnership(string role, int createdBy, bool expected)
    {
        Assert.Equal(expected, DocumentActionPolicy.MayEditOrSubmit(role, Me, Doc(DocumentStatus.Draft, createdBy: createdBy, submittedBy: null)));
    }

    [Theory]
    [InlineData(Roles.WarehouseStaff, DocumentStatus.Draft, Me, SomeoneElse, true)]       // own draft
    [InlineData(Roles.WarehouseStaff, DocumentStatus.Draft, SomeoneElse, Me, false)]      // someone else's draft
    [InlineData(Roles.WarehouseStaff, DocumentStatus.Submitted, SomeoneElse, Me, true)]   // I submitted it (recall)
    [InlineData(Roles.WarehouseStaff, DocumentStatus.Submitted, Me, SomeoneElse, false)]  // I created it, a supervisor submitted it
    [InlineData(Roles.Clerk, DocumentStatus.Submitted, SomeoneElse, SomeoneElse, false)]
    [InlineData(Roles.Supervisor, DocumentStatus.Submitted, SomeoneElse, SomeoneElse, true)]
    [InlineData(Roles.Admin, DocumentStatus.Draft, SomeoneElse, null, true)]
    [InlineData(Roles.Clerk, DocumentStatus.Approved, Me, Me, false)]                   // the creator of an approved document cannot cancel it
    [InlineData(Roles.Supervisor, DocumentStatus.Approved, SomeoneElse, SomeoneElse, true)]
    [InlineData(Roles.Owner, DocumentStatus.Draft, Me, null, false)]                    // the Owner creates nothing
    public void MayCancel_FollowsOwnershipOfADraftAndTheSubmitterOfASubmittedDocument(string role, DocumentStatus status, int createdBy, int? submittedBy, bool expected)
    {
        Assert.Equal(expected, DocumentActionPolicy.MayCancel(role, Me, Doc(status, createdBy: createdBy, submittedBy: submittedBy)));
    }

    [Theory]
    [InlineData(Roles.WarehouseStaff, Me, true)]
    [InlineData(Roles.Clerk, Me, true)]
    [InlineData(Roles.Supervisor, Me, true)]
    [InlineData(Roles.Admin, Me, true)]
    [InlineData(Roles.WarehouseStaff, SomeoneElse, false)]
    [InlineData(Roles.Supervisor, SomeoneElse, false)]   // a supervisor is not the submitter either
    [InlineData(Roles.Admin, SomeoneElse, false)]
    [InlineData(Roles.Owner, Me, false)]                  // no create right
    public void MayRecall_IsTheSubmitterWhoMayCreateDocuments(string role, int submittedBy, bool expected)
    {
        Assert.Equal(expected, DocumentActionPolicy.MayRecall(role, Me, Doc(DocumentStatus.Submitted, requiresApproval: true, submittedBy: submittedBy)));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void MayRecall_IsFalseInEveryStatusButSubmitted(DocumentStatus status)
    {
        foreach (var role in Roles.All)
            Assert.False(DocumentActionPolicy.MayRecall(role, Me, Doc(status, createdBy: Me, submittedBy: Me)));
    }

    [Theory]
    [InlineData(Roles.Admin, false, true)]
    [InlineData(Roles.Supervisor, false, true)]
    [InlineData(Roles.Owner, false, false)]
    [InlineData(Roles.Admin, true, true)]
    [InlineData(Roles.Supervisor, true, false)]
    [InlineData(Roles.Owner, true, true)]
    [InlineData(Roles.WarehouseStaff, true, false)]
    [InlineData(Roles.Clerk, false, false)]
    public void MayReviewApproved_FollowsTheReviewerTheDocumentIsRoutedTo(string role, bool isLarge, bool expected)
    {
        Assert.Equal(expected, DocumentActionPolicy.MayReviewApproved(role, Doc(DocumentStatus.Approved, requiresApproval: true, isLarge: isLarge)));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Submitted)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void MayReviewApproved_IsFalseInEveryStatusButApproved(DocumentStatus status)
    {
        foreach (var role in Roles.All)
            Assert.False(DocumentActionPolicy.MayReviewApproved(role, Doc(status, requiresApproval: true, isLarge: true)));
    }

    // ----- Consistency with the document state machine -----
    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Submitted)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public void NoRoleIsOfferedAnActionTheStateMachineWouldRefuse(DocumentStatus status)
    {
        foreach (var requiresApproval in new[] { true, false })
        foreach (var isLarge in new[] { true, false })
        foreach (var role in Roles.All)
        {
            var actions = Actions(role, status, requiresApproval, isLarge, createdBy: Me, submittedBy: Me);

            if (actions.Contains(DocumentActions.Post))
                Assert.True(status == DocumentStatus.Approved || (status == DocumentStatus.Submitted && !requiresApproval));
            if (actions.Contains(DocumentActions.Approve) || actions.Contains(DocumentActions.Reject))
                Assert.True(status == DocumentStatus.Submitted && requiresApproval);
            if (actions.Contains(DocumentActions.Cancel))
                Assert.True(status is DocumentStatus.Draft or DocumentStatus.Submitted or DocumentStatus.Approved);
            if (actions.Contains(DocumentActions.Recall))
                Assert.Equal(DocumentStatus.Submitted, status);
            if (actions.Contains(DocumentActions.Reverse))
                Assert.Equal(DocumentStatus.Posted, status);
            if (actions.Contains(DocumentActions.Edit) || actions.Contains(DocumentActions.Submit))
                Assert.Equal(DocumentStatus.Draft, status);
        }
    }
}
