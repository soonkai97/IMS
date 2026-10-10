using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

public sealed class ReversalMovementsCountSpec : Specification<StockDocumentLine>
{
    public ReversalMovementsCountSpec(MovementFilter f, IReadOnlyCollection<int>? scope) => MovementFilters.ApplyToReversals(Query, f, scope);
}
