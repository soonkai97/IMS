using Ims.Application.Features.Stock;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

public sealed class EfLowStockReader(AppDbContext db) : ILowStockReader
{
    public Task<int> CountAsync(IReadOnlyCollection<int>? warehouseScope, int? warehouseId, CancellationToken ct) =>
        LowStockQuery.Build(db, warehouseScope, warehouseId).CountAsync(ct);

    public async Task<IReadOnlyList<LowStockAlert>> PageAsync(IReadOnlyCollection<int>? warehouseScope, int? warehouseId, int page, int pageSize, CancellationToken ct) =>
        ToAlerts(await LowStockQuery.Ordered(LowStockQuery.Build(db, warehouseScope, warehouseId)).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct));

    public async Task<IReadOnlyList<LowStockAlert>> ListAsync(IReadOnlyCollection<int>? warehouseScope, int? warehouseId, int limit, CancellationToken ct) =>
        ToAlerts(await LowStockQuery.Ordered(LowStockQuery.Build(db, warehouseScope, warehouseId)).Take(limit).ToListAsync(ct));

    private static List<LowStockAlert> ToAlerts(List<LowStockQuery.Row> rows) => rows
        .Select(r => new LowStockAlert(r.ProductId, r.Sku, r.ProductName, r.WarehouseId, r.WarehouseCode, r.WarehouseName,
            r.QtyOnHand, r.SafetyStock, r.SafetyStock - r.QtyOnHand, r.QtyOnHand < 0))
        .ToList();
}
