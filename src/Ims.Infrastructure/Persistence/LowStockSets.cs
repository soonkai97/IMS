using Ims.Domain;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// The two sets that make up "low stock" (see <see cref="LowStockRule"/>), built in ONE place for every reader: the low-stock list, its total, the Home count and
/// the export (<see cref="LowStockQuery"/>), and the stock list with "low only" (<see cref="EfLowOnlyStockReader"/>). A reader adds its own filters and shapes the rows;
/// it never restates the rule. The two sets never overlap: a product either has a balance row in a warehouse the caller can see, or it has none.
/// </summary>
internal static class LowStockSets
{
    /// <summary>Balance rows that are low, inside the caller's scope and the optional single warehouse.</summary>
    public static IQueryable<StockBalance> LowBalances(AppDbContext db, IReadOnlyCollection<int>? scope, int? warehouseId)
    {
        var balances = db.Set<StockBalance>().Where(LowStockRule.IsLowBalance);
        if (scope is not null) balances = balances.Where(b => scope.Contains(b.WarehouseId));
        if (warehouseId is { } only) balances = balances.Where(b => b.WarehouseId == only);
        return balances;
    }

    /// <summary>
    /// Active products with a safety level that have no balance row in ANY active warehouse the caller can see: never stocked, listed ONCE each (no warehouse,
    /// quantity zero), not once per warehouse. Only the caller's warehouses are looked at, so a scoped caller learns nothing about stock elsewhere and the answer does
    /// not change when a warehouse they cannot see receives the product. The test is an anti-join on the balance table, which has the product as the first key column.
    /// </summary>
    public static IQueryable<Product> NeverStocked(AppDbContext db, IReadOnlyCollection<int>? scope)
    {
        var visible = db.Set<StockBalance>().Where(b => b.Warehouse.IsActive);
        if (scope is not null) visible = visible.Where(b => scope.Contains(b.WarehouseId));
        return db.Set<Product>().Where(LowStockRule.CanBeNeverStockedLow).Where(p => !visible.Any(b => b.ProductId == p.Id));
    }
}
