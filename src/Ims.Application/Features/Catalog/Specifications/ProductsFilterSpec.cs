using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class ProductsFilterSpec : Specification<Product>
{
    public ProductsFilterSpec(ProductFilter q)
    {
        Query.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            Query.Where(p => p.Sku.Contains(s) || p.Name.Contains(s) || p.Barcodes.Any(b => b.Barcode == s));
        }
        if (q.CategoryId is { } c) Query.Where(p => p.CategoryId == c);
        if (q.Active is { } a) Query.Where(p => p.IsActive == a);
    }
}
