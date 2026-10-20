using Ardalis.Specification;
using Ims.Application.Features.Reports;
using Ims.Domain;
namespace Ims.Application.Features.Accounting;

/// <summary>
/// The first <c>take</c> posting rows of the period, oldest first. The period, the status rule and the scope are the movements report's own (<c>MovementFilters</c>), so the date
/// predicate stays on the PostedAt index; the order is that index's order, so the rows are read as the index yields them instead of being sorted.
/// </summary>
public sealed class AccountingPostingsSpec : Specification<StockDocumentLine, AccountingEntry>
{
    public AccountingPostingsSpec(DateOnly from, DateOnly to, int? warehouseId, IReadOnlyCollection<int>? scope, int take)
    {
        MovementFilters.Apply(Query, new MovementFilter(null, null, null, from, to), warehouseId, productId: null, scope);
        Query.OrderBy(l => l.Document.PostedAt).ThenBy(l => l.DocumentId).ThenBy(l => l.Id);
        Query.Take(take).Select(AccountingProjection.Posting);
    }
}
