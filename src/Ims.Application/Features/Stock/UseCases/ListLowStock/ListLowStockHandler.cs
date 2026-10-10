using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <summary>STK-03: products below safety stock, paged. Computed from balances so it is never stale.</summary>
public class ListLowStockHandler(IReadRepository<StockBalance> balances, IWarehouseScope scope) : IQueryHandler<ListLowStockQuery, PagedResult<LowStockAlert>>
{
    public async Task<PagedResult<LowStockAlert>> HandleAsync(ListLowStockQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var countFilter = new StockFilter(q.Filter.WarehouseId, null, null, null, LowOnly: true);
        var total = await balances.CountAsync(new StockCountSpec(countFilter, ids, search: null), ct);
        var items = await balances.ListAsync(new LowStockPageSpec(q.Filter, ids), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize, Limits.MaxStockPageSize);
        return new PagedResult<LowStockAlert>(items, total, page, size);
    }
}
