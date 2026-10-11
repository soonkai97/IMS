using Ims.Domain;
namespace Ims.Application.Features.Stock;

public class QueryLedgerHandler(IReadRepository<StockLedgerEntry> ledger, IWarehouseScope scope) : IQueryHandler<QueryLedgerQuery, PagedResult<LedgerRow>>
{
    public async Task<PagedResult<LedgerRow>> HandleAsync(QueryLedgerQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var total = q.Filter.IncludeTotal ? await ledger.CountAsync(new LedgerCountSpec(q.Filter, ids), ct) : Paging.TotalNotCounted;
        var items = await ledger.ListAsync(new LedgerRowsSpec(q.Filter, ids), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize, Limits.MaxStockPageSize);
        return new PagedResult<LedgerRow>(items, total, page, size, Paging.NextCursor(items, size, r => r.Id));
    }
}
