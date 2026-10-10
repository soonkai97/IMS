using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>
/// The one order of the receipt/issue detail, shared by the page and the export: newest movement first, movements at the same instant newest
/// document id first, then the lines of a document in entry order, and a reversal before the posting it undoes. The keys are unique together, so rows
/// never swap between requests.
/// <para>
/// The posting rows keep the order they always had (<c>PostedAt</c>, document id, line id), so a backward scan of an index on <c>PostedAt</c> yields
/// the first rows instead of the whole report being sorted. Each branch is cut to the rows a page needs before the two are merged
/// (<see cref="Merged"/>), so the reversal rows, which are few, never make the posting rows be sorted in full.
/// </para>
/// </summary>
public static class MovementOrder
{
    public static void ApplyToPostings(ISpecificationBuilder<StockDocumentLine> query) =>
        query.OrderByDescending(l => l.Document.PostedAt).ThenByDescending(l => l.DocumentId).ThenBy(l => l.Id);

    public static void ApplyToReversals(ISpecificationBuilder<StockDocumentLine> query) =>
        query.OrderByDescending(l => l.Document.ReversedAt).ThenByDescending(l => l.DocumentId).ThenBy(l => l.Id);

    /// <summary>The order of the merged rows; the same keys as the two branches, plus the kind of row as the last tie-break.</summary>
    public static IOrderedQueryable<MovementEntry> Merged(IQueryable<MovementEntry> rows) =>
        rows.OrderByDescending(r => r.At).ThenByDescending(r => r.DocumentId).ThenBy(r => r.LineId).ThenByDescending(r => r.IsReversal);
}
