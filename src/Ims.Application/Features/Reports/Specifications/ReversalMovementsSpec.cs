using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>The first <c>take</c> reversal rows in report order: one per line of each reversed document, dated by the reversal.</summary>
public sealed class ReversalMovementsSpec : Specification<StockDocumentLine, MovementEntry>
{
    public ReversalMovementsSpec(MovementFilter f, int? warehouseId, IReadOnlyCollection<int>? scope, int take)
    {
        MovementFilters.ApplyToReversals(Query, f, warehouseId, scope);
        MovementOrder.ApplyToReversals(Query);
        Query.Take(take).Select(MovementProjection.Reversal);
    }
}
