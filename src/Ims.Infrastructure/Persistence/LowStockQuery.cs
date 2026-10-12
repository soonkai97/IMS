namespace Ims.Infrastructure.Persistence;

/// <summary>
/// The low-stock query, shared by the list, its total, the Home count and the export (see <c>ILowStockReader</c> for the definition). The two sets come from
/// <see cref="LowStockSets"/>, the same ones the stock list's "low only" filter uses; this class only shapes them into rows. All parameters; no raw SQL.
/// </summary>
internal static class LowStockQuery
{
    /// <summary>Plain properties (not a constructor) so both branches have the same shape and EF can concatenate them.</summary>
    internal sealed class Row
    {
        public Guid ProductId { get; init; }
        public string Sku { get; init; } = "";
        public string ProductName { get; init; } = "";
        /// <summary>Null on the row of a product that is stocked in no warehouse the caller can see.</summary>
        public Guid? WarehouseId { get; init; }
        public string? WarehouseCode { get; init; }
        public string? WarehouseName { get; init; }
        public decimal QtyOnHand { get; init; }
        public decimal SafetyStock { get; init; }
    }

    public static IQueryable<Row> Build(AppDbContext db, IReadOnlyCollection<int>? scope, int? warehouseId)
    {
        var stocked = LowStockSets.LowBalances(db, scope, warehouseId)
            .Select(b => new Row
            {
                ProductId = b.Product.PublicId, Sku = b.Product.Sku, ProductName = b.Product.Name, WarehouseId = b.Warehouse.PublicId,
                WarehouseCode = b.Warehouse.Code, WarehouseName = b.Warehouse.Name, QtyOnHand = b.QtyOnHand, SafetyStock = b.Product.SafetyStock
            });

        // Stocked in no warehouse the caller can see: one row per product, no warehouse. They belong to no warehouse, so a filter on one leaves them out.
        if (warehouseId is not null) return stocked;
        var neverStocked = LowStockSets.NeverStocked(db, scope)
            .Select(p => new Row { ProductId = p.PublicId, Sku = p.Sku, ProductName = p.Name, WarehouseId = null, WarehouseCode = null, WarehouseName = null, QtyOnHand = 0m, SafetyStock = p.SafetyStock });

        return stocked.Concat(neverStocked);
    }

    /// <summary>
    /// Worst first = the largest shortfall as a share of the safety level (design: S22), then the largest shortfall in units, then SKU and warehouse code, so the order is
    /// total and a walk through the pages never repeats a row. A row without a safety level (low only because it is negative) has no share and counts as the worst.
    /// </summary>
    public static IOrderedQueryable<Row> Ordered(IQueryable<Row> rows) =>
        rows.OrderByDescending(r => r.SafetyStock > 0 ? (r.SafetyStock - r.QtyOnHand) / r.SafetyStock : NoSafetyLevelShare)
            .ThenByDescending(r => r.SafetyStock - r.QtyOnHand).ThenBy(r => r.Sku).ThenBy(r => r.WarehouseCode);

    /// <summary>The share a negative row without a safety level sorts by: far above any real shortfall share, so it comes first.</summary>
    private const decimal NoSafetyLevelShare = 1_000_000m;
}
