namespace Ims.Domain.Tests.Purchasing;

/// <summary>
/// Status x system role x requires-approval x tier x who made / submitted it. The single source of truth for the purchase order's allowedActions (plan D1, D3, BR-07). The two approval tiers
/// are the existing ones: a Supervisor holds the standard tier, the Owner the large one, an Administrator both; po.manage (Admin, Supervisor, Clerk) makes, submits, recalls and cancels.
/// </summary>
public class PurchaseOrderActionPolicyTests
{
    private const int Me = 42;
    private const int SomeoneElse = 43;

    private static ActorRights Rights(string role) => SystemRoleDefaults.RightsFor(role, Me);

    private static PurchaseOrderActionContext Order(PurchaseOrderStatus status, bool requiresApproval = false, bool isLarge = false, int createdBy = SomeoneElse, int? submittedBy = SomeoneElse, bool anythingReceived = false, bool anythingRemaining = false) =>
        new(status, requiresApproval, isLarge, createdBy, submittedBy, anythingReceived, anythingRemaining);

    private static string[] Actions(string role, PurchaseOrderActionContext order, bool allowSelf = false, bool withinLimit = true) =>
        PurchaseOrderActionPolicy.AllowedActions(Rights(role), order, allowSelf, withinLimit).Order().ToArray();

    private static string[] Sorted(params string[] actions) => actions.Order().ToArray();

    // ----- Draft: any po.manage holder edits and submits; only the creator cancels, and whoever holds modules.manage (the Administrator) cancels any Draft -----
    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Clerk)]
    public void A_manager_edits_and_submits_any_draft_and_cancels_only_their_own(string role)
    {
        Assert.Equal(Sorted(DocumentActions.Edit, DocumentActions.Submit), Actions(role, Order(PurchaseOrderStatus.Draft, submittedBy: null)));
        Assert.Equal(Sorted(DocumentActions.Edit, DocumentActions.Submit, DocumentActions.Cancel), Actions(role, Order(PurchaseOrderStatus.Draft, createdBy: Me, submittedBy: null)));
    }

    [Fact]
    public void Whoever_holds_modules_manage_cancels_any_draft_so_a_departed_creators_draft_cannot_block_the_switch_off()
    {
        Assert.Equal(Sorted(DocumentActions.Edit, DocumentActions.Submit, DocumentActions.Cancel), Actions(Roles.Admin, Order(PurchaseOrderStatus.Draft, submittedBy: null)));
        Assert.DoesNotContain(DocumentActions.Cancel, Actions(Roles.Supervisor, Order(PurchaseOrderStatus.Draft, submittedBy: null))); // po.manage alone cancels only its own
        Assert.True(PurchaseOrderActionPolicy.MayCancel(Rights(Roles.Admin), Order(PurchaseOrderStatus.Draft, createdBy: SomeoneElse, submittedBy: null)));
    }

    [Fact]
    public void The_draft_cancel_follows_the_permission_alone_modules_manage_without_po_manage_or_ownership_cancels_and_nothing_else_does()
    {
        var onlyModules = new ActorRights(Me, EffectivePermissions.Compute([Permissions.ModulesManage]));
        var onlyPoManage = new ActorRights(Me, EffectivePermissions.Compute([Permissions.PoManage]));
        var someonesDraft = Order(PurchaseOrderStatus.Draft, createdBy: SomeoneElse, submittedBy: null);

        Assert.True(onlyModules.Has(Permissions.ModulesManage) && !onlyModules.Has(Permissions.PoManage)); // the actor is what the test says
        Assert.True(PurchaseOrderActionPolicy.MayCancel(onlyModules, someonesDraft));
        Assert.False(PurchaseOrderActionPolicy.MayCancel(onlyPoManage, someonesDraft));
        Assert.Empty(PurchaseOrderActionPolicy.AllowedActions(onlyModules, someonesDraft, allowSelfApproval: false).Except([DocumentActions.Cancel])); // it cancels, it does not edit or submit
        Assert.False(PurchaseOrderActionPolicy.MayCancel(onlyModules, Order(PurchaseOrderStatus.Submitted, requiresApproval: true))); // and only a Draft: a Submitted order stays its submitter's
    }

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.WarehouseStaff)]
    public void Someone_without_po_manage_has_no_action_on_a_draft(string role)
    {
        Assert.Empty(Actions(role, Order(PurchaseOrderStatus.Draft, createdBy: Me, submittedBy: null)));
    }

    // ----- Submitted and held for approval -----
    [Fact]
    public void A_standard_order_is_reviewed_by_the_supervisor_and_the_administrator_not_the_owner()
    {
        var order = Order(PurchaseOrderStatus.Submitted, requiresApproval: true);

        Assert.Equal(Sorted(DocumentActions.Approve, DocumentActions.Reject), Actions(Roles.Supervisor, order));
        Assert.Equal(Sorted(DocumentActions.Approve, DocumentActions.Reject), Actions(Roles.Admin, order));
        Assert.Empty(Actions(Roles.Owner, order));
        Assert.Empty(Actions(Roles.Clerk, order));
        Assert.Empty(Actions(Roles.WarehouseStaff, order));
    }

    [Fact]
    public void A_large_order_is_reviewed_by_the_owner_and_the_administrator_not_the_supervisor()
    {
        var order = Order(PurchaseOrderStatus.Submitted, requiresApproval: true, isLarge: true);

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
        var mine = Order(PurchaseOrderStatus.Submitted, requiresApproval: true, submittedBy: Me);

        var actions = Actions(role, mine, allowSelf);

        Assert.Equal(allowSelf, actions.Contains(DocumentActions.Approve));
        Assert.Equal(allowSelf, actions.Contains(DocumentActions.Reject));
        Assert.Contains(DocumentActions.Recall, actions); // taking it back is theirs to do
    }

    [Fact]
    public void An_approval_the_rules_in_force_now_put_above_the_caller_is_not_offered()
    {
        var order = Order(PurchaseOrderStatus.Submitted, requiresApproval: true);

        Assert.Empty(Actions(Roles.Supervisor, order, withinLimit: false));
        Assert.False(PurchaseOrderActionPolicy.MayReview(Rights(Roles.Supervisor), Order(PurchaseOrderStatus.Submitted, requiresApproval: false), false)); // not held for approval
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Clerk)]
    public void Only_the_submitter_recalls_and_cancels_a_submitted_order(string role)
    {
        var mine = Actions(role, Order(PurchaseOrderStatus.Submitted, requiresApproval: true, submittedBy: Me));
        var theirs = Actions(role, Order(PurchaseOrderStatus.Submitted, requiresApproval: true, submittedBy: SomeoneElse));

        Assert.Contains(DocumentActions.Recall, mine);
        Assert.Contains(DocumentActions.Cancel, mine);
        Assert.DoesNotContain(DocumentActions.Recall, theirs);
        Assert.DoesNotContain(DocumentActions.Cancel, theirs);
    }

    // ----- Approved: only the reviewers it is routed to cancel it; nobody recalls or edits; close short is offered only once something was received (PL2.5) -----
    [Theory]
    [InlineData(Roles.Admin, false, true)]
    [InlineData(Roles.Admin, true, true)]
    [InlineData(Roles.Supervisor, false, true)]
    [InlineData(Roles.Supervisor, true, false)]
    [InlineData(Roles.Owner, false, false)]
    [InlineData(Roles.Owner, true, true)]
    [InlineData(Roles.Clerk, false, false)]
    [InlineData(Roles.WarehouseStaff, true, false)]
    public void An_approved_order_with_nothing_received_is_cancelled_only_by_a_reviewer_of_its_tier_and_is_never_closed_short(string role, bool isLarge, bool mayCancel)
    {
        var actions = Actions(role, Order(PurchaseOrderStatus.Approved, requiresApproval: true, isLarge: isLarge));

        Assert.Equal(mayCancel ? Sorted(DocumentActions.Cancel) : [], actions); // cancel authority stays the only way to void an order
    }

    [Theory]
    [InlineData(Roles.Admin, false, true)]
    [InlineData(Roles.Supervisor, false, true)]
    [InlineData(Roles.Supervisor, true, false)]
    [InlineData(Roles.Owner, false, false)]
    [InlineData(Roles.Owner, true, true)]
    [InlineData(Roles.Clerk, false, false)]
    [InlineData(Roles.WarehouseStaff, true, false)]
    public void Once_something_is_received_any_po_manage_holder_may_close_the_order_short_and_cancel_is_unchanged(string role, bool isLarge, bool mayCancel)
    {
        var actions = Actions(role, Order(PurchaseOrderStatus.Approved, requiresApproval: true, isLarge: isLarge, anythingReceived: true));

        var mayClose = role is Roles.Admin or Roles.Supervisor or Roles.Clerk; // po.manage
        var expected = new List<string>();
        if (mayCancel) expected.Add(DocumentActions.Cancel);
        if (mayClose) expected.Add(DocumentActions.Close);
        Assert.Equal(Sorted([.. expected]), actions);
    }

    [Fact]
    public void An_order_released_at_once_is_standard_so_a_supervisor_cancels_it_and_its_clerk_maker_cannot()
    {
        var released = Order(PurchaseOrderStatus.Approved, requiresApproval: false, createdBy: Me, submittedBy: Me);

        Assert.Equal(Sorted(DocumentActions.Cancel), Actions(Roles.Supervisor, released));
        Assert.Empty(Actions(Roles.Clerk, released));
        Assert.Equal(Sorted(DocumentActions.Close), Actions(Roles.Clerk, released with { AnythingReceived = true })); // the clerk who made it closes it short once something came in
    }

    // ----- receive: an Approved order with something left, to whoever holds document.create and po.read (PL2.9) -----
    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Clerk, true)]
    [InlineData(Roles.WarehouseStaff, true)]
    [InlineData(Roles.Owner, false)] // po.read but no document.create
    public void An_approved_order_with_something_left_is_received_by_whoever_holds_document_create_and_po_read(string role, bool mayReceive)
    {
        var order = Order(PurchaseOrderStatus.Approved, requiresApproval: true, anythingRemaining: true);

        Assert.Equal(mayReceive, Actions(role, order).Contains(DocumentActions.Receive));
        Assert.Equal(mayReceive, PurchaseOrderActionPolicy.MayReceive(Rights(role), order));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Submitted)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    [InlineData(PurchaseOrderStatus.Closed)]
    public void Receive_is_offered_only_on_an_approved_order(PurchaseOrderStatus status)
    {
        foreach (var role in new[] { Roles.Admin, Roles.Supervisor, Roles.Clerk, Roles.WarehouseStaff })
            Assert.DoesNotContain(DocumentActions.Receive, Actions(role, Order(status, requiresApproval: true, anythingRemaining: true)));
    }

    [Fact]
    public void Receive_needs_something_left_and_both_permissions_not_a_role()
    {
        var approved = Order(PurchaseOrderStatus.Approved, anythingRemaining: true);
        var onlyCreate = new ActorRights(Me, EffectivePermissions.Compute([Permissions.DocumentCreate]));
        var onlyRead = new ActorRights(Me, EffectivePermissions.Compute([Permissions.PoRead]));
        var both = new ActorRights(Me, EffectivePermissions.Compute([Permissions.DocumentCreate, Permissions.PoRead]));

        Assert.False(PurchaseOrderActionPolicy.MayReceive(onlyCreate, approved));
        Assert.False(PurchaseOrderActionPolicy.MayReceive(onlyRead, approved));
        Assert.True(PurchaseOrderActionPolicy.MayReceive(both, approved));
        Assert.False(PurchaseOrderActionPolicy.MayReceive(both, approved with { AnythingRemaining = false })); // fully received
        Assert.Equal([DocumentActions.Receive], PurchaseOrderActionPolicy.AllowedActions(both, approved, allowSelfApproval: false)); // and nothing else: no cancel, no close
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Closed)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public void A_closed_or_cancelled_order_offers_nothing(PurchaseOrderStatus status)
    {
        foreach (var role in new[] { Roles.Admin, Roles.Owner, Roles.Supervisor, Roles.Clerk, Roles.WarehouseStaff })
            Assert.Empty(Actions(role, Order(status, createdBy: Me, submittedBy: Me)));
    }
}
