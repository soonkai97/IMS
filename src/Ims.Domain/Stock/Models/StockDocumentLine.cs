namespace Ims.Domain;

public class StockDocumentLine : Entity
{
    private StockDocumentLine() { }

    public int DocumentId { get; private set; }
    public int ProductId { get; private set; }
    /// <summary>Quantity as entered, in <see cref="UomId"/>.</summary>
    public decimal Qty { get; private set; }
    public int UomId { get; private set; }
    /// <summary>Quantity converted to the product's base unit (BR-04). The ledger always uses this.</summary>
    public decimal BaseQty { get; private set; }
    /// <summary>Per base unit. Receipts: purchase cost. Issues: filled with the average cost at posting.</summary>
    public decimal UnitCost { get; private set; }
    public string? Remark { get; private set; }

    public StockDocument Document { get; private set; } = null!;
    public Product Product { get; private set; } = null!;
    public Uom Uom { get; private set; } = null!;

    /// <summary>
    /// Builds a line: the product must be usable, quantities convert to the base unit, and the entered cost
    /// (per entered unit) becomes a cost per base unit.
    /// </summary>
    public static StockDocumentLine Create(Product product, DocumentType documentType, decimal qty, int? uomId, decimal? unitCost, string? remark)
    {
        product.EnsureUsableOnDocuments();
        if (qty <= 0) throw new DomainException(Messages.QtyPositive);
        if (unitCost < 0) throw new DomainException(Messages.CostNegative);

        var uom = uomId ?? product.BaseUomId;
        var baseQty = product.ToBaseQuantity(qty, uom);
        return new StockDocumentLine
        {
            // Only the key is stored: the product may be a detached read copy, and assigning it as a navigation would make EF insert it again.
            ProductId = product.Id, Qty = qty, UomId = uom, BaseQty = baseQty,
            UnitCost = CostingPolicy.BaseUnitCost(documentType, unitCost, qty, baseQty), Remark = remark
        };
    }

    /// <summary>The current average cost, kept on an unposted issue so its value can be shown and tested against approval rules. Posting replaces it.</summary>
    internal void EstimateIssueCost(decimal averageCost) => UnitCost = averageCost;

    /// <summary>Issues are booked at the average cost in force when they post (BR-05).</summary>
    internal void BookIssueCost(decimal averageCost) => UnitCost = averageCost;
}
