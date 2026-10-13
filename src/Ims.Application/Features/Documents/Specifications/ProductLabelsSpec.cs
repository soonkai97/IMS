using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class ProductLabelsSpec : Specification<Product, ProductLabel>
{
    public ProductLabelsSpec(IReadOnlyCollection<int> ids) =>
        Query.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new ProductLabel(p.Id, p.PublicId, p.Sku, p.BaseUom.Code));
}
