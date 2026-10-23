namespace Ims.Domain;

/// <summary>
/// Keys of a business-event audit payload that carry cost or value. A payload that holds money names its key here (an anonymous object cannot be
/// tagged), so the audit log can withhold it from a caller without the permission. Row-level audit payloads are named after entity properties,
/// which are tagged on the entity itself.
/// </summary>
public static class AuditFields
{
    /// <summary>The value of the document when it was submitted.</summary>
    [VisibleWith(Permissions.CostView)]
    public const string Amount = "Amount";

    /// <summary>The value of a sales order at selling price. A selling price is not cost, so it is gated by <c>so.manage</c>, not <c>cost.view</c>.</summary>
    [VisibleWith(Permissions.SoManage)]
    public const string SellingAmount = "SellingAmount";
}
