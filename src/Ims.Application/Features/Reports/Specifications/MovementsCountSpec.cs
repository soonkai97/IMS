using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

public sealed class MovementsCountSpec : Specification<StockDocumentLine>
{
    public MovementsCountSpec(MovementFilter f, IReadOnlyCollection<int>? scope) => MovementFilters.Apply(Query, f, scope);
}
