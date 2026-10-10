using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class UomsLookupSpec : Specification<Uom>
{
    public UomsLookupSpec() => Query.AsNoTracking().OrderBy(u => u.Code).Take(Limits.MaxLookupRows);
}
