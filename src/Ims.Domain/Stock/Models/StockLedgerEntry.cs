namespace Ims.Domain;

/// <summary>One stock movement. Append-only (BR-01): enforced in the DbContext and by a database trigger.</summary>
public class StockLedgerEntry : AuditableEntity
{
    private StockLedgerEntry() { }

    public long Id { get; private set; }
    public int ProductId { get; private set; }
    public int WarehouseId { get; private set; }
    public decimal QtyDelta { get; private set; }
    /// <summary>
    /// What the product held in the warehouse right after this movement (base units): the sum of its ledger up to and including this row. Set when the
    /// entry is created by the posting run (<see cref="PostingContext.AddEntry"/>), under the (warehouse, product) lock; the row is never updated, so the
    /// ledger stays append-only. Reading a deep page of a product's ledger needs no aggregation.
    /// </summary>
    public decimal BalanceAfter { get; private set; }
    [VisibleWith(Permissions.ValuationView)]
    public decimal UnitCost { get; private set; }
    public int SourceDocId { get; private set; }
    public LedgerReason Reason { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public int CreatedBy { get; private set; }

    public Product Product { get; private set; } = null!;
    public Warehouse Warehouse { get; private set; } = null!;
    public StockDocument SourceDocument { get; private set; } = null!;
    public User Creator { get; private set; } = null!;

    public static StockLedgerEntry Create(StockDocument source, int productId, decimal qtyDelta, decimal balanceAfter, decimal unitCost, LedgerReason reason, int createdBy, DateTime at) =>
        new()
        {
            ProductId = productId, WarehouseId = source.WarehouseId, QtyDelta = qtyDelta, BalanceAfter = balanceAfter, UnitCost = unitCost,
            SourceDocId = source.Id, Reason = reason, OccurredAt = at, CreatedBy = createdBy
        };
}
