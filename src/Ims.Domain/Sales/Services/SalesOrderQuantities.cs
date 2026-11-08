namespace Ims.Domain;

/// <summary>
/// The arithmetic of delivering against a sales order, in one place (plan D5), the twin of <c>PurchaseOrderQuantities</c>. Delivered quantity is COMPUTED, never stored: the sum over the Posted
/// deliveries linked to the order, per order line number (never per product: one product may be on several lines). A Draft, Submitted, Approved, Cancelled or Reversed delivery counts as nothing,
/// so a reversal frees its quantity without any code. The check is made in base units, the unit the ledger uses; the same figures in the line's own unit are for the sentence and the screen.
/// </summary>
public static class SalesOrderQuantities
{
    /// <summary>What is left of an ordered quantity: never below zero.</summary>
    public static decimal Remaining(decimal ordered, decimal delivered) => Math.Max(ordered - delivered, 0m);

    public static decimal RemainingBase(SalesOrderLine line, DeliveredQuantity delivered) => Remaining(line.BaseQty, delivered.BaseQty);

    public static decimal RemainingQty(SalesOrderLine line, DeliveredQuantity delivered) => Remaining(line.Qty, delivered.Qty);

    /// <summary>
    /// The order lines a delivery asks too much of. Lines of the delivery that name the same order line are added up first, so splitting a quantity over two lines does not get past the check.
    /// A line number the order does not have is not judged here (the caller refuses it with its own sentence). Nothing is stored or locked: the check at posting (PL3.7) is the authoritative one.
    /// </summary>
    public static IReadOnlyList<OverDelivery> OverDeliveries(IReadOnlyList<SalesOrderLine> orderLines, IReadOnlyDictionary<int, DeliveredQuantity> delivered, IEnumerable<DeliveryQuantity> deliveryLines)
    {
        var over = new List<OverDelivery>();
        foreach (var group in deliveryLines.GroupBy(l => l.LineNo).OrderBy(g => g.Key))
        {
            var line = orderLines.FirstOrDefault(l => l.LineNo == group.Key);
            if (line is null) continue;
            var already = delivered.GetValueOrDefault(line.LineNo);
            if (group.Sum(l => l.BaseQty) <= RemainingBase(line, already)) continue;
            over.Add(new OverDelivery(line.LineNo, line.Qty, already.Qty, RemainingQty(line, already), group.Sum(l => l.Qty)));
        }
        return over;
    }
}
