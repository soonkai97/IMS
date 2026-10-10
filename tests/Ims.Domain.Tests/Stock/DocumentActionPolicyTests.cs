namespace Ims.Domain.Tests.Stock;

/// <summary>Status x role x requiresApproval x self-submitted. The single source of truth for allowedActions (PRD 3.1, BR-07).</summary>
public class DocumentActionPolicyTests
{
    private const int Me = 42;
    private const int SomeoneElse = 43;

    private static string[] Actions(string role, DocumentStatus status, bool requiresApproval = false, int? submittedBy = SomeoneElse, bool allowSelf = false) =>
        DocumentActionPolicy.AllowedActions(role, Me, status, requiresApproval, submittedBy, allowSelf).Order().ToArray();

    private static string[] Sorted(params string[] actions) => actions.Order().ToArray();

    // ----- Draft: edit / submit / cancel for anyone who can create documents -----
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public void Draft_RolesThatCanCreate_MayEditSubmitAndCancel(string role)
    {
        Assert.Equal(Sorted(DocumentActions.Edit, DocumentActions.Submit, DocumentActions.Cancel), Actions(role, DocumentStatus.Draft));
    }

    [Fact]
    public void Draft_Owner_CannotDoAnything()
    {
        Assert.Empty(Actions(Roles.Owner, DocumentStatus.Draft));
    }

    // ----- Submitted, approval needed -----
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    public void SubmittedNeedingApproval_ApproverRoles_MayApproveRejectAndCancel(string role)
    {
        Assert.Equal(
            Sorted(DocumentActions.Approve, DocumentActions.Reject, DocumentActions.Cancel),
            Actions(role, DocumentStatus.Submitted, requiresApproval: true));
    }

    [Fact]
    public void SubmittedNeedingApproval_Owner_MayApproveAndRejectButNotCancel()
    {
        Assert.Equal(
            Sorted(DocumentActions.Approve, DocumentActions.Reject),
            Actions(Roles.Owner, DocumentStatus.Submitted, requiresApproval: true));
    }

    [Theory]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public void SubmittedNeedingApproval_CreatorOnlyRoles_MayOnlyCancel(string role)
    {
        Assert.Equal(Sorted(DocumentActions.Cancel), Actions(role, DocumentStatus.Submitted, requiresApproval: true));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Owner)]
    public void SubmittedNeedingApproval_OwnSubmission_CannotApproveOrReject(string role)
    {
        var actions = Actions(role, DocumentStatus.Submitted, requiresApproval: true, submittedBy: Me, allowSelf: false);

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
    public void SubmittedWithoutApproval_PostingRoles_MayPost(string role)
    {
        Assert.Contains(DocumentActions.Post, Actions(role, DocumentStatus.Submitted, requiresApproval: false));
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
        foreach (var role in Roles.All)
        {
            var actions = Actions(role, status, requiresApproval);

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
