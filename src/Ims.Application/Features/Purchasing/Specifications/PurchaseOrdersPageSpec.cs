using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public sealed class PurchaseOrdersPageSpec : Specification<PurchaseOrder, PurchaseOrderListItem>
{
    public PurchaseOrdersPageSpec(PurchaseOrderFilter f, int? supplierId, int? warehouseId, IReadOnlyCollection<int>? scope, long? after)
    {
        PurchaseOrderFilters.Apply(Query, f, supplierId, warehouseId, scope);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize);
        Query.OrderByDescending(o => o.Id);
        if (after is { } position)
        {
            var olderThan = (int)Math.Min(position, int.MaxValue); // a position past the largest order id means "everything is older"
            Query.Where(o => o.Id < olderThan).Take(size);
        }
        else Query.Skip((page - 1) * size).Take(size);
        Query.Select(o => new PurchaseOrderListItem(o.PublicId, o.No, o.Status, o.Warehouse.Name, o.Supplier.Name, o.CreatedAt, o.CreatedByUser.Username,
            o.Lines.Count(), o.Lines.Sum(l => l.Amount), o.RowVersion, o.Id));
    }
}
