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

    /// <summary>
    /// The approval-limit check by what the approver holds, not by a role name: the approver must outrank every rule that fired (a rule names the role it needs; with no rule
    /// fired a standard approver suffices). The rank of the approver is read from the tiers held: both tiers 4 (the Admin), the large tier alone 3 (the Owner), the
    /// standard tier alone 2 (a Supervisor), neither 0. Asked together with <see cref="MayReview(ActorRights, bool)"/>, and for the five system roles it answers as the
    /// role-keyed <see cref="CanApprove(string, IEnumerable{ApprovalRule}, DocumentType, decimal, decimal)"/> for every stored tier and every rule set (the parity test pins that),
    /// so the rules in force at approval time still refuse a standard approver on a document stored as standard, as before. RP-4b replaces the rule's role name with a tier.
    /// </summary>
    public static bool CanApprove(ActorRights actor, IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount)
    {
        var required = rules.Where(r => r.Matches(type, totalQty, totalAmount))
            .Select(r => RankOf(r.RoleRequired)).DefaultIfEmpty(RankOf(Roles.Supervisor)).Max();
        return RankOf(actor) >= required;
    }

    private static int RankOf(ActorRights actor) =>
        actor.Has(Permissions.DocumentApprove) ? (actor.Has(Permissions.DocumentApproveLarge) ? 4 : 2) : actor.Has(Permissions.DocumentApproveLarge) ? 3 : 0;

    // ----- By role name: what the oracle fixtures were written from; nothing in the running code calls them (RP-4b deletes them with RoleRequired) -----

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
