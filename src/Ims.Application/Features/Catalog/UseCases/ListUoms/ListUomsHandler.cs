using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class ListUomsHandler(IReadRepository<Uom> uoms) : IQueryHandler<ListUomsQuery, IReadOnlyList<UomDto>>
{
    public async Task<IReadOnlyList<UomDto>> HandleAsync(ListUomsQuery q, CancellationToken ct) =>
        (await uoms.ListAsync(new UomsLookupSpec(), ct)).Select(u => new UomDto(u.Id, u.Code, u.Name)).ToList();
}
