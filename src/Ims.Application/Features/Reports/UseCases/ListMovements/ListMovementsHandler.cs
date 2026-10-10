using Ims.Domain;
namespace Ims.Application.Features.Reports;

public class ListMovementsHandler(IReadRepository<StockDocumentLine> lines, IWarehouseScope scope) : IQueryHandler<ListMovementsQuery, PagedResult<MovementRow>>
{
    public async Task<PagedResult<MovementRow>> HandleAsync(ListMovementsQuery q, CancellationToken ct)
    {
        var ids = scope.Ids();
        var total = await lines.CountAsync(new MovementsCountSpec(q.Filter, ids), ct);
        var items = await lines.ListAsync(new MovementsPageSpec(q.Filter, ids), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<MovementRow>(items, total, page, size);
    }
}
