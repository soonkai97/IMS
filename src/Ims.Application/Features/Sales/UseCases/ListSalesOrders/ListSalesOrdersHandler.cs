using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class ListSalesOrdersHandler(IReadRepository<SalesOrder> orders, ISalesOrderAccess access, ICursorProtector cursors, IIdMap<Warehouse> warehouseIds, IIdMap<Partner> partnerIds, IDocumentVersion version)
    : IQueryHandler<ListSalesOrdersQuery, PagedResult<SalesOrderListItem>>
{
    public async Task<PagedResult<SalesOrderListItem>> HandleAsync(ListSalesOrdersQuery q, CancellationToken ct)
    {
        var after = CursorToken.Decode(cursors, CursorLists.SalesOrders, q.Filter.Cursor);
        var scope = access.WarehouseScope();
        var warehouseId = await warehouseIds.ToInternalOrNoRowAsync(q.Filter.WarehouseId, ct);
        var customerId = await partnerIds.ToInternalOrNoRowAsync(q.Filter.CustomerId, ct);
        var total = q.Filter.IncludeTotal ? await orders.CountAsync(new SalesOrdersCountSpec(q.Filter, customerId, warehouseId, scope), ct) : Paging.TotalNotCounted;
        var rows = await orders.ListAsync(new SalesOrdersPageSpec(q.Filter, customerId, warehouseId, scope, after), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);

        var items = rows.Select(r => r with { ConcurrencyToken = version.Token(r.Id, r.RowVersion) }).ToList();
        return new PagedResult<SalesOrderListItem>(items, total, page, size, Paging.NextCursor(items, size, o => o.Key, cursors, CursorLists.SalesOrders));
    }
}
