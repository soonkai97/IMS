namespace Ims.Domain;

/// <summary>
/// One line of a purchase order: a product, a quantity in any unit the product converts, and the price the supplier asked per ENTERED unit. The base quantity (BR-04) is
/// converted once when the line is built; the amount is <c>round(entered quantity x entered price, 2)</c> with no division in between, as on a receipt line.
/// </summary>
public class PurchaseOrderLine : Entity
{
    private PurchaseOrderLine() { }

    public int PurchaseOrderId { get; private set; }

    /// <summary>1 to n inside the order, in the order the lines were entered; the order assigns it (<see cref="PurchaseOrder.ReplaceLines"/>). Frozen once the order is Approved (receipts will name a line by it).</summary>
    public int LineNo { get; private set; }
    public int ProductId { get; private set; }

    /// <summary>Quantity as entered, in <see cref="UomId"/>.</summary>
    public decimal Qty { get; private set; }
    public int UomId { get; private set; }

    /// <summary>Quantity converted to the product's base unit. Received quantities are summed in it.</summary>
    public decimal BaseQty { get; private set; }

    /// <summary>The price per ENTERED unit, in money (2 decimals): what the buyer typed. 0.00 is allowed on a draft (a reorder draft starts there).</summary>
    [VisibleWith(Permissions.CostView)]
    public decimal UnitPrice { get; private set; }

    /// <summary>The value of the line in money, stored when the line was built: <c>round(qty x unit price, 2)</c>, half away from zero (<see cref="Money.Rounding"/>). An order total is the sum of these.</summary>
    [VisibleWith(Permissions.CostView)]
    public decimal Amount { get; private set; }
    public string? Remark { get; private set; }

    public PurchaseOrder PurchaseOrder { get; private set; } = null!;
    public Product Product { get; private set; } = null!;
    public Uom Uom { get; private set; } = null!;

    /// <summary>Builds a line. The product must be usable; the quantity, the price and the amount must fit the limits; the unit must convert to the product's base unit.</summary>
    public static PurchaseOrderLine Create(Product product, decimal qty, int? uomId, decimal unitPrice, string? remark)
    {
        product.EnsureUsableOnDocuments();
        if (qty <= 0) throw new DomainException(Messages.QtyPositive);
        if (unitPrice < 0) throw new DomainException(Messages.PriceNegative);
        if (unitPrice > Limits.MaxUnitCost) throw new DomainException(Messages.PriceTooLarge);
        if (!Money.IsExact(unitPrice)) throw new DomainException(Messages.PriceTooPrecise);

        var uom = uomId ?? product.BaseUomId;
        var baseQty = product.ToBaseQuantity(qty, uom);
        if (baseQty > Limits.MaxBaseQuantity) throw new DomainException(Messages.QtyTooLarge);
        var amount = Money.Amount(qty, unitPrice);
        if (amount > Limits.MaxLineAmount) throw new DomainException(Messages.LineAmountTooLarge);

        // Only the key is stored: the product may be a detached read copy, and assigning it as a navigation would make EF insert it again.
        return new PurchaseOrderLine { ProductId = product.Id, Qty = qty, UomId = uom, BaseQty = baseQty, UnitPrice = unitPrice, Amount = amount, Remark = remark };
    }

    internal void Number(int lineNo) => LineNo = lineNo;
}
