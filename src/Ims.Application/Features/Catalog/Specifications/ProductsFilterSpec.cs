using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class ProductsFilterSpec : Specification<Product>
{
    public ProductsFilterSpec(ProductFilter q, int? categoryId, ProductSearch? search)
    {
        Query.AsNoTracking();
        ProductSearchFilter.Apply(Query, search);
        if (categoryId is { } c) Query.Where(p => p.CategoryId == c);
        if (q.Active is { } a) Query.Where(p => p.IsActive == a);
    }
}
