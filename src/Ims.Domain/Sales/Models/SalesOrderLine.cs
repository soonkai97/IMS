namespace Ims.Domain;

/// <summary>
/// One line of a sales order: a product, a quantity in any unit the product converts, and the price asked of the customer per ENTERED unit. The base quantity (BR-04) is converted once when
/// the line is built; the amount is <c>round(entered quantity x entered price, 2)</c> with no division in between. A selling price is not cost: it is gated by <c>so.manage</c>. The properties have names of their own (SellingPrice, SellingAmount) because the audit scrubber withholds a tagged NAME unless the caller holds every permission that tags it, and UnitPrice and Amount are already the cost-gated names of the purchase order and document lines.
/// </summary>
public class SalesOrderLine : Entity
{
    private SalesOrderLine() { }

    public int SalesOrderId { get; private set; }

    /// <summary>1 to n inside the order, in the order the lines were entered; the order assigns it (<see cref="SalesOrder.ReplaceLines"/>). Frozen once the order is Approved (deliveries will name a line by it).</summary>
    public int LineNo { get; private set; }
    public int ProductId { get; private set; }

    /// <summary>Quantity as entered, in <see cref="UomId"/>.</summary>
    public decimal Qty { get; private set; }
    public int UomId { get; private set; }

    /// <summary>Quantity converted to the product's base unit. Delivered quantities are summed in it.</summary>
    public decimal BaseQty { get; private set; }

    /// <summary>The price per ENTERED unit, in money (2 decimals): what the seller typed. 0.00 is allowed on a draft; submitting it is refused (plan D15, PL3.3).</summary>
    [VisibleWith(Permissions.SoManage)]
    public decimal SellingPrice { get; private set; }

    /// <summary>The value of the line in money, stored when the line was built: <c>round(qty x unit price, 2)</c>, half away from zero (<see cref="Money.Rounding"/>). An order total is the sum of these.</summary>
    [VisibleWith(Permissions.SoManage)]
    public decimal SellingAmount { get; private set; }
    public string? Remark { get; private set; }

    public SalesOrder SalesOrder { get; private set; } = null!;
    public Product Product { get; private set; } = null!;
    public Uom Uom { get; private set; } = null!;

    /// <summary>Builds a line. The product must be usable; the quantity, the price and the amount must fit the limits; the unit must convert to the product's base unit.</summary>
    public static SalesOrderLine Create(Product product, decimal qty, int? uomId, decimal unitPrice, string? remark)
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
        return new SalesOrderLine { ProductId = product.Id, Qty = qty, UomId = uom, BaseQty = baseQty, SellingPrice = unitPrice, SellingAmount = amount, Remark = remark };
    }

    internal void Number(int lineNo) => LineNo = lineNo;
}
