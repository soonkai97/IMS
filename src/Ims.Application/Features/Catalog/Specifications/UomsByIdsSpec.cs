using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class UomsByIdsSpec : Specification<Uom>
{
    public UomsByIdsSpec(IReadOnlyCollection<int> ids) => Query.AsNoTracking().Where(u => ids.Contains(u.Id));
}
