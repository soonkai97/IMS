using Ims.Domain;
namespace Ims.Application.Features.Stock;

public class QueryStockHandler(IReadRepository<StockBalance> balances, IReadRepository<ProductBarcode> barcodes, IWarehouseScope scope)
    : IQueryHandler<QueryStockQuery, PagedResult<StockRow>>
{
    public async Task<PagedResult<StockRow>> HandleAsync(QueryStockQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var search = await ProductSearch.ResolveAsync(barcodes, q.Filter.Search, ct);
        var total = await balances.CountAsync(new StockCountSpec(q.Filter, ids, search), ct);
        var items = await balances.ListAsync(new StockRowsSpec(q.Filter, ids, search, paged: true), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize, Limits.MaxStockPageSize);
        return new PagedResult<StockRow>(items, total, page, size);
    }
}
