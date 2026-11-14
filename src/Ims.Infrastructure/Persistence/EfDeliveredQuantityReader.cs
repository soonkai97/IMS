using Ims.Application;
using Ims.Application.Features.Sales;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// One grouped sum over the lines of the Posted deliveries linked to the order: a seek on the filtered index of <c>StockDocument.SalesOrderId</c>, then those few documents' lines. Soft-deleted rows are not counted (the global filter).
/// Tagged <see cref="QueryHint.NoSniffing"/>: one plan from the average statistics, not from the first key that arrives. Compiled for a key newer than the statistics of the sparse filtered index
/// (a new order, a freshly returned document), the optimizer starts from a scan of every document line instead of the seek: about 25,000 to 30,000 page reads and 0.3 s a call at 1,000,000 documents
/// (docs/dba/README.md section 20). <c>OPTION (RECOMPILE)</c> does not help, the estimate for the actual key is the wrong one.
/// </summary>
public sealed class EfDeliveredQuantityReader(AppDbContext db) : IDeliveredQuantityReader
{
    public async Task<IReadOnlyDictionary<int, DeliveredQuantity>> PostedByLineAsync(int orderKey, CancellationToken ct) =>
        await db.Set<StockDocumentLine>().AsNoTracking().TagWith(QueryHint.NoSniffing)
            .Where(l => l.Document.SalesOrderId == orderKey && l.Document.Status == DocumentStatus.Posted && l.SalesOrderLineNo != null)
            .GroupBy(l => l.SalesOrderLineNo!.Value)
            .Select(g => new { LineNo = g.Key, Qty = g.Sum(l => l.Qty), BaseQty = g.Sum(l => l.BaseQty) })
            .ToDictionaryAsync(x => x.LineNo, x => new DeliveredQuantity(x.Qty, x.BaseQty), ct);
}
