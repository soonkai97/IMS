namespace Ims.Domain;

/// <summary>BR-06 / BR-07: who must approve a document and whether it needs approval at all. By tier, not by role: the approver is whoever holds the tier's permission.</summary>
public static class ApprovalPolicy
{
    /// <summary>
    /// Which reviewer a document is routed to (PRD 3.1): a large document goes to the holders of <see cref="Permissions.DocumentApproveLarge"/>, any other to
    /// the holders of <see cref="Permissions.DocumentApprove"/>; whoever holds both (the Admin) reviews anything. Used for approving and for rejecting, and
    /// by <c>allowedActions</c>, so they cannot disagree. The approval limit is the same question asked of the rules in force at approval time (<see cref="IsLarge"/>).
    /// </summary>
    public static bool MayReview(ActorRights actor, bool isLarge) =>
        actor.Has(isLarge ? Permissions.DocumentApproveLarge : Permissions.DocumentApprove);

    public static bool RequiresApproval(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount));

    /// <summary>A "large" document fired a rule of the Large tier, one a standard approver may not satisfy.</summary>
    public static bool IsLarge(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount) && r.Tier == ApproverTier.Large);
}
