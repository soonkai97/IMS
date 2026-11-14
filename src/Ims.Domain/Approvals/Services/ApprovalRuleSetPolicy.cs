namespace Ims.Domain;

/// <summary>
/// The two rules that span the approval rules of one document type. A rule has its own row, public id and row version, so the "set" of a type is not a stored root: this policy takes the facts
/// the caller has read (the rule, the type and state it is being given, whether its module is on, how many active rules the type has) and answers. It reads nothing and locks nothing; the
/// handler takes the <c>modules</c> lock and makes the count, in that order, only when <see cref="NeedsActiveRuleCount"/> says the answer depends on it.
/// </summary>
public static class ApprovalRuleSetPolicy
{
    /// <summary>
    /// Whether an edit of <paramref name="rule"/> to <paramref name="newType"/> and <paramref name="newIsActive"/> is refused, and why. First the type: an order rule (purchase order, sales order)
    /// keeps its type, and no other rule becomes one (the tiers for orders are set up on purpose, not by editing a rule of another type). Then the last active rule: while the module is on, the last
    /// active rule of an order type cannot be switched off, because with none every priced order is released with no approver. <paramref name="activeRulesOfType"/> counts the active rules of the
    /// rule's current type, this one included while it is active, and is looked at only when <see cref="NeedsActiveRuleCount"/> is true; pass anything otherwise.
    /// </summary>
    public static ApprovalRuleRefusalReason Refusal(ApprovalRule rule, DocumentType newType, bool newIsActive, bool moduleOn, int activeRulesOfType)
    {
        var typeRefusal = TypeRefusal(rule.DocType, newType);
        if (typeRefusal != ApprovalRuleRefusalReason.None) return typeRefusal;
        if (!NeedsActiveRuleCount(rule, newType, newIsActive, moduleOn) || activeRulesOfType > 1) return ApprovalRuleRefusalReason.None;
        return rule.DocType == DocumentType.SalesOrder ? ApprovalRuleRefusalReason.LastSalesOrderRule : ApprovalRuleRefusalReason.LastPurchaseOrderRule;
    }

    /// <summary>
    /// Whether the count of active rules can change the answer of <see cref="Refusal"/>: an active order rule switched off while its module is on, and its type not refused first. A caller that has to
    /// take a lock and read the count asks this first, so neither happens when it does not matter.
    /// </summary>
    public static bool NeedsActiveRuleCount(ApprovalRule rule, DocumentType newType, bool newIsActive, bool moduleOn) =>
        moduleOn && ApprovalRuleModules.IsOrderType(rule.DocType) && rule.IsActive && !newIsActive && TypeRefusal(rule.DocType, newType) == ApprovalRuleRefusalReason.None;

    /// <summary>The sales order is named whenever either side of the move is one, the purchase order for any other move to or from an order type.</summary>
    private static ApprovalRuleRefusalReason TypeRefusal(DocumentType current, DocumentType target)
    {
        if (current == target || !(ApprovalRuleModules.IsOrderType(current) || ApprovalRuleModules.IsOrderType(target))) return ApprovalRuleRefusalReason.None;
        return current == DocumentType.SalesOrder || target == DocumentType.SalesOrder ? ApprovalRuleRefusalReason.SalesOrderRuleTypeFixed : ApprovalRuleRefusalReason.PurchaseOrderRuleTypeFixed;
    }
}
