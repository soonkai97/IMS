namespace Ims.Domain;

/// <summary>
/// What a user may do with a document right now: the single source of truth for the API (<c>allowedActions</c>, <c>canAct</c>), for the
/// guards in the command handlers and for the UI buttons. Combines the state machine, the permission matrix (PRD 3.1, UX 7.2),
/// ownership (Warehouse staff and Clerk act only on their own documents) and segregation of duties (BR-07).
/// </summary>
public static class DocumentActionPolicy
{
    /// <summary>Roles that create documents but cannot approve (Warehouse staff, Clerk): they edit, submit and cancel only their own.</summary>
    public static bool IsRestrictedToOwn(string role) =>
        Permissions.Has(role, Permissions.DocumentCreate) && !Permissions.Has(role, Permissions.DocumentApprove);

    /// <summary>
    /// Edit a Draft or submit it: needs the create right and, for restricted roles, being the creator. Ownership only matters while the document
    /// is a Draft; in any other status the state machine gives the answer (nobody may edit it), so the refusal is the same for everyone.
    /// </summary>
    public static bool MayEditOrSubmit(string role, int userId, in DocumentActionContext doc) =>
        Permissions.Has(role, Permissions.DocumentCreate) &&
        (!IsRestrictedToOwn(role) || doc.Status != DocumentStatus.Draft || doc.CreatedBy == userId);

    /// <summary>
    /// Cancel a Draft (restricted roles: the creator) or a Submitted document (restricted roles: the submitter, a recall). In any other
    /// status the state machine refuses it for everyone, so ownership is not asked.
    /// </summary>
    public static bool MayCancel(string role, int userId, in DocumentActionContext doc) =>
        Permissions.Has(role, Permissions.DocumentCreate) &&
        (!IsRestrictedToOwn(role) || doc.Status switch
        {
            DocumentStatus.Draft => doc.CreatedBy == userId,
            DocumentStatus.Submitted => doc.SubmittedBy == userId,
            _ => true
        });

    /// <summary>
    /// Approve or reject a Submitted document held for approval: the reviewer role it is routed to (<see cref="ApprovalPolicy.MayReview"/>),
    /// and not the submitter unless self-approval is on.
    /// </summary>
    public static bool MayReview(string role, int userId, in DocumentActionContext doc, bool allowSelfApproval)
    {
        if (doc.Status != DocumentStatus.Submitted || !doc.RequiresApproval) return false;
        if (!Permissions.Has(role, Permissions.DocumentApprove) || !ApprovalPolicy.MayReview(role, doc.IsLarge)) return false;
        return allowSelfApproval || doc.SubmittedBy != userId;
    }

    /// <param name="withinApprovalLimit">False when the approval rules in force need a higher role than the caller's (checked by the caller, which knows the rules).</param>
    public static IReadOnlyList<string> AllowedActions(string role, int userId, in DocumentActionContext doc, bool allowSelfApproval, bool withinApprovalLimit = true)
    {
        bool Can(string permission) => Permissions.Has(role, permission);
        var actions = new List<string>();

        switch (doc.Status)
        {
            case DocumentStatus.Draft:
                if (MayEditOrSubmit(role, userId, doc)) actions.AddRange([DocumentActions.Edit, DocumentActions.Submit]);
                if (MayCancel(role, userId, doc)) actions.Add(DocumentActions.Cancel);
                break;

            case DocumentStatus.Submitted:
                if (MayCancel(role, userId, doc)) actions.Add(DocumentActions.Cancel);
                if (doc.RequiresApproval)
                {
                    if (withinApprovalLimit && MayReview(role, userId, doc, allowSelfApproval))
                        actions.AddRange([DocumentActions.Approve, DocumentActions.Reject]);
                }
                else if (Can(Permissions.DocumentPost))
                {
                    actions.Add(DocumentActions.Post);
                }
                break;

            // Warehouse staff may only post documents that never needed approval (PRD 3.1).
            case DocumentStatus.Approved when Can(Permissions.DocumentPost) && PostingAuthority.MayPostApprovalRequiredDocument(role):
                actions.Add(DocumentActions.Post);
                break;

            case DocumentStatus.Posted when Can(Permissions.DocumentReverse):
                actions.Add(DocumentActions.Reverse);
                break;
        }
        return actions;
    }
}
