using Ims.Domain;
namespace Ims.Application.Features.Stock;

public class QueryStockHandler(IReadRepository<StockBalance> balances, IWarehouseScope scope) : IQueryHandler<QueryStockQuery, PagedResult<StockRow>>
{
    public async Task<PagedResult<StockRow>> HandleAsync(QueryStockQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var total = await balances.CountAsync(new StockCountSpec(q.Filter, ids), ct);
        var items = await balances.ListAsync(new StockRowsSpec(q.Filter, ids, paged: true), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize, Limits.MaxStockPageSize);
        return new PagedResult<StockRow>(items, total, page, size);
    }
}
