using Ims.Application;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

public sealed class EfStockSummaryReader(AppDbContext db) : IStockSummaryReader
{
    public async Task<(decimal TotalValue, int SkuCount, int LowCount)> GetAsync(IReadOnlyCollection<int>? warehouseScope, CancellationToken ct)
    {
        var balances = db.Set<StockBalance>().AsNoTracking();
        if (warehouseScope is not null) balances = balances.Where(b => warehouseScope.Contains(b.WarehouseId));

        // One aggregate row computed by SQL Server. Value is net (a negative balance lowers it); a product counts as "with stock" only when it has a
        // positive quantity somewhere in the caller's warehouses.
        var row = await balances.GroupBy(_ => 1).Select(g => new
        {
            Value = g.Sum(b => Math.Round(b.QtyOnHand * b.AvgCost, Money.Decimals)),
            Skus = g.Where(b => b.QtyOnHand > 0).Select(b => b.ProductId).Distinct().Count()
        }).FirstOrDefaultAsync(ct);

        // Low products come from the shared low-stock query, so the Home count equals the low-stock list total (it includes never-stocked products).
        var low = await LowStockQuery.Build(db, warehouseScope, warehouseId: null).CountAsync(ct);
        return row is null ? (0m, 0, low) : (row.Value, row.Skus, low);
    }
}
