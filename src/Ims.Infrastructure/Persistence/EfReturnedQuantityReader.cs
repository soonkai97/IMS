using Ims.Application.Features.Returns;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>One grouped sum over the lines of the Posted returns of a document: a seek on the filtered index of <c>StockDocument.ReturnOfDocumentId</c>, then those few documents' lines. Soft-deleted rows are not counted (the global filter). A Reversed return counts as nothing.</summary>
public sealed class EfReturnedQuantityReader(AppDbContext db) : IReturnedQuantityReader
{
    public async Task<IReadOnlyDictionary<int, ReturnedQuantity>> PostedByLineAsync(int originalKey, CancellationToken ct) =>
        await db.Set<StockDocumentLine>().AsNoTracking()
            .Where(l => l.Document.ReturnOfDocumentId == originalKey && l.Document.Status == DocumentStatus.Posted && l.ReturnOfLineNo != null)
            .GroupBy(l => l.ReturnOfLineNo!.Value)
            .Select(g => new { LineNo = g.Key, Qty = g.Sum(l => l.Qty), BaseQty = g.Sum(l => l.BaseQty) })
            .ToDictionaryAsync(x => x.LineNo, x => new ReturnedQuantity(x.Qty, x.BaseQty), ct);
}
