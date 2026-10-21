namespace Ims.Domain;

/// <summary>
/// The arithmetic of receiving against a purchase order, in one place (plan D4). Received quantity is COMPUTED, never stored: the sum over the Posted receipts linked to the order, per
/// order line number (never per product: one product may be on several lines). A Draft, Submitted, Approved, Cancelled or Reversed receipt counts as nothing, so a reversal frees its
/// quantity without any code. The check is made in base units, the unit the ledger uses; the same figures in the line's own unit are for the sentence and for the screen.
/// </summary>
public static class PurchaseOrderQuantities
{
    /// <summary>What is left of an ordered quantity: never below zero (a receipt posted before the check existed, or an order line reduced, cannot make it negative).</summary>
    public static decimal Remaining(decimal ordered, decimal received) => Math.Max(ordered - received, 0m);

    public static decimal RemainingBase(PurchaseOrderLine line, ReceivedQuantity received) => Remaining(line.BaseQty, received.BaseQty);

    public static decimal RemainingQty(PurchaseOrderLine line, ReceivedQuantity received) => Remaining(line.Qty, received.Qty);

    /// <summary>
    /// The order lines a receipt asks too much of. Lines of the receipt that name the same order line are added up first, so splitting a quantity over two lines does not get past the check.
    /// A line number the order does not have is not judged here (the caller refuses it with its own sentence). Nothing is stored or locked: the check at posting is the authoritative one (PL2.5).
    /// </summary>
    public static IReadOnlyList<OverReceipt> OverReceipts(IReadOnlyList<PurchaseOrderLine> orderLines, IReadOnlyDictionary<int, ReceivedQuantity> received,
        IEnumerable<ReceiptQuantity> receiptLines)
    {
        var over = new List<OverReceipt>();
        foreach (var group in receiptLines.GroupBy(l => l.LineNo).OrderBy(g => g.Key))
        {
            var line = orderLines.FirstOrDefault(l => l.LineNo == group.Key);
            if (line is null) continue;
            var already = received.GetValueOrDefault(line.LineNo);
            if (group.Sum(l => l.BaseQty) <= RemainingBase(line, already)) continue;
            over.Add(new OverReceipt(line.LineNo, line.Qty, already.Qty, RemainingQty(line, already), group.Sum(l => l.Qty)));
        }
        return over;
    }
}
