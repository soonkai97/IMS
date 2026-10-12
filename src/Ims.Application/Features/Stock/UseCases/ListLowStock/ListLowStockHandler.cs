using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <summary>STK-03: products at or below safety stock, stocked in no visible warehouse, or negative, paged. Computed live (see <see cref="ILowStockReader"/>) so it is never stale.</summary>
public class ListLowStockHandler(ILowStockReader lowStock, IWarehouseScope scope) : IQueryHandler<ListLowStockQuery, PagedResult<LowStockAlert>>
{
    public async Task<PagedResult<LowStockAlert>> HandleAsync(ListLowStockQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize, Limits.MaxStockPageSize);
        var total = await lowStock.CountAsync(ids, q.Filter.WarehouseId, ct);
        var items = await lowStock.PageAsync(ids, q.Filter.WarehouseId, page, size, ct);
        return new PagedResult<LowStockAlert>(items, total, page, size);
    }
}
