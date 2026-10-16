namespace Ims.Domain;

/// <summary>BR-06 / BR-07: who must approve a document and whether it needs approval at all.</summary>
public static class ApprovalPolicy
{
    // ----- By tier: what the Domain policies ask (ADR roles-permissions 2.7) -----

    /// <summary>
    /// Which reviewer a document is routed to (PRD 3.1): a large document goes to the holders of <see cref="Permissions.DocumentApproveLarge"/>, any other to
    /// the holders of <see cref="Permissions.DocumentApprove"/>; whoever holds both (the Admin) reviews anything. Used for approving and for rejecting, and
    /// by <c>allowedActions</c>, so they cannot disagree.
    /// </summary>
    public static bool MayReview(ActorRights actor, bool isLarge) =>
        actor.Has(isLarge ? Permissions.DocumentApproveLarge : Permissions.DocumentApprove);

    public static bool RequiresApproval(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount));

    /// <summary>A "large" document fired a rule of the Large tier, one a standard approver may not satisfy.</summary>
    public static bool IsLarge(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount) && r.Tier == ApproverTier.Large);

    // ----- By role name: still used by the approval services until approvals go end to end (RP-4b), which deletes them -----

    /// <summary>Approval authority. A higher rank may approve anything a lower rank may.</summary>
    public static int RankOf(string role) => role switch
    {
        Roles.Admin => 4,
        Roles.Owner => 3,
        Roles.Supervisor => 2,
        _ => 0
    };

    /// <summary>The role-keyed form of <see cref="MayReview(ActorRights, bool)"/>: the Admin reviews anything, the Owner only large documents, a Supervisor only the rest.</summary>
    public static bool MayReview(string role, bool isLarge) => role switch
    {
        Roles.Admin => true,
        Roles.Owner => isLarge,
        Roles.Supervisor => !isLarge,
        _ => false
    };

    /// <summary>The approver must outrank every rule that fired; with no rule fired, a supervisor suffices.</summary>
    public static bool CanApprove(string approverRole, IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount)
    {
        var required = rules.Where(r => r.Matches(type, totalQty, totalAmount))
            .Select(r => RankOf(r.RoleRequired)).DefaultIfEmpty(RankOf(Roles.Supervisor)).Max();
        return RankOf(approverRole) >= required;
    }
}
