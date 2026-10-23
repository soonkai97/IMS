using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class SalesOrdersCountSpec : Specification<SalesOrder>
{
    public SalesOrdersCountSpec(SalesOrderFilter f, int? customerId, int? warehouseId, IReadOnlyCollection<int>? scope) => SalesOrderFilters.Apply(Query, f, customerId, warehouseId, scope);
}
