using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class SalesOrderDtoSpec : Specification<SalesOrder, SalesOrderDto>
{
    public SalesOrderDtoSpec(Guid publicId, IReadOnlyCollection<int>? scope)
    {
        Query.AsNoTracking().Where(o => o.PublicId == publicId);
        if (scope is not null) Query.Where(o => scope.Contains(o.WarehouseId)); // outside the caller's warehouses the order does not exist for them: 404, not 403 (SEC-11)
        Query.Select(o => new SalesOrderDto(
            o.PublicId, o.No, o.Status, o.Warehouse.PublicId, o.Warehouse.Name, o.Customer.PublicId, o.Customer.Name, o.Remark,
            o.CreatedByUser.PublicId, o.CreatedByUser.Username, o.CreatedByUser.DisplayName, o.CreatedAt,
            o.Lines.Sum(l => l.BaseQty), o.Lines.Sum(l => l.SellingAmount),
            o.Lines.OrderBy(l => l.LineNo).Select(l => new SalesOrderLineDto(l.LineNo, l.Product.PublicId, l.Product.Sku, l.Product.Name, l.Qty, l.Uom.PublicId, l.Uom.Code,
                l.BaseQty, l.SellingPrice, l.SellingAmount, l.Remark)).ToList(),
            o.RowVersion, o.Id, o.CreatedBy));
    }
}
