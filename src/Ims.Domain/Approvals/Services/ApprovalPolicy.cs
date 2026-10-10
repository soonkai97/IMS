namespace Ims.Domain;

/// <summary>BR-06 / BR-07: who must approve a document and whether it needs approval at all.</summary>
public static class ApprovalPolicy
{
    /// <summary>Approval authority. A higher rank may approve anything a lower rank may.</summary>
    public static int RankOf(string role) => role switch
    {
        Roles.Admin => 4,
        Roles.Owner => 3,
        Roles.Supervisor => 2,
        _ => 0
    };

    /// <summary>
    /// Which reviewer role a document is routed to (PRD 3.1): the Admin reviews anything, the Owner only large documents, a Supervisor only the
    /// rest. Used for approving and for rejecting, and by <c>allowedActions</c>, the approvals count and the inbox, so they cannot disagree.
    /// </summary>
    public static bool MayReview(string role, bool isLarge) => role switch
    {
        Roles.Admin => true,
        Roles.Owner => isLarge,
        Roles.Supervisor => !isLarge,
        _ => false
    };

    public static bool RequiresApproval(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount));

    /// <summary>A "large" document fired a rule that a supervisor may not satisfy (the Owner or the Admin must approve it).</summary>
    public static bool IsLarge(IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount) =>
        rules.Any(r => r.Matches(type, totalQty, totalAmount) && RankOf(r.RoleRequired) > RankOf(Roles.Supervisor));

    /// <summary>The approver must outrank every rule that fired; with no rule fired, a supervisor suffices.</summary>
    public static bool CanApprove(string approverRole, IEnumerable<ApprovalRule> rules, DocumentType type, decimal totalQty, decimal totalAmount)
    {
        var required = rules.Where(r => r.Matches(type, totalQty, totalAmount))
            .Select(r => RankOf(r.RoleRequired)).DefaultIfEmpty(RankOf(Roles.Supervisor)).Max();
        return RankOf(approverRole) >= required;
    }
}
