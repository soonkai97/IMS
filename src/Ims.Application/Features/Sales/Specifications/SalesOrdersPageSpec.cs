using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class SalesOrdersPageSpec : Specification<SalesOrder, SalesOrderListItem>
{
    public SalesOrdersPageSpec(SalesOrderFilter f, int? customerId, int? warehouseId, IReadOnlyCollection<int>? scope, long? after)
    {
        SalesOrderFilters.Apply(Query, f, customerId, warehouseId, scope);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize);
        Query.OrderByDescending(o => o.Id);
        if (after is { } position)
        {
            var olderThan = (int)Math.Min(position, int.MaxValue); // a position past the largest order id means "everything is older"
            Query.Where(o => o.Id < olderThan).Take(size);
        }
        else Query.Skip((page - 1) * size).Take(size);
        Query.Select(o => new SalesOrderListItem(o.PublicId, o.No, o.Status, o.Warehouse.Name, o.Customer.Name, o.CreatedAt, o.CreatedByUser.Username,
            o.Lines.Count(), o.Lines.Sum(l => l.SellingAmount), o.RequiresApproval, o.IsLarge, o.RowVersion, o.CreatedBy, o.SubmittedBy, o.Id));
    }
}
