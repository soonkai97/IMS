using Ims.Domain;
namespace Ims.Application.Features.Reports;

public class ListMovementsHandler(IMovementReader movements, IWarehouseScope scope, IIdMap<Warehouse> warehouseIds) : IQueryHandler<ListMovementsQuery, PagedResult<MovementRow>>
{
    public async Task<PagedResult<MovementRow>> HandleAsync(ListMovementsQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var warehouseId = await warehouseIds.ToInternalOrNoRowAsync(q.Filter.WarehouseId, ct);
        var total = await movements.CountAsync(q.Filter, warehouseId, ids, ct);
        var items = await movements.PageAsync(q.Filter, warehouseId, ids, ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<MovementRow>(items, total, page, size);
    }
}
