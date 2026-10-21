namespace Ims.Domain;

/// <summary>
/// The reorder suggestion in its simplest form (PRD AI-02, pilot plan D8): a product that is low in a warehouse (<see cref="LowStockRule"/>) is topped up to <see cref="TargetMultiple"/> times its
/// safety level, less what is already on order. The target is above the safety level because "low" means AT OR BELOW it, so ordering up to the level would suggest nothing for a product that sits
/// exactly on it. A negative balance counts as it is: on hand -4 with no safety level suggests 4. Quantities are base units and keep the scale of every quantity (<see cref="Limits.QuantityScale"/>);
/// the catalog has no countable-unit flag, so nothing is forced to whole units.
/// </summary>
public static class ReorderRule
{
    /// <summary>Order up to this many times the safety level (owner's default for plan question 3).</summary>
    public const decimal TargetMultiple = 2m;

    /// <summary>
    /// The orders whose remaining quantity counts as "on order": every open one. A Draft counts so that two buyers do not order the same shortage twice and the row leaves the list as soon as a draft
    /// exists; a Closed order has nothing left to come and a Cancelled one never will. Received quantity is taken off an Approved order line by line (see <see cref="PurchaseOrderQuantities"/>).
    /// </summary>
    public static readonly PurchaseOrderStatus[] OnOrderStatuses = [PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted, PurchaseOrderStatus.Approved];

    /// <summary>What is still missing: the target less on hand less on order, never below zero. Zero means the row is not listed.</summary>
    public static decimal SuggestedQty(decimal safetyStock, decimal qtyOnHand, decimal onOrderQty) =>
        Math.Max(TargetMultiple * safetyStock - qtyOnHand - onOrderQty, 0m);
}
