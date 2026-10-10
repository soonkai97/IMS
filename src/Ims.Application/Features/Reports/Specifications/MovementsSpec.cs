using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>The same rows as the list, newest first, capped for export (no paging).</summary>
public sealed class MovementsSpec : Specification<StockDocumentLine, MovementRow>
{
    public MovementsSpec(MovementFilter f, IReadOnlyCollection<int>? scope)
    {
        MovementFilters.Apply(Query, f, scope);
        MovementOrder.Apply(Query);
        Query.Take(Limits.MaxExportRows).Select(MovementProjection.Expression);
    }
}
