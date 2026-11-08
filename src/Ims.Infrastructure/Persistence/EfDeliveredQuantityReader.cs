using Ims.Application.Features.Sales;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>One grouped sum over the lines of the Posted deliveries linked to the order: a seek on the filtered index of <c>StockDocument.SalesOrderId</c>, then those few documents' lines. Soft-deleted rows are not counted (the global filter).</summary>
public sealed class EfDeliveredQuantityReader(AppDbContext db) : IDeliveredQuantityReader
{
    public async Task<IReadOnlyDictionary<int, DeliveredQuantity>> PostedByLineAsync(int orderKey, CancellationToken ct) =>
        await db.Set<StockDocumentLine>().AsNoTracking()
            .Where(l => l.Document.SalesOrderId == orderKey && l.Document.Status == DocumentStatus.Posted && l.SalesOrderLineNo != null)
            .GroupBy(l => l.SalesOrderLineNo!.Value)
            .Select(g => new { LineNo = g.Key, Qty = g.Sum(l => l.Qty), BaseQty = g.Sum(l => l.BaseQty) })
            .ToDictionaryAsync(x => x.LineNo, x => new DeliveredQuantity(x.Qty, x.BaseQty), ct);
}
