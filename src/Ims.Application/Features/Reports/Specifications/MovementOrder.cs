using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>
/// The one order of the receipt/issue detail, shared by the page and the export: newest posting first, documents posted at the same instant
/// newest id first, then the lines of a document in entry order. The last two keys are unique together, so rows never swap between requests,
/// and a backward scan of an index on <c>PostedAt</c> yields the first two keys, so the first rows stream instead of the whole report being sorted.
/// </summary>
internal static class MovementOrder
{
    public static void Apply(ISpecificationBuilder<StockDocumentLine> query) =>
        query.OrderByDescending(l => l.Document.PostedAt).ThenByDescending(l => l.DocumentId).ThenBy(l => l.Id);
}
