namespace Ims.Application.Features.Reports;

public class ListMovementsHandler(IMovementReader movements, IWarehouseScope scope) : IQueryHandler<ListMovementsQuery, PagedResult<MovementRow>>
{
    public async Task<PagedResult<MovementRow>> HandleAsync(ListMovementsQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var total = await movements.CountAsync(q.Filter, ids, ct);
        var items = await movements.PageAsync(q.Filter, ids, ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<MovementRow>(items, total, page, size);
    }
}
