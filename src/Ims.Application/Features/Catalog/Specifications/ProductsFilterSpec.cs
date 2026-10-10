using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class ProductsFilterSpec : Specification<Product>
{
    public ProductsFilterSpec(ProductFilter q, ProductSearch? search)
    {
        Query.AsNoTracking();
        ProductSearchFilter.Apply(Query, search);
        if (q.CategoryId is { } c) Query.Where(p => p.CategoryId == c);
        if (q.Active is { } a) Query.Where(p => p.IsActive == a);
    }
}
