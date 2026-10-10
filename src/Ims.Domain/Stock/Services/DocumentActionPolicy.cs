namespace Ims.Domain;

/// <summary>
/// What a user may do with a document right now: the single source of truth for both the API (<c>allowedActions</c>)
/// and the UI buttons. Combines the state machine, the permission matrix and segregation of duties.
/// </summary>
public static class DocumentActionPolicy
{
    public static IReadOnlyList<string> AllowedActions(string role, int userId, DocumentStatus status, bool requiresApproval,
        int? submittedById, bool allowSelfApproval)
    {
        bool Can(string permission) => Permissions.Has(role, permission);
        var actions = new List<string>();

        switch (status)
        {
            case DocumentStatus.Draft when Can(Permissions.DocumentCreate):
                actions.AddRange([DocumentActions.Edit, DocumentActions.Submit, DocumentActions.Cancel]);
                break;

            case DocumentStatus.Submitted:
                if (Can(Permissions.DocumentCreate)) actions.Add(DocumentActions.Cancel);
                if (requiresApproval)
                {
                    var selfBlocked = !allowSelfApproval && submittedById == userId;
                    if (Can(Permissions.DocumentApprove) && !selfBlocked)
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
