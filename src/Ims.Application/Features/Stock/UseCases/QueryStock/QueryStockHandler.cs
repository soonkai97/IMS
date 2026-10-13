using Ims.Domain;
namespace Ims.Application.Features.Stock;

public class QueryStockHandler(IReadRepository<StockBalance> balances, IReadRepository<ProductBarcode> barcodes, ILowOnlyStockReader lowOnly, IWarehouseScope scope,
    IIdMap<Warehouse> warehouseIds, IIdMap<Product> productIds, IIdMap<Category> categoryIds) : IQueryHandler<QueryStockQuery, PagedResult<StockRow>>
{
    public async Task<PagedResult<StockRow>> HandleAsync(QueryStockQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var search = await ProductSearch.ResolveAsync(barcodes, q.Filter.Search, ct);
        var warehouseId = await warehouseIds.ToInternalOrNoRowAsync(q.Filter.WarehouseId, ct);
        var productId = await productIds.ToInternalOrNoRowAsync(q.Filter.ProductId, ct);
        var categoryId = await categoryIds.ToInternalOrNoRowAsync(q.Filter.CategoryId, ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize, Limits.MaxStockPageSize);
        if (q.Filter.LowOnly) // the low-stock definition, never-stocked products included (PR-04): the same set as the low-stock list
            return new PagedResult<StockRow>(await lowOnly.ListAsync(q.Filter, warehouseId, productId, categoryId, ids, search, paged: true, ct),
                await lowOnly.CountAsync(q.Filter, warehouseId, productId, categoryId, ids, search, ct), page, size);

        var total = await balances.CountAsync(new StockCountSpec(q.Filter, warehouseId, productId, categoryId, ids, search), ct);
        var items = await balances.ListAsync(new StockRowsSpec(q.Filter, warehouseId, productId, categoryId, ids, search, paged: true), ct);
        return new PagedResult<StockRow>(items, total, page, size);
    }
}
