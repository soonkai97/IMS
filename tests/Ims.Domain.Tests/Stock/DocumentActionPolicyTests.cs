namespace Ims.Domain.Tests.Stock;

/// <summary>
/// Status x role x requiresApproval x large x who created / submitted it. The single source of truth for allowedActions
/// (PRD 3.1, UX 7.2, BR-07): the ownership rules of Warehouse staff and Clerk, and the reviewer a document is routed to.
/// </summary>
public class DocumentActionPolicyTests
{
    private const int Me = 42;
    private const int SomeoneElse = 43;

    /// <summary>The default rights of a system role, the acting user being <see cref="Me"/>.</summary>
    private static ActorRights Rights(string role) => SystemRoleDefaults.RightsFor(role, Me);

    private static DocumentActionContext Doc(DocumentStatus status, bool requiresApproval = false, bool isLarge = false,
        int createdBy = SomeoneElse, int? submittedBy = SomeoneElse) =>
        new(status, requiresApproval, isLarge, createdBy, submittedBy);

    private static string[] Actions(string role, DocumentStatus status, bool requiresApproval = false, bool isLarge = false,
        int createdBy = SomeoneElse, int? submittedBy = SomeoneElse, bool allowSelf = false, bool withinLimit = true) =>
        DocumentActionPolicy.AllowedActions(Rights(role), Doc(status, requiresApproval, isLarge, createdBy, submittedBy), allowSelf, withinLimit).Order().ToArray();

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

    // ----- Submitted: Warehouse staff and Clerk may only recall what they submitted -----
    [Theory]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public void SubmittedNeedingApproval_StaffAndClerk_MayCancelOnlyWhatTheySubmitted(string role)
    {
        Assert.Equal(Sorted(DocumentActions.Cancel), Actions(role, DocumentStatus.Submitted, requiresApproval: true, submittedBy: Me));
        Assert.Empty(Actions(role, DocumentStatus.Submitted, requiresApproval: true, createdBy: Me, submittedBy: SomeoneElse));
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
    public void SubmittedNeedingApproval_OwnSubmission_StillMayCancel()
    {
        var actions = Actions(Roles.Supervisor, DocumentStatus.Submitted, requiresApproval: true, submittedBy: Me);

        Assert.Equal(Sorted(DocumentActions.Cancel), actions);
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

        Assert.Contains(DocumentActions.Post, actions);
    }

    // ----- Approved -----
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
        Assert.DoesNotContain(DocumentActions.Cancel, actions); // an approved document cannot be cancelled
        Assert.DoesNotContain(DocumentActions.Edit, actions);
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
        Assert.Equal(expected, DocumentActionPolicy.MayEditOrSubmit(Rights(role), Doc(DocumentStatus.Draft, createdBy: createdBy, submittedBy: null)));
    }

    [Theory]
    [InlineData(Roles.WarehouseStaff, DocumentStatus.Draft, Me, SomeoneElse, true)]       // own draft
    [InlineData(Roles.WarehouseStaff, DocumentStatus.Draft, SomeoneElse, Me, false)]      // someone else's draft
    [InlineData(Roles.WarehouseStaff, DocumentStatus.Submitted, SomeoneElse, Me, true)]   // I submitted it (recall)
    [InlineData(Roles.WarehouseStaff, DocumentStatus.Submitted, Me, SomeoneElse, false)]  // I created it, a supervisor submitted it
    [InlineData(Roles.Clerk, DocumentStatus.Submitted, SomeoneElse, SomeoneElse, false)]
    [InlineData(Roles.Supervisor, DocumentStatus.Submitted, SomeoneElse, SomeoneElse, true)]
    [InlineData(Roles.Admin, DocumentStatus.Draft, SomeoneElse, null, true)]
    public void MayCancel_FollowsOwnershipOfADraftAndTheSubmitterOfASubmittedDocument(string role, DocumentStatus status, int createdBy, int? submittedBy, bool expected)
    {
        Assert.Equal(expected, DocumentActionPolicy.MayCancel(Rights(role), Doc(status, createdBy: createdBy, submittedBy: submittedBy)));
    }

    // ----- What a person holds decides, not the role they are called (ADR roles-permissions 2.7) -----
    private static ActorRights With(params string[] codes) => new(Me, EffectivePermissions.Compute(codes));

    [Fact]
    public void AnApproverWithoutEditAny_IsStillRestrictedToTheirOwnDrafts()
    {
        // Before: giving a Clerk document.approve silently lifted the ownership rule. Now the rule is its own permission.
        var rights = With(Permissions.DocumentCreate, Permissions.DocumentApprove);

        Assert.True(DocumentActionPolicy.IsRestrictedToOwn(rights));
        Assert.False(DocumentActionPolicy.MayEditOrSubmit(rights, Doc(DocumentStatus.Draft, createdBy: SomeoneElse, submittedBy: null)));
        Assert.True(DocumentActionPolicy.MayEditOrSubmit(rights, Doc(DocumentStatus.Draft, createdBy: Me, submittedBy: null)));
    }

    [Fact]
    public void EditAny_LiftsTheOwnershipRule_ForEditingSubmittingAndCancelling()
    {
        var rights = With(Permissions.DocumentCreate, Permissions.DocumentEditAny);

        Assert.False(DocumentActionPolicy.IsRestrictedToOwn(rights));
        Assert.True(DocumentActionPolicy.MayEditOrSubmit(rights, Doc(DocumentStatus.Draft, createdBy: SomeoneElse, submittedBy: null)));
        Assert.True(DocumentActionPolicy.MayCancel(rights, Doc(DocumentStatus.Submitted, createdBy: SomeoneElse, submittedBy: SomeoneElse)));
    }

    [Fact]
    public void EditAnyWithoutCreate_DoesNothing()
    {
        var rights = With(Permissions.DocumentEditAny);

        Assert.False(DocumentActionPolicy.IsRestrictedToOwn(rights));
        Assert.False(DocumentActionPolicy.MayEditOrSubmit(rights, Doc(DocumentStatus.Draft, createdBy: Me, submittedBy: null)));
        Assert.False(DocumentActionPolicy.MayCancel(rights, Doc(DocumentStatus.Draft, createdBy: Me, submittedBy: null)));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]  // post alone: not a document that needed approval
    [InlineData(false, true, false)]  // the extra permission needs document.post itself
    [InlineData(true, true, true)]
    public void PostingAnApprovedDocument_NeedsPostAndPostApproved(bool post, bool postApproved, bool expected)
    {
        var codes = new List<string>();
        if (post) codes.Add(Permissions.DocumentPost);
        if (postApproved) codes.Add(Permissions.DocumentPostApproved);

        var actions = DocumentActionPolicy.AllowedActions(With([.. codes]), Doc(DocumentStatus.Approved, requiresApproval: true), allowSelfApproval: false);

        Assert.Equal(expected, actions.Contains(DocumentActions.Post));
    }

    [Theory]
    [InlineData(Permissions.DocumentApprove, false, true)]
    [InlineData(Permissions.DocumentApprove, true, false)]
    [InlineData(Permissions.DocumentApproveLarge, true, true)]
    [InlineData(Permissions.DocumentApproveLarge, false, false)]
    public void TheTierThePersonHolds_DecidesWhichDocumentsTheyReview(string tier, bool isLarge, bool expected)
    {
        var actions = DocumentActionPolicy.AllowedActions(With(tier), Doc(DocumentStatus.Submitted, requiresApproval: true, isLarge: isLarge), allowSelfApproval: false);

        Assert.Equal(expected, actions.Contains(DocumentActions.Approve) && actions.Contains(DocumentActions.Reject));
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
                Assert.True(status is DocumentStatus.Draft or DocumentStatus.Submitted);
            if (actions.Contains(DocumentActions.Reverse))
                Assert.Equal(DocumentStatus.Posted, status);
            if (actions.Contains(DocumentActions.Edit) || actions.Contains(DocumentActions.Submit))
                Assert.Equal(DocumentStatus.Draft, status);
        }
    }
}
