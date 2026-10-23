namespace Ims.Domain;

/// <summary>
/// What a user may do with a sales order right now: the single source of truth for the API (<c>allowedActions</c>, <c>canAct</c>), for the guards in the command handlers and for the UI
/// buttons. It is <see cref="DocumentActionPolicy"/> for the sales order: the state machine, <c>so.manage</c> (create, edit, submit, recall, cancel), the two approval tiers
/// (<see cref="ApprovalPolicy"/>, no permission of its own for sales orders, plan D3) and segregation of duties (BR-07). It reads what the person holds, never a role name.
/// </summary>
public static class SalesOrderActionPolicy
{
    /// <summary>Edit a Draft or submit it: any holder of <c>so.manage</c> (no ownership split, plan D4). Whoever submits is the submitter and so cannot approve it.</summary>
    public static bool MayEditOrSubmit(ActorRights actor) => actor.Has(Permissions.SoManage);

    /// <summary>Take a Submitted order back to Draft: only the person who submitted it. An approval is someone else's decision, so an Approved order is never recalled.</summary>
    public static bool MayRecall(ActorRights actor, in SalesOrderActionContext order) =>
        actor.Has(Permissions.SoManage) && order.Status == SalesOrderStatus.Submitted && order.SubmittedBy == actor.UserId;

    /// <summary>Undo the approval of an Approved order: the reviewer it is routed to (<see cref="ApprovalPolicy.MayReview(ActorRights, bool)"/>). The submitter is not excluded: this only reduces what was allowed.</summary>
    public static bool MayReviewApproved(ActorRights actor, in SalesOrderActionContext order) =>
        order.Status == SalesOrderStatus.Approved && ApprovalPolicy.MayReview(actor, order.IsLarge);

    /// <summary>
    /// Cancel a Draft (its creator, or whoever holds <c>modules.manage</c>), a Submitted order (its submitter) or an Approved one (a reviewer it is routed to, who need not hold <c>so.manage</c>). In any other status the state
    /// machine refuses it for everyone, so ownership is not asked and the caller is told the status.
    /// </summary>
    public static bool MayCancel(ActorRights actor, in SalesOrderActionContext order) => order.Status switch
    {
        SalesOrderStatus.Approved => MayReviewApproved(actor, order),
        // A holder of modules.manage (the capability that must clear open orders before purchasing can be switched off) may cancel any Draft, so one left behind by a departed creator cannot block it.
        SalesOrderStatus.Draft => (actor.Has(Permissions.SoManage) && order.CreatedBy == actor.UserId) || actor.Has(Permissions.ModulesManage),
        SalesOrderStatus.Submitted => actor.Has(Permissions.SoManage) && order.SubmittedBy == actor.UserId,
        _ => true
    };

    /// <summary>Approve or reject a Submitted order held for approval: the reviewer it is routed to, and not the submitter unless self-approval is on.</summary>
    public static bool MayReview(ActorRights actor, in SalesOrderActionContext order, bool allowSelfApproval)
    {
        if (order.Status != SalesOrderStatus.Submitted || !order.RequiresApproval) return false;
        if (!ApprovalPolicy.MayReview(actor, order.IsLarge)) return false;
        return allowSelfApproval || order.SubmittedBy != actor.UserId;
    }

    /// <param name="withinApprovalLimit">False when the approval rules in force need a higher approver than the caller (checked by the caller, which knows the rules).</param>
    public static IReadOnlyList<string> AllowedActions(ActorRights actor, in SalesOrderActionContext order, bool allowSelfApproval, bool withinApprovalLimit = true)
    {
        var actions = new List<string>();
        switch (order.Status)
        {
            case SalesOrderStatus.Draft:
                if (MayEditOrSubmit(actor)) actions.AddRange([DocumentActions.Edit, DocumentActions.Submit]);
                if (MayCancel(actor, order)) actions.Add(DocumentActions.Cancel);
                break;

            case SalesOrderStatus.Submitted:
                if (MayRecall(actor, order)) actions.Add(DocumentActions.Recall);
                if (MayCancel(actor, order)) actions.Add(DocumentActions.Cancel);
                if (withinApprovalLimit && MayReview(actor, order, allowSelfApproval)) actions.AddRange([DocumentActions.Approve, DocumentActions.Reject]);
                break;

            case SalesOrderStatus.Approved:
                if (MayCancel(actor, order)) actions.Add(DocumentActions.Cancel);
                break;
        }
        return actions;
    }
}
