using Ims.Application.Features.Purchasing;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

public sealed class EfReorderReader(AppDbContext db) : IReorderReader
{
    public Task<int> CountAsync(IReadOnlyCollection<int>? warehouseScope, int warehouseId, string? search, CancellationToken ct) =>
        ReorderQuery.Build(db, warehouseScope, warehouseId, search).CountAsync(ct);

    public async Task<IReadOnlyList<ReorderSuggestion>> PageAsync(IReadOnlyCollection<int>? warehouseScope, int warehouseId, string? search, int page, int pageSize, CancellationToken ct)
    {
        var rows = await ReorderQuery.Ordered(ReorderQuery.Build(db, warehouseScope, warehouseId, search)).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return rows.Select(r =>
        {
            var suggested = ReorderRule.SuggestedQty(r.SafetyStock, r.QtyOnHand, r.OnOrderQty);
            return new ReorderSuggestion(r.ProductId, r.Sku, r.ProductName, r.UomCode, r.WarehouseId, r.WarehouseCode, r.WarehouseName,
                r.QtyOnHand, r.SafetyStock, r.OnOrderQty, suggested, r.AvgCost is { } cost ? Money.Round(suggested * cost) : null);
        }).ToList();
    }
}
