namespace Ims.Domain;

/// <summary>
/// Who may do the sensitive parts of posting. One place for the rule, used by both the posting use case and
/// <see cref="DocumentActionPolicy"/>, so changing the permission matrix is a single edit.
/// </summary>
public static class PostingAuthority
{
    /// <summary>Warehouse staff may only post documents that never needed approval (PRD 3.1).</summary>
    public static bool MayPostApprovalRequiredDocument(string role) => role != Roles.WarehouseStaff;

    /// <summary>BR-03: pushing stock below zero needs at least supervisor authority.</summary>
    public static bool MayOverrideNegativeStock(string role) => ApprovalPolicy.RankOf(role) >= ApprovalPolicy.RankOf(Roles.Supervisor);
}
