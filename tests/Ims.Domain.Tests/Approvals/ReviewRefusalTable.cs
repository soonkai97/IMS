namespace Ims.Domain.Tests.Approvals;

/// <summary>What a reviewer is told. The member names are the ones of <c>ApprovalPolicy</c>'s refusal enum, so a policy answer compares by name.</summary>
public enum ReviewOutcome { None, OwnerReviewsLargeOnly, AboveApprovalLimit }

/// <summary>
/// DDD-07: the question "may this caller approve or reject this document, and if not, why" over rights x stored tier x rules in force x document type x quantity x amount x return.
/// First asked of <c>ApprovalEvaluator</c> (characterization of today's code), then of <c>ApprovalPolicy.ReviewRefusal</c>: one table, so both give the same answers.
/// The expected outcome is written out here from the rule in PRD 3.1 (BR-06), independent of the code under test: routing by the tier the document was stored with, a return has no limit,
/// then a Large rule in force needs the large tier.
/// </summary>
public static class ReviewRefusalTable
{
    public sealed record Caller(string Name, string[] Held)
    {
        public bool HoldsStandard => Held.Contains(Permissions.DocumentApprove);
        public bool HoldsLarge => Held.Contains(Permissions.DocumentApproveLarge);
        public ActorRights Rights() => new(7, EffectivePermissions.Compute(Held));
    }

    public sealed record Rule(DocumentType Type, decimal? Amount, decimal? Qty, ApproverTier Tier, bool Active = true)
    {
        public ApprovalRule ToEntity() => ApprovalRule.Create(Type, Amount, Qty, Tier, Active);

        /// <summary>The rule fires: active, this type, and either threshold reached (at or above).</summary>
        public bool Fires(DocumentType type, decimal qty, decimal amount) =>
            Active && Type == type && ((Amount is { } a && amount >= a) || (Qty is { } q && qty >= q));
    }

    public sealed record RuleSet(string Name, Rule[] Rules);

    public static readonly Caller[] Callers =
    [
        new("supervisor", [Permissions.DocumentApprove]),
        new("owner", [Permissions.DocumentApproveLarge]),
        new("admin", [Permissions.DocumentApprove, Permissions.DocumentApproveLarge]),
        new("nobody", []),
        new("operator", [Permissions.CostView]), // holds some permission, neither tier
    ];

    public static readonly RuleSet[] RuleSets =
    [
        new("none", []),
        new("standard 100", [new(DocumentType.Receipt, 100m, null, ApproverTier.Standard)]),
        new("large 1000", [new(DocumentType.Receipt, 1000m, null, ApproverTier.Large)]),
        new("standard 100 + large 1000", [new(DocumentType.Receipt, 100m, null, ApproverTier.Standard), new(DocumentType.Receipt, 1000m, null, ApproverTier.Large)]),
        new("large by qty 50", [new(DocumentType.Receipt, null, 50m, ApproverTier.Large)]),
        new("large inactive", [new(DocumentType.Receipt, 1m, null, ApproverTier.Large, Active: false)]),
        new("large for issues", [new(DocumentType.Issue, 1m, null, ApproverTier.Large)]),
        new("large for purchase orders by amount or qty", [new(DocumentType.PurchaseOrder, 500m, 20m, ApproverTier.Large)]),
    ];

    public static readonly DocumentType[] Types = [DocumentType.Receipt, DocumentType.Issue, DocumentType.PurchaseOrder, DocumentType.SalesOrder];

    /// <summary>(quantity, amount): below, on and above the thresholds above, and an empty document.</summary>
    public static readonly (decimal Qty, decimal Amount)[] Sizes = [(0m, 0m), (1m, 50m), (1m, 99.99m), (1m, 100m), (1m, 999.99m), (1m, 1000m), (49m, 0m), (50m, 0m), (20m, 499.99m), (1m, 5000m)];

    public sealed record Case(Caller Caller, bool IsLarge, RuleSet Rules, DocumentType Type, decimal Qty, decimal Amount, bool IsReturn)
    {
        public override string ToString() => $"{Caller.Name}, stored {(IsLarge ? "large" : "standard")}, rules [{Rules.Name}], {Type}, qty {Qty}, amount {Amount}{(IsReturn ? ", return" : "")}";
    }

    public static IEnumerable<Case> All() =>
        from caller in Callers
        from isLarge in new[] { false, true }
        from rules in RuleSets
        from type in Types
        from size in Sizes
        from isReturn in new[] { false, true }
        select new Case(caller, isLarge, rules, type, size.Qty, size.Amount, isReturn);

    public static ReviewOutcome Expected(Case c)
    {
        // 1. Routing by the stored tier: a large document goes to the large tier, any other to the standard tier.
        var routed = c.IsLarge ? c.Caller.HoldsLarge : c.Caller.HoldsStandard;
        if (!routed) return c.Caller.HoldsLarge && !c.Caller.HoldsStandard ? ReviewOutcome.OwnerReviewsLargeOnly : ReviewOutcome.AboveApprovalLimit;

        // 2. A return has no limit.
        if (c.IsReturn) return ReviewOutcome.None;

        // 3. The rules in force now: a Large rule that fires needs the large tier, whatever tier the document was stored with.
        var largeFires = c.Rules.Rules.Any(r => r.Tier == ApproverTier.Large && r.Fires(c.Type, c.Qty, c.Amount));
        return largeFires && !c.Caller.HoldsLarge ? ReviewOutcome.AboveApprovalLimit : ReviewOutcome.None;
    }
}
