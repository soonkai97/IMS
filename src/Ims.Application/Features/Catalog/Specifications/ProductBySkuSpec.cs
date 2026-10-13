using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>The live product with this SKU (unique among live rows; the comparison is case-insensitive like the column). One seek on the filtered unique index.</summary>
public sealed class ProductBySkuSpec : Specification<Product, ProductDto>
{
    public ProductBySkuSpec(string sku) => Query.AsNoTracking().Where(p => p.Sku == sku).Select(ProductProjection.Expression);
}
