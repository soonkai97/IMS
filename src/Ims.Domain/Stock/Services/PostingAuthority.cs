namespace Ims.Domain;

/// <summary>
/// Who may do the sensitive parts of posting. One place for the rule, used by both the posting use case and
/// <see cref="DocumentActionPolicy"/>, so changing the permission matrix is a single edit.
/// </summary>
public static class PostingAuthority
{
    /// <summary>Warehouse staff may only post documents that never needed approval (PRD 3.1). Holding the permission also needs <see cref="Permissions.DocumentPost"/>.</summary>
    public static bool MayPostApprovalRequiredDocument(ActorRights actor) => actor.Has(Permissions.DocumentPostApproved);

    /// <summary>BR-03: pushing stock below zero needs at least supervisor authority. Holding the permission also needs <see cref="Permissions.DocumentPost"/>.</summary>
    public static bool MayOverrideNegativeStock(ActorRights actor) => actor.Has(Permissions.StockNegativeOverride);
}
