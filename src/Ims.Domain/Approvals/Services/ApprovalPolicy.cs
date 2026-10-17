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

    /// <summary>
    /// The routing as it is asked when someone approves or rejects: by the tier the document was stored with, and by the rules in force now, which can be stricter than when it
    /// was stored (a rule changed to Large since): a Large rule in force needs the large tier, so a standard approver is refused on such a document. This is the approval limit
    /// (BR-06); there is no rank of role names to compare.
    /// </summary>
    public static bool MayReview(ActorRights actor, bool isLarge, IEnumerable<ApprovalRule> rulesInForce, DocumentType type, decimal totalQty, decimal totalAmount) =>
        MayReview(actor, isLarge) && (!IsLarge(rulesInForce, type, totalQty, totalAmount) || MayReview(actor, isLarge: true));

    public static bool RequiresApproval(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount));

    /// <summary>A "large" document fired a rule of the Large tier, one a standard approver may not satisfy.</summary>
    public static bool IsLarge(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount) && r.Tier == ApproverTier.Large);
}
