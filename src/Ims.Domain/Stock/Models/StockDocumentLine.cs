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
    /// <summary>
    /// Per BASE unit, with more places than money (an internal number: a carton price spread over 12 pieces). Receipts: purchase cost. Issues: filled with the average
    /// cost at posting. It values stock; it does not make the line amount (see <see cref="Amount"/>).
    /// </summary>
    [VisibleWith(Permissions.CostView)]
    public decimal UnitCost { get; private set; }
    public string? Remark { get; private set; }

    /// <summary>The average cost of the product just before this receipt line posted. Set at posting, only on receipt lines; null before that and for lines posted before it was recorded.</summary>
    [VisibleWith(Permissions.ValuationView)]
    public decimal? AvgCostBefore { get; private set; }
    /// <summary>The average cost this receipt line produced (kept for the audit trail and the repair query; a reversal looks at <see cref="PreviousLineId"/> and the balance's marker, not at this value).</summary>
    [VisibleWith(Permissions.ValuationView)]
    public decimal? AvgCostAfter { get; private set; }

    /// <summary>The line that was the balance's latest stock-in just before this one posted (see <see cref="StockBalance.LastReceiptLineId"/>); null if none or not recorded.</summary>
    public int? PreviousLineId { get; private set; }

    /// <summary>What this line did to the moving average when it posted, if that was recorded.</summary>
    public AverageCostChange? PostedAverage => AvgCostBefore is { } before && AvgCostAfter is { } after ? new AverageCostChange(before, after, PreviousLineId) : null;

    public StockDocument Document { get; private set; } = null!;
    public Product Product { get; private set; } = null!;
    public Uom Uom { get; private set; } = null!;

    /// <summary>
    /// The cost per ENTERED unit, in money (2 decimals): what the person typed for a receipt (1 carton at 10.00 is 10.00, whatever the carton holds), and for a line
    /// that took the average (an issue, a staff receipt) the average expressed per entered unit. Reading a draft and saving it again sends this value back, so a
    /// line in a non-base unit is never re-priced.
    /// </summary>
    [VisibleWith(Permissions.CostView)]
    public decimal EnteredUnitCost { get; private set; }

    /// <summary>
    /// The value of the line in money, STORED: for a receipt <c>round(entered quantity x entered unit cost, 2)</c> with no division in between (100,000 cartons at 10.00
    /// are 1,000,000.00 whatever the carton holds); for a line valued at the average <c>round(base quantity x average, 2)</c>. Rounded half away from zero
    /// (<see cref="Money.Rounding"/>). A document total is the sum of these.
    /// </summary>
    [VisibleWith(Permissions.CostView)]
    public decimal Amount { get; private set; }

    /// <summary>
    /// True when nobody typed this line's cost: it is valued at the average cost of the product (an issue always; a receipt saved by someone who may not enter
    /// cost, and a receipt line whose cost was left out of a draft that already had it taken from the average). Such a line has no cost to edit
    /// (<see cref="EnteredUnitCost"/> is only its average per entered unit, shown for information): saving the draft again keeps it at the average, so a Warehouse
    /// staff receipt is never re-priced by someone who opens and saves it. A cost typed on the line makes it a typed cost (false).
    /// </summary>
    public bool CostFromAverage { get; private set; }

    /// <summary>
    /// Builds a line: the product must be usable, quantities convert to the base unit, the entered cost (per entered unit, at most 2 decimals on a receipt) becomes a
    /// cost per base unit for stock valuation, and the line amount is computed once from the entered quantity and cost.
    /// </summary>
    /// <param name="costFromAverage">The receipt line takes the average of the product (the cost is not typed): the caller values it afterwards with <see cref="StockDocument.ApplyKnownReceiptCosts"/>. An issue is always valued at the average.</param>
    public static StockDocumentLine Create(Product product, DocumentType documentType, decimal qty, int? uomId, decimal? unitCost, string? remark, bool costFromAverage = false)
    {
        product.EnsureUsableOnDocuments();
        if (qty <= 0) throw new DomainException(Messages.QtyPositive);
        var fromAverage = documentType != DocumentType.Receipt || costFromAverage;
        var typed = fromAverage ? null : unitCost;
        if (typed > Limits.MaxUnitCost) throw new DomainException(Messages.CostTooLarge);
        if (typed < 0) throw new DomainException(Messages.CostNegative);
        if (typed is { } entered && !Money.IsExact(entered)) throw new DomainException(Messages.CostTooPrecise);

        var uom = uomId ?? product.BaseUomId;
        var baseQty = product.ToBaseQuantity(qty, uom);
        var enteredCost = typed ?? 0m;
        return new StockDocumentLine
        {
            // Only the key is stored: the product may be a detached read copy, and assigning it as a navigation would make EF insert it again.
            ProductId = product.Id, Qty = qty, UomId = uom, BaseQty = baseQty,
            UnitCost = CostingPolicy.BaseUnitCost(documentType, typed, qty, baseQty), Remark = remark,
            EnteredUnitCost = enteredCost, Amount = EnsureAmountFits(Money.Amount(qty, enteredCost)), CostFromAverage = fromAverage
        };
    }

    /// <summary>The current average cost, kept on an unposted issue so its value can be shown and tested against approval rules. Posting replaces it.</summary>
    internal void EstimateIssueCost(decimal averageCost) => ValueAtAverage(averageCost);

    /// <summary>The cost a receipt line takes when whoever saved it may not enter a cost: the last known (average) cost per base unit.</summary>
    internal void SetKnownCost(decimal costPerBaseUnit) => ValueAtAverage(costPerBaseUnit);

    /// <summary>Issues are booked at the average cost in force when they post (BR-05).</summary>
    internal void BookIssueCost(decimal averageCost) => ValueAtAverage(averageCost);

    /// <summary>The line takes a cost per base unit (the average): the amount is the base quantity times it, and the entered-unit cost is the same cost per entered unit.</summary>
    private void ValueAtAverage(decimal costPerBaseUnit)
    {
        UnitCost = costPerBaseUnit;
        Amount = EnsureAmountFits(Money.Amount(BaseQty, costPerBaseUnit));
        EnteredUnitCost = Qty > 0 ? EnsureAmountFits(Money.Round(costPerBaseUnit * BaseQty / Qty)) : 0m;
    }

    /// <summary>A line amount is stored as <c>decimal(18,2)</c>: above <see cref="Limits.MaxLineAmount"/> the answer is a 400 with a sentence, never a database overflow.</summary>
    private static decimal EnsureAmountFits(decimal amount) =>
        amount > Limits.MaxLineAmount ? throw new DomainException(Messages.LineAmountTooLarge) : amount;

    /// <summary>Remembers what posting this receipt line did to the moving average, so reversing it can put the average back.</summary>
    internal void RecordAverageChange(decimal before, decimal after, int? previousLineId)
    {
        AvgCostBefore = before;
        AvgCostAfter = after;
        PreviousLineId = previousLineId;
    }
}
