using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Import;

public sealed class ProductSkusByIdsSpec : Specification<Product, SkuRef>
{
    public ProductSkusByIdsSpec(IReadOnlyCollection<int> ids) =>
        Query.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new SkuRef(p.Id, p.PublicId, p.Sku, p.IsActive, p.BaseUomId));
}
