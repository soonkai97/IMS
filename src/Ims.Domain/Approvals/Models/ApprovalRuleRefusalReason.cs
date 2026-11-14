namespace Ims.Domain;

/// <summary>Why an edit of an approval rule is refused (<see cref="ApprovalRuleSetPolicy.Refusal"/>). Application turns it into the sentence and the status: the first two are a 400, the last two a 409.</summary>
public enum ApprovalRuleRefusalReason
{
    /// <summary>The edit may go ahead.</summary>
    None,

    /// <summary>The rule would be moved to or from purchase orders.</summary>
    PurchaseOrderRuleTypeFixed,

    /// <summary>The rule would be moved to or from sales orders (named whenever either side is a sales order).</summary>
    SalesOrderRuleTypeFixed,

    /// <summary>The last active purchase order rule would be switched off while purchasing is on.</summary>
    LastPurchaseOrderRule,

    /// <summary>The last active sales order rule would be switched off while sales is on.</summary>
    LastSalesOrderRule,
}
