using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

public sealed class StockCountSpec : Specification<StockBalance>
{
    public StockCountSpec(StockFilter f, int? warehouseId, int? categoryId, IReadOnlyCollection<int>? scope, ProductSearch? search) => StockFilters.Apply(Query, f, warehouseId, categoryId, scope, search);
}
