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

        // One aggregate row computed by SQL Server.
        var row = await balances.GroupBy(_ => 1).Select(g => new
        {
            Value = g.Sum(b => b.QtyOnHand * b.AvgCost),
            Skus = g.Select(b => b.ProductId).Distinct().Count(),
            Low = g.Count(b => b.Product.SafetyStock > 0 && b.QtyOnHand < b.Product.SafetyStock)
        }).FirstOrDefaultAsync(ct);
        return row is null ? (0m, 0, 0) : (row.Value, row.Skus, row.Low);
    }
}
