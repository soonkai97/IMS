namespace Ims.Domain.Tests.Approvals;

/// <summary>What an edit of an approval rule comes to. The member names are the ones of the policy's refusal enum, so a policy answer compares by name.</summary>
public enum RuleChangeOutcome { None, PurchaseOrderRuleTypeFixed, SalesOrderRuleTypeFixed, LastPurchaseOrderRule, LastSalesOrderRule }

/// <summary>
/// DDD-10: the two rules that span the approval rules of a document type, over every old type x new type x active before x active after x active rules of the old type.
/// First asked of <c>UpdateApprovalRuleHandler</c> (characterization of today's code), then of <c>ApprovalRuleSetPolicy</c>: one table, so both give the same answers.
/// The expected outcome is written out here from the two sentences of the rule (UpdateApprovalRuleHandler.cs:15-23), independent of the code under test:
/// (1) an order rule (purchase order, sales order) keeps its type, and no other rule becomes one; (2) while the module is on, the last active rule of an order type cannot be switched off.
/// </summary>
public static class ApprovalRuleChangeTable
{
    public static readonly DocumentType[] Types = Enum.GetValues<DocumentType>();

    public static bool IsOrder(DocumentType type) => type is DocumentType.PurchaseOrder or DocumentType.SalesOrder;

    /// <param name="ActiveOfType">How many active rules the old type has now, this rule included when it is active.</param>
    public sealed record Case(DocumentType Type, bool Active, DocumentType NewType, bool NewActive, int ActiveOfType)
    {
        public override string ToString() => $"{(Active ? "active" : "inactive")} {Type} rule -> {(NewActive ? "active" : "inactive")} {NewType}, {ActiveOfType} active of {Type}";
    }

    public static RuleChangeOutcome Expected(Case c, bool moduleOn = true)
    {
        // (1) The type of an order rule is fixed, and a rule of another type cannot be moved onto an order type. A sales order is named when either side is one.
        if (c.Type != c.NewType && (IsOrder(c.Type) || IsOrder(c.NewType)))
            return c.Type == DocumentType.SalesOrder || c.NewType == DocumentType.SalesOrder ? RuleChangeOutcome.SalesOrderRuleTypeFixed : RuleChangeOutcome.PurchaseOrderRuleTypeFixed;

        // (2) Only switching an active order rule off can leave the type without one, and only while its module is on; the rule itself is one of the active ones counted.
        if (moduleOn && IsOrder(c.Type) && c.Active && !c.NewActive && c.ActiveOfType <= 1)
            return c.Type == DocumentType.SalesOrder ? RuleChangeOutcome.LastSalesOrderRule : RuleChangeOutcome.LastPurchaseOrderRule;

        return RuleChangeOutcome.None;
    }

    /// <summary>Whether the answer needs the count of active rules (and so the <c>modules</c> lock the handler takes before it): an active order rule switched off, its type unchanged.</summary>
    public static bool NeedsCount(Case c, bool moduleOn = true) => moduleOn && IsOrder(c.Type) && c.Active && !c.NewActive && c.Type == c.NewType;

    /// <summary>Active rules of the old type besides this one: 0, 1, 2 (so the total is 0 to 3 for an inactive rule and 1 to 3 for an active one).</summary>
    public static IEnumerable<Case> All() =>
        from type in Types
        from active in new[] { true, false }
        from newType in Types
        from newActive in new[] { true, false }
        from others in new[] { 0, 1, 2 }
        select new Case(type, active, newType, newActive, others + (active ? 1 : 0));
}
