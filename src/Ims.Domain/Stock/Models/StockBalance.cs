namespace Ims.Domain;

/// <summary>
/// Read model rebuilt from the ledger. Carries the moving weighted-average cost (BR-05). The stock value of a balance is always
/// quantity x average cost: nothing else is stored, so a value can never disagree with the pieces it is made of.
/// </summary>
public class StockBalance : AuditableEntity
{
    private StockBalance() { }

    public int ProductId { get; private set; }
    public int WarehouseId { get; private set; }
    public decimal QtyOnHand { get; private set; }
    public decimal QtyInTransit { get; private set; }
    [VisibleWith(Permissions.ValuationView)]
    public decimal AvgCost { get; private set; }

    public Product Product { get; private set; } = null!;
    public Warehouse Warehouse { get; private set; } = null!;

    /// <summary>A zero balance for a product that has never been stocked in the warehouse.</summary>
    public static StockBalance OpenFor(int productId, int warehouseId) => new() { ProductId = productId, WarehouseId = warehouseId };

    /// <summary>Increase stock and update the moving weighted-average cost.</summary>
    public void Receive(decimal qty, decimal unitCost)
    {
        if (qty <= 0) throw new DomainException(Messages.ReceiveQtyPositive);
        var newQty = QtyOnHand + qty;
        // While stock is zero or negative the old average is meaningless, so the new cost wins. Stock that comes in at the average itself
        // (the return of an issue) leaves it exactly as it is, instead of deriving it again and drifting in the last place.
        AvgCost = QtyOnHand <= 0 || newQty <= 0 ? unitCost
            : unitCost == AvgCost ? AvgCost
            : Math.Round((QtyOnHand * AvgCost + qty * unitCost) / newQty, Limits.CostDecimals);
        QtyOnHand = newQty;
    }

    /// <summary>Decrease stock and return the unit cost to book (the current average).</summary>
    public decimal Issue(decimal qty, bool allowNegative)
    {
        if (qty <= 0) throw new DomainException(Messages.IssueQtyPositive);
        if (!allowNegative && QtyOnHand < qty)
            throw new InsufficientStockException(ProductId, WarehouseId, QtyOnHand, qty);
        QtyOnHand -= qty;
        return AvgCost;
    }

    /// <summary>
    /// Take a posted receipt back out: <paramref name="qty"/> units leave, and the average is corrected so that the cost of the receipt leaves with them.
    /// <list type="number">
    /// <item>The average has not moved since the receipt posted (nothing came in after it, only issues, which never change the average): it goes back to the
    /// value it had BEFORE the receipt, exactly, however many units were issued in between. The receipt is undone as if it had never happened, so a duplicate
    /// delivery entered at a very high cost leaves no trace in the cost of the stock.</item>
    /// <item>A later receipt blended into the average: the stock loses <c>qty x unitCost</c> of value (what came in at that cost goes out at that cost) and the
    /// remaining units share what is left. That is exact when nothing was issued since, so the value stays equal to the value of the movements.</item>
    /// <item>Where that arithmetic has no sound answer (no stock is left, or later issues already used up more value than remains) the average stays as it is
    /// now: it is the last known cost, and the next receipt replaces it when stock is zero or negative.</item>
    /// </list>
    /// Documented in <c>docs/design/mvp-ux.md</c> (costing and reversal).
    /// </summary>
    /// <param name="posted">What the receipt did to the average when it posted; null for a receipt posted before that was recorded.</param>
    public void Unreceive(decimal qty, decimal unitCost, AverageCostChange? posted, bool allowNegative)
    {
        if (qty <= 0) throw new DomainException(Messages.ReceiveQtyPositive);
        if (!allowNegative && QtyOnHand < qty)
            throw new InsufficientStockException(ProductId, WarehouseId, QtyOnHand, qty);

        var remaining = QtyOnHand - qty;
        AvgCost = AverageAfterUnreceive(qty, unitCost, remaining, posted);
        QtyOnHand = remaining;
    }

    private decimal AverageAfterUnreceive(decimal qty, decimal unitCost, decimal remaining, AverageCostChange? posted)
    {
        if (posted is { } change && change.After == AvgCost) return change.Before;
        if (remaining <= 0 || QtyOnHand <= 0) return AvgCost;
        var valueLeft = QtyOnHand * AvgCost - qty * unitCost;
        return valueLeft > 0 ? Math.Round(valueLeft / remaining, Limits.CostDecimals) : AvgCost;
    }
}
