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
    internal static bool MayReviewUnderRulesInForce(ActorRights actor, bool isLarge, IEnumerable<ApprovalRule> rulesInForce, DocumentType type, decimal totalQty, decimal totalAmount) =>
        MayReview(actor, isLarge) && (!IsLarge(rulesInForce, type, totalQty, totalAmount) || MayReview(actor, isLarge: true));

    /// <summary>
    /// Whether the caller may approve or reject a document held for approval, and if not, why: what the approve and reject commands enforce and <c>allowedActions</c> shows.
    /// First the routing by the stored tier (<see cref="MayReview"/>): a refused holder of only the large tier is told owners review large documents only, anyone else that the
    /// document is above their limit. Then a return has no limit (<see cref="ForReturn"/>): the rules in force do not apply to it. Last the rules in force now (<see cref="MayReviewUnderRulesInForce"/>).
    /// </summary>
    public static ReviewRefusalReason ReviewRefusal(ActorRights actor, bool isLarge, IEnumerable<ApprovalRule> rulesInForce, DocumentType type, decimal totalQty, decimal totalAmount, bool isReturn)
    {
        if (!MayReview(actor, isLarge))
            return actor.Has(Permissions.DocumentApproveLarge) && !actor.Has(Permissions.DocumentApprove) ? ReviewRefusalReason.OwnerReviewsLargeOnly : ReviewRefusalReason.AboveApprovalLimit;
        if (isReturn) return ReviewRefusalReason.None;
        return MayReviewUnderRulesInForce(actor, isLarge, rulesInForce, type, totalQty, totalAmount) ? ReviewRefusalReason.None : ReviewRefusalReason.AboveApprovalLimit;
    }

    /// <summary>
    /// Whether the rules in force can change the answer of <see cref="ReviewRefusal"/>: not when the routing already refuses and not for a return. A caller that has to read the rules
    /// from a store asks this first, so the read happens only when it matters.
    /// </summary>
    public static bool NeedsRulesInForce(ActorRights actor, bool isLarge, bool isReturn) => !isReturn && MayReview(actor, isLarge);

    /// <summary>
    /// A return (plan D12) is always held for approval and always at the standard tier, whatever the rules say and whatever it is worth: the one place that decides it. It is not a seeded rule, because rules are
    /// per document type and would hit every receipt and issue. The Owner, who holds only the large tier, is not a reviewer of a return; a Supervisor or an Administrator is.
    /// </summary>
    public static (bool Required, bool IsLarge) ForReturn => (true, false);

    public static bool RequiresApproval(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount));

    /// <summary>A "large" document fired a rule of the Large tier, one a standard approver may not satisfy.</summary>
    public static bool IsLarge(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount) && r.Tier == ApproverTier.Large);
}
