using Ardalis.Specification;
using Ims.Application.Features.Reports;
using Ims.Domain;
namespace Ims.Application.Features.Accounting;

/// <summary>The first <c>take</c> reversal rows of the period, oldest first: one per line of each document reversed in the period, dated by the reversal.</summary>
public sealed class AccountingReversalsSpec : Specification<StockDocumentLine, AccountingEntry>
{
    public AccountingReversalsSpec(DateOnly from, DateOnly to, int? warehouseId, IReadOnlyCollection<int>? scope, int take)
    {
        MovementFilters.ApplyToReversals(Query, new MovementFilter(null, null, null, from, to), warehouseId, productId: null, scope);
        Query.OrderBy(l => l.Document.ReversedAt).ThenBy(l => l.DocumentId).ThenBy(l => l.Id);
        Query.Take(take).Select(AccountingProjection.Reversal);
    }
}
