namespace Ims.Domain;

/// <summary>Read model rebuilt from the ledger. Carries the moving weighted-average cost (BR-05).</summary>
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
        // While stock is zero or negative the old average is meaningless, so the new cost wins.
        AvgCost = QtyOnHand <= 0 || newQty <= 0
            ? unitCost
            : Math.Round((QtyOnHand * AvgCost + qty * unitCost) / newQty, Limits.RoundingDecimals);
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
}
