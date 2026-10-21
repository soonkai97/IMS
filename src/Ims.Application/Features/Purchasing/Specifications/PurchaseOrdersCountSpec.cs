using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public sealed class PurchaseOrdersCountSpec : Specification<PurchaseOrder>
{
    public PurchaseOrdersCountSpec(PurchaseOrderFilter f, int? supplierId, int? warehouseId, IReadOnlyCollection<int>? scope) => PurchaseOrderFilters.Apply(Query, f, supplierId, warehouseId, scope);
}
