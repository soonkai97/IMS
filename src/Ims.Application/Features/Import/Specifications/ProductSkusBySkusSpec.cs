using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Import;

public sealed class ProductSkusBySkusSpec : Specification<Product, SkuRef>
{
    public ProductSkusBySkusSpec(IReadOnlyCollection<string> skus) =>
        Query.AsNoTracking().Where(p => skus.Contains(p.Sku)).Select(p => new SkuRef(p.Id, p.Sku, p.IsActive, p.BaseUomId));
}
