using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class ProductByCodeSpec : Specification<Product, ProductDto>
{
    public ProductByCodeSpec(string code) =>
        Query.AsNoTracking().Where(p => p.Sku == code || p.Barcodes.Any(b => b.Barcode == code)).Select(ProductProjection.Expression);
}
