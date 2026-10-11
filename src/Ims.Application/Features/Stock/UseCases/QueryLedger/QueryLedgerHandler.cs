using Ims.Domain;
namespace Ims.Application.Features.Stock;

public class QueryLedgerHandler(IReadRepository<StockLedgerEntry> ledger, ILedgerBalanceReader balanceAfter, IWarehouseScope scope) : IQueryHandler<QueryLedgerQuery, PagedResult<LedgerRow>>
{
    public async Task<PagedResult<LedgerRow>> HandleAsync(QueryLedgerQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var total = q.Filter.IncludeTotal ? await ledger.CountAsync(new LedgerCountSpec(q.Filter, ids), ct) : Paging.TotalNotCounted;
        var items = await ledger.ListAsync(new LedgerRowsSpec(q.Filter, ids), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize, Limits.MaxStockPageSize);
        var cursor = Paging.NextCursor(items, size, r => r.Id);
        // PS-14: the product ledger shows what it held after each movement (only when one product is asked for).
        if (q.Filter.ProductId is { } productId && items.Count > 0)
        {
            var after = await balanceAfter.BalanceAfterAsync(productId, ids, q.Filter.WarehouseId, items.Select(r => r.Id).ToList(), ct);
            items = items.Select(r => after.TryGetValue(r.Id, out var balance) ? r with { BalanceAfter = balance } : r).ToList();
        }
        return new PagedResult<LedgerRow>(items, total, page, size, cursor);
    }
}
