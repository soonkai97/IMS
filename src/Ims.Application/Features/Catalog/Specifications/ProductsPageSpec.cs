using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class ProductsPageSpec : Specification<Product, ProductDto>
{
    public ProductsPageSpec(ProductFilter q)
    {
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        Query.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            Query.Where(p => p.Sku.Contains(s) || p.Name.Contains(s) || p.Barcodes.Any(b => b.Barcode == s));
        }
        if (q.CategoryId is { } c) Query.Where(p => p.CategoryId == c);
        if (q.Active is { } a) Query.Where(p => p.IsActive == a);
        switch (q.SortBy, q.Desc)
        {
            case (ProductSort.Name, false): Query.OrderBy(p => p.Name).ThenBy(p => p.Id); break;
            case (ProductSort.Name, true): Query.OrderByDescending(p => p.Name).ThenBy(p => p.Id); break;
            case (_, true): Query.OrderByDescending(p => p.Sku); break;
            default: Query.OrderBy(p => p.Sku); break;
        }
        Query.Skip((page - 1) * size).Take(size).Select(ProductProjection.Expression);
    }
}
