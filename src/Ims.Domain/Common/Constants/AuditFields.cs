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

    /// <summary>The prices a price list save added, removed or changed, in words. A selling price is not cost: it is gated by <c>pricelist.manage</c>.</summary>
    [VisibleWith(Permissions.PricelistManage)]
    public const string ListPriceChanges = "ListPriceChanges";

    /// <summary>The price the price list gave a sales order line when the order was saved. A selling price: gated by <c>so.manage</c>. (Not named ListPrice: that name is the price-list item column, gated by <c>pricelist.manage</c>, and the audit scrubber works on names.)</summary>
    [VisibleWith(Permissions.SoManage)]
    public const string ListedPrice = "ListedPrice";

    /// <summary>The price the seller entered on the line instead.</summary>
    [VisibleWith(Permissions.SoManage)]
    public const string EnteredPrice = "EnteredPrice";
}
