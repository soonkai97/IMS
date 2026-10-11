using Ims.Domain;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// The two sets that make up "low stock" (see <see cref="LowStockRule"/>), built in ONE place for every reader: the low-stock list, its total, the Home count and
/// the export (<see cref="LowStockQuery"/>), and the stock list with "low only" (<see cref="EfLowOnlyStockReader"/>). A reader adds its own filters and shapes the rows;
/// it never restates the rule. The two sets never overlap: a balance row exists, or it does not.
/// </summary>
internal static class LowStockSets
{
    /// <summary>A product that has never been stocked in a warehouse (no balance row for the pair), where it is therefore low with nothing on hand.</summary>
    internal sealed class NeverStockedPair
    {
        public Product Product { get; init; } = null!;
        public Warehouse Warehouse { get; init; } = null!;
    }

    /// <summary>Balance rows that are low, inside the caller's scope and the optional single warehouse.</summary>
    public static IQueryable<StockBalance> LowBalances(AppDbContext db, IReadOnlyCollection<int>? scope, int? warehouseId)
    {
        var balances = db.Set<StockBalance>().Where(LowStockRule.IsLowBalance);
        if (scope is not null) balances = balances.Where(b => scope.Contains(b.WarehouseId));
        if (warehouseId is { } only) balances = balances.Where(b => b.WarehouseId == only);
        return balances;
    }

    /// <summary>
    /// (product, warehouse) pairs where an active product with a safety level has no balance row in an ACTIVE warehouse of the caller's scope. Only the pair
    /// itself is looked at: stock in another warehouse (or a row in an inactive one) does not make the product "stocked here", and a scoped caller's answer never
    /// depends on a warehouse they cannot see. The test is an anti-join on the balance primary key (<c>ProductId, WarehouseId</c>): one seek per pair.
    /// </summary>
    public static IQueryable<NeverStockedPair> NeverStocked(AppDbContext db, IReadOnlyCollection<int>? scope, int? warehouseId)
    {
        var warehouses = db.Set<Warehouse>().Where(w => w.IsActive);
        if (scope is not null) warehouses = warehouses.Where(w => scope.Contains(w.Id));
        if (warehouseId is { } only) warehouses = warehouses.Where(w => w.Id == only);

        return db.Set<Product>().Where(LowStockRule.CanBeNeverStockedLow)
            .SelectMany(_ => warehouses, (p, w) => new NeverStockedPair { Product = p, Warehouse = w })
            .Where(pair => !db.Set<StockBalance>().Any(b => b.ProductId == pair.Product.Id && b.WarehouseId == pair.Warehouse.Id));
    }
}
