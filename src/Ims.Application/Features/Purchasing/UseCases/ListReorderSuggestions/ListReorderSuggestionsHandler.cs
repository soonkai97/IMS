using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>
/// AI-02 in its simplest form (pilot slice PL2.6): what to order for one warehouse. Read-only; turning rows into an order is the existing create-order endpoint. A warehouse that does not exist or that the caller
/// may not read is a 404, like an order there.
/// </summary>
public class ListReorderSuggestionsHandler(IReorderReader reorder, IPurchaseOrderAccess access, IIdMap<Warehouse> warehouseIds) : IQueryHandler<ListReorderSuggestionsQuery, PagedResult<ReorderSuggestion>>
{
    public async Task<PagedResult<ReorderSuggestion>> HandleAsync(ListReorderSuggestionsQuery q, CancellationToken ct)
    {
        var scope = access.WarehouseScope();
        var warehouseId = await warehouseIds.ToInternalOrNoRowAsync(q.Filter.WarehouseId, ct) ?? IdMapExtensions.NoRow;
        if (warehouseId == IdMapExtensions.NoRow || (scope is not null && !scope.Contains(warehouseId))) throw new NotFoundException(Messages.WarehouseNotFound);

        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize, Limits.MaxStockPageSize);
        var total = await reorder.CountAsync(scope, warehouseId, q.Filter.Search, ct);
        var items = await reorder.PageAsync(scope, warehouseId, q.Filter.Search, page, size, ct);
        return new PagedResult<ReorderSuggestion>(items, total, page, size);
    }
}
