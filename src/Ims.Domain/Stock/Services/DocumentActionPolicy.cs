namespace Ims.Domain;

/// <summary>
/// What a user may do with a document right now: the single source of truth for the API (<c>allowedActions</c>, <c>canAct</c>), for the
/// guards in the command handlers and for the UI buttons. Combines the state machine, the permissions (PRD 3.1, UX 7.2),
/// ownership (people who create documents but may not edit other people's act only on their own) and segregation of duties (BR-07).
/// It reads what the person holds (<see cref="ActorRights"/>), never a role name.
/// </summary>
public static class DocumentActionPolicy
{
    /// <summary>Creates documents but may not edit other people's (<see cref="Permissions.DocumentEditAny"/>): they edit, submit and cancel only their own.</summary>
    public static bool IsRestrictedToOwn(ActorRights actor) =>
        actor.Has(Permissions.DocumentCreate) && !actor.Has(Permissions.DocumentEditAny);

    /// <summary>
    /// Edit a Draft or submit it: needs the create right and, for a restricted person, being the creator. Ownership only matters while the document
    /// is a Draft; in any other status the state machine gives the answer (nobody may edit it), so the refusal is the same for everyone.
    /// </summary>
    public static bool MayEditOrSubmit(ActorRights actor, in DocumentActionContext doc) =>
        actor.Has(Permissions.DocumentCreate) &&
        (!IsRestrictedToOwn(actor) || doc.Status != DocumentStatus.Draft || doc.CreatedBy == actor.UserId);

    /// <summary>
    /// Take a Submitted document back to Draft: only the person who submitted it, whatever they hold beyond the create right. An approval is someone
    /// else's decision, so there is no recall of an Approved document. Ownership only matters while it is Submitted; in any other status the state machine answers.
    /// </summary>
    public static bool MayRecall(ActorRights actor, in DocumentActionContext doc) =>
        actor.Has(Permissions.DocumentCreate) && doc.Status == DocumentStatus.Submitted && doc.SubmittedBy == actor.UserId;

    /// <summary>
    /// Undo the approval of an Approved document: the reviewer it is routed to (<see cref="ApprovalPolicy.MayReview(ActorRights, bool)"/>), so a standard
    /// approver cannot touch a large document and the large approver only a large one. The submitter is not excluded: this only reduces what was allowed.
    /// </summary>
    public static bool MayReviewApproved(ActorRights actor, in DocumentActionContext doc) =>
        doc.Status == DocumentStatus.Approved && ApprovalPolicy.MayReview(actor, doc.IsLarge);

    /// <summary>
    /// Cancel a Draft (restricted: the creator), a Submitted document (restricted: the submitter) or an Approved one (the reviewers of
    /// <see cref="MayReviewApproved"/>, who need not be able to create documents). In any other status the state machine refuses it for everyone,
    /// so ownership is not asked.
    /// </summary>
    public static bool MayCancel(ActorRights actor, in DocumentActionContext doc) =>
        doc.Status == DocumentStatus.Approved
            ? MayReviewApproved(actor, doc)
            : actor.Has(Permissions.DocumentCreate) &&
              (!IsRestrictedToOwn(actor) || doc.Status switch
              {
                  DocumentStatus.Draft => doc.CreatedBy == actor.UserId,
                  DocumentStatus.Submitted => doc.SubmittedBy == actor.UserId,
                  _ => true
              });

    /// <summary>
    /// Approve or reject a Submitted document held for approval: the reviewer it is routed to (<see cref="ApprovalPolicy.MayReview(ActorRights, bool)"/>),
    /// and not the submitter unless self-approval is on.
    /// </summary>
    public static bool MayReview(ActorRights actor, in DocumentActionContext doc, bool allowSelfApproval)
    {
        if (doc.Status != DocumentStatus.Submitted || !doc.RequiresApproval) return false;
        if (!ApprovalPolicy.MayReview(actor, doc.IsLarge)) return false;
        return allowSelfApproval || doc.SubmittedBy != actor.UserId;
    }

    /// <param name="withinApprovalLimit">False when the approval rules in force need a higher approver than the caller (checked by the caller, which knows the rules).</param>
    /// <param name="link">Set for a receipt created from a purchase order: <see cref="LinkedReceiptPolicy"/> then takes away what the API would refuse (no <c>po.read</c>, module off).</param>
    public static IReadOnlyList<string> AllowedActions(ActorRights actor, in DocumentActionContext doc, bool allowSelfApproval, bool withinApprovalLimit = true, LinkedReceiptContext? link = null)
    {
        var actions = new List<string>();

        switch (doc.Status)
        {
            case DocumentStatus.Draft:
                if (MayEditOrSubmit(actor, doc)) actions.AddRange([DocumentActions.Edit, DocumentActions.Submit]);
                if (MayCancel(actor, doc)) actions.Add(DocumentActions.Cancel);
                break;

            case DocumentStatus.Submitted:
                if (MayRecall(actor, doc)) actions.Add(DocumentActions.Recall);
                if (MayCancel(actor, doc)) actions.Add(DocumentActions.Cancel);
                if (doc.RequiresApproval)
                {
                    if (withinApprovalLimit && MayReview(actor, doc, allowSelfApproval))
                        actions.AddRange([DocumentActions.Approve, DocumentActions.Reject]);
                }
                else if (actor.Has(Permissions.DocumentPost))
                {
                    actions.Add(DocumentActions.Post);
                }
                break;

            case DocumentStatus.Approved:
                // Warehouse staff may only post documents that never needed approval (PRD 3.1).
                if (actor.Has(Permissions.DocumentPost) && PostingAuthority.MayPostApprovalRequiredDocument(actor)) actions.Add(DocumentActions.Post);
                if (MayCancel(actor, doc)) actions.Add(DocumentActions.Cancel);
                break;

            case DocumentStatus.Posted when actor.Has(Permissions.DocumentReverse):
                actions.Add(DocumentActions.Reverse);
                break;
        }
        return link is { } linked ? LinkedReceiptPolicy.Offered(actions, actor.Has(Permissions.PoRead), linked.PurchasingOn) : actions;
    }
}
