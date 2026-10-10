using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

public sealed class StockCountSpec : Specification<StockBalance>
{
    public StockCountSpec(StockFilter f, IReadOnlyCollection<int>? scope, ProductSearch? search) => StockFilters.Apply(Query, f, scope, search);
}
