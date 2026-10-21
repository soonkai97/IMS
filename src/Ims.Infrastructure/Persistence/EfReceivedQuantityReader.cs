using Ims.Application.Features.Purchasing;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>One grouped sum over the lines of the Posted receipts linked to the order: a seek on the filtered index of <c>StockDocument.PurchaseOrderId</c>, then those few documents' lines. Soft-deleted rows are not counted (the global filter).</summary>
public sealed class EfReceivedQuantityReader(AppDbContext db) : IReceivedQuantityReader
{
    public async Task<IReadOnlyDictionary<int, ReceivedQuantity>> PostedByLineAsync(int orderKey, CancellationToken ct) =>
        await db.Set<StockDocumentLine>().AsNoTracking()
            .Where(l => l.Document.PurchaseOrderId == orderKey && l.Document.Status == DocumentStatus.Posted && l.PurchaseOrderLineNo != null)
            .GroupBy(l => l.PurchaseOrderLineNo!.Value)
            .Select(g => new { LineNo = g.Key, Qty = g.Sum(l => l.Qty), BaseQty = g.Sum(l => l.BaseQty) })
            .ToDictionaryAsync(x => x.LineNo, x => new ReceivedQuantity(x.Qty, x.BaseQty), ct);
}
