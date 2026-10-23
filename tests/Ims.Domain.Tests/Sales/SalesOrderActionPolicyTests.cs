namespace Ims.Domain.Tests.Sales;

/// <summary>
/// Status x system role x requires-approval x tier x who made / submitted it. The single source of truth for the sales order's allowedActions (plan D1, D3, BR-07). The two approval tiers
/// are the existing ones: a Supervisor holds the standard tier, the Owner the large one, an Administrator both; so.manage (Admin, Supervisor, Clerk) makes, submits, recalls and cancels.
/// </summary>
public class SalesOrderActionPolicyTests
{
    private const int Me = 42;
    private const int SomeoneElse = 43;

    private static ActorRights Rights(string role) => SystemRoleDefaults.RightsFor(role, Me);

    private static SalesOrderActionContext Order(SalesOrderStatus status, bool requiresApproval = false, bool isLarge = false, int createdBy = SomeoneElse, int? submittedBy = SomeoneElse) =>
        new(status, requiresApproval, isLarge, createdBy, submittedBy);

    private static string[] Actions(string role, SalesOrderActionContext order, bool allowSelf = false, bool withinLimit = true) =>
        SalesOrderActionPolicy.AllowedActions(Rights(role), order, allowSelf, withinLimit).Order().ToArray();

    private static string[] Sorted(params string[] actions) => actions.Order().ToArray();

    // ----- Draft: any so.manage holder edits and submits; only the creator cancels, and whoever holds modules.manage (the Administrator) cancels any Draft -----
    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Clerk)]
    [InlineData(Roles.Owner)]
    public void A_manager_edits_and_submits_any_draft_and_cancels_only_their_own(string role)
    {
        Assert.Equal(Sorted(DocumentActions.Edit, DocumentActions.Submit), Actions(role, Order(SalesOrderStatus.Draft, submittedBy: null)));
        Assert.Equal(Sorted(DocumentActions.Edit, DocumentActions.Submit, DocumentActions.Cancel), Actions(role, Order(SalesOrderStatus.Draft, createdBy: Me, submittedBy: null)));
    }

    [Fact]
    public void Whoever_holds_modules_manage_cancels_any_draft_so_a_departed_creators_draft_cannot_block_the_switch_off()
    {
        Assert.Equal(Sorted(DocumentActions.Edit, DocumentActions.Submit, DocumentActions.Cancel), Actions(Roles.Admin, Order(SalesOrderStatus.Draft, submittedBy: null)));
        Assert.DoesNotContain(DocumentActions.Cancel, Actions(Roles.Supervisor, Order(SalesOrderStatus.Draft, submittedBy: null))); // so.manage alone cancels only its own
        Assert.True(SalesOrderActionPolicy.MayCancel(Rights(Roles.Admin), Order(SalesOrderStatus.Draft, createdBy: SomeoneElse, submittedBy: null)));
    }

    [Fact]
    public void The_draft_cancel_follows_the_permission_alone_modules_manage_without_so_manage_or_ownership_cancels_and_nothing_else_does()
    {
        var onlyModules = new ActorRights(Me, EffectivePermissions.Compute([Permissions.ModulesManage]));
        var onlySoManage = new ActorRights(Me, EffectivePermissions.Compute([Permissions.SoManage]));
        var someonesDraft = Order(SalesOrderStatus.Draft, createdBy: SomeoneElse, submittedBy: null);

        Assert.True(onlyModules.Has(Permissions.ModulesManage) && !onlyModules.Has(Permissions.SoManage)); // the actor is what the test says
        Assert.True(SalesOrderActionPolicy.MayCancel(onlyModules, someonesDraft));
        Assert.False(SalesOrderActionPolicy.MayCancel(onlySoManage, someonesDraft));
        Assert.Empty(SalesOrderActionPolicy.AllowedActions(onlyModules, someonesDraft, allowSelfApproval: false).Except([DocumentActions.Cancel])); // it cancels, it does not edit or submit
        Assert.False(SalesOrderActionPolicy.MayCancel(onlyModules, Order(SalesOrderStatus.Submitted, requiresApproval: true))); // and only a Draft: a Submitted order stays its submitter's
    }

    [Fact]
    public void Someone_without_so_manage_has_no_action_on_a_draft()
    {
        Assert.Empty(Actions(Roles.WarehouseStaff, Order(SalesOrderStatus.Draft, createdBy: Me, submittedBy: null)));
    }

    // ----- Submitted and held for approval -----
    [Fact]
    public void A_standard_order_is_reviewed_by_the_supervisor_and_the_administrator_not_the_owner()
    {
        var order = Order(SalesOrderStatus.Submitted, requiresApproval: true);

        Assert.Equal(Sorted(DocumentActions.Approve, DocumentActions.Reject), Actions(Roles.Supervisor, order));
        Assert.Equal(Sorted(DocumentActions.Approve, DocumentActions.Reject), Actions(Roles.Admin, order));
        Assert.Empty(Actions(Roles.Owner, order));
        Assert.Empty(Actions(Roles.Clerk, order));
        Assert.Empty(Actions(Roles.WarehouseStaff, order));
    }

    [Fact]
    public void A_large_order_is_reviewed_by_the_owner_and_the_administrator_not_the_supervisor()
    {
        var order = Order(SalesOrderStatus.Submitted, requiresApproval: true, isLarge: true);

        Assert.Equal(Sorted(DocumentActions.Approve, DocumentActions.Reject), Actions(Roles.Owner, order));
        Assert.Equal(Sorted(DocumentActions.Approve, DocumentActions.Reject), Actions(Roles.Admin, order));
        Assert.Empty(Actions(Roles.Supervisor, order));
    }

    [Theory]
    [InlineData(Roles.Admin, false)]
    [InlineData(Roles.Supervisor, false)]
    [InlineData(Roles.Admin, true)]
    public void Nobody_reviews_their_own_submission_unless_self_approval_is_on(string role, bool allowSelf)
    {
        var mine = Order(SalesOrderStatus.Submitted, requiresApproval: true, submittedBy: Me);

        var actions = Actions(role, mine, allowSelf);

        Assert.Equal(allowSelf, actions.Contains(DocumentActions.Approve));
        Assert.Equal(allowSelf, actions.Contains(DocumentActions.Reject));
        Assert.Contains(DocumentActions.Recall, actions); // taking it back is theirs to do
    }

    [Fact]
    public void An_approval_the_rules_in_force_now_put_above_the_caller_is_not_offered()
    {
        var order = Order(SalesOrderStatus.Submitted, requiresApproval: true);

        Assert.Empty(Actions(Roles.Supervisor, order, withinLimit: false));
        Assert.False(SalesOrderActionPolicy.MayReview(Rights(Roles.Supervisor), Order(SalesOrderStatus.Submitted, requiresApproval: false), false)); // not held for approval
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Clerk)]
    public void Only_the_submitter_recalls_and_cancels_a_submitted_order(string role)
    {
        var mine = Actions(role, Order(SalesOrderStatus.Submitted, requiresApproval: true, submittedBy: Me));
        var theirs = Actions(role, Order(SalesOrderStatus.Submitted, requiresApproval: true, submittedBy: SomeoneElse));

        Assert.Contains(DocumentActions.Recall, mine);
        Assert.Contains(DocumentActions.Cancel, mine);
        Assert.DoesNotContain(DocumentActions.Recall, theirs);
        Assert.DoesNotContain(DocumentActions.Cancel, theirs);
    }

    // ----- Approved: only the reviewers it is routed to cancel it; nobody recalls or edits -----
    [Theory]
    [InlineData(Roles.Admin, false, true)]
    [InlineData(Roles.Admin, true, true)]
    [InlineData(Roles.Supervisor, false, true)]
    [InlineData(Roles.Supervisor, true, false)]
    [InlineData(Roles.Owner, false, false)]
    [InlineData(Roles.Owner, true, true)]
    [InlineData(Roles.Clerk, false, false)]
    [InlineData(Roles.WarehouseStaff, true, false)]
    public void An_approved_order_is_cancelled_only_by_a_reviewer_of_its_tier(string role, bool isLarge, bool mayCancel)
    {
        var actions = Actions(role, Order(SalesOrderStatus.Approved, requiresApproval: true, isLarge: isLarge));

        Assert.Equal(mayCancel ? Sorted(DocumentActions.Cancel) : [], actions); // closing short and delivering are offered by later slices (PL3.5, PL3.7)
    }

    [Fact]
    public void An_order_released_at_once_is_standard_so_a_supervisor_cancels_it_and_its_clerk_maker_cannot()
    {
        var released = Order(SalesOrderStatus.Approved, requiresApproval: false, createdBy: Me, submittedBy: Me);

        Assert.Equal(Sorted(DocumentActions.Cancel), Actions(Roles.Supervisor, released));
        Assert.Empty(Actions(Roles.Clerk, released));
    }

    [Theory]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public void A_closed_or_cancelled_order_offers_nothing(SalesOrderStatus status)
    {
        foreach (var role in new[] { Roles.Admin, Roles.Owner, Roles.Supervisor, Roles.Clerk, Roles.WarehouseStaff })
            Assert.Empty(Actions(role, Order(status, createdBy: Me, submittedBy: Me)));
    }
}
