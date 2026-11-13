using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class ListPriceListsHandler(IReadRepository<PriceList> lists, IDocumentVersion version) : IQueryHandler<ListPriceListsQuery, PagedResult<PriceListListItem>>
{
    public async Task<PagedResult<PriceListListItem>> HandleAsync(ListPriceListsQuery q, CancellationToken ct)
    {
        var total = await lists.CountAsync(new PriceListsCountSpec(q.Filter), ct);
        var rows = await lists.ListAsync(new PriceListsPageSpec(q.Filter), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<PriceListListItem>(rows.Select(r => r with { ConcurrencyToken = version.Token(r.Id, r.RowVersion) }).ToList(), total, page, size);
    }
}
