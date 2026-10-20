using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class ListPurchaseOrdersHandler(IReadRepository<PurchaseOrder> orders, IPurchaseOrderAccess access, ICursorProtector cursors, IIdMap<Warehouse> warehouseIds, IIdMap<Partner> partnerIds, IDocumentVersion version)
    : IQueryHandler<ListPurchaseOrdersQuery, PagedResult<PurchaseOrderListItem>>
{
    public async Task<PagedResult<PurchaseOrderListItem>> HandleAsync(ListPurchaseOrdersQuery q, CancellationToken ct)
    {
        var after = CursorToken.Decode(cursors, CursorLists.PurchaseOrders, q.Filter.Cursor);
        var scope = access.WarehouseScope();
        var warehouseId = await warehouseIds.ToInternalOrNoRowAsync(q.Filter.WarehouseId, ct);
        var supplierId = await partnerIds.ToInternalOrNoRowAsync(q.Filter.SupplierId, ct);
        var total = q.Filter.IncludeTotal ? await orders.CountAsync(new PurchaseOrdersCountSpec(q.Filter, supplierId, warehouseId, scope), ct) : Paging.TotalNotCounted;
        var rows = await orders.ListAsync(new PurchaseOrdersPageSpec(q.Filter, supplierId, warehouseId, scope, after), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);

        var items = rows.Select(r => r with { ConcurrencyToken = version.Token(r.Id, r.RowVersion) }).ToList();
        return new PagedResult<PurchaseOrderListItem>(items, total, page, size, Paging.NextCursor(items, size, o => o.Key, cursors, CursorLists.PurchaseOrders));
    }
}
