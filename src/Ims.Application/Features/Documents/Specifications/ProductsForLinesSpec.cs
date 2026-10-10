using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class ProductsForLinesSpec : Specification<Product>
{
    public ProductsForLinesSpec(IReadOnlyCollection<int> ids) =>
        Query.Where(p => ids.Contains(p.Id)).Include(p => p.Conversions).AsNoTracking();
}
