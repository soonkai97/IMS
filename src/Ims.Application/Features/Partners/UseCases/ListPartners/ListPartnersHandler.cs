using Ims.Domain;
namespace Ims.Application.Features.Partners;

public class ListPartnersHandler(IReadRepository<Partner> partners) : IQueryHandler<ListPartnersQuery, PagedResult<PartnerDto>>
{
    public async Task<PagedResult<PartnerDto>> HandleAsync(ListPartnersQuery q, CancellationToken ct)
    {
        var total = await partners.CountAsync(new PartnersCountSpec(q.Filter), ct);
        var items = await partners.ListAsync(new PartnersSpec(q.Filter), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<PartnerDto>(items, total, page, size);
    }
}
