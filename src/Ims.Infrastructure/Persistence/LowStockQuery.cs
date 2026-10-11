using Ims.Domain;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// The low-stock query, shared by the list, its total, the Home count and the export (see <c>ILowStockReader</c> for the definition).
/// Two branches that never overlap: balances that are low, and products that have no balance row in any warehouse. All parameters; no raw SQL.
/// </summary>
internal static class LowStockQuery
{
    /// <summary>Plain properties (not a constructor) so both branches have the same shape and EF can concatenate them.</summary>
    internal sealed class Row
    {
        public int ProductId { get; init; }
        public string Sku { get; init; } = "";
        public string ProductName { get; init; } = "";
        public int WarehouseId { get; init; }
        public string WarehouseCode { get; init; } = "";
        public string WarehouseName { get; init; } = "";
        public decimal QtyOnHand { get; init; }
        public decimal SafetyStock { get; init; }
    }

    public static IQueryable<Row> Build(AppDbContext db, IReadOnlyCollection<int>? scope, int? warehouseId)
    {
        var balances = db.Set<StockBalance>().AsQueryable();
        var warehouses = db.Set<Warehouse>().Where(w => w.IsActive);
        if (scope is not null)
        {
            balances = balances.Where(b => scope.Contains(b.WarehouseId));
            warehouses = warehouses.Where(w => scope.Contains(w.Id));
        }
        if (warehouseId is { } only)
        {
            balances = balances.Where(b => b.WarehouseId == only);
            warehouses = warehouses.Where(w => w.Id == only);
        }

        var stocked = balances
            .Where(LowStockRule.IsLowBalance)
            .Select(b => new Row
            {
                ProductId = b.ProductId, Sku = b.Product.Sku, ProductName = b.Product.Name, WarehouseId = b.WarehouseId,
                WarehouseCode = b.Warehouse.Code, WarehouseName = b.Warehouse.Name, QtyOnHand = b.QtyOnHand, SafetyStock = b.Product.SafetyStock
            });

        var neverStocked = db.Set<Product>()
            .Where(LowStockRule.CanBeNeverStockedLow)
            .Where(p => !db.Set<StockBalance>().Any(b => b.ProductId == p.Id))
            .SelectMany(_ => warehouses, (p, w) => new Row
            {
                ProductId = p.Id, Sku = p.Sku, ProductName = p.Name, WarehouseId = w.Id, WarehouseCode = w.Code, WarehouseName = w.Name,
                QtyOnHand = 0m, SafetyStock = p.SafetyStock
            });

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
