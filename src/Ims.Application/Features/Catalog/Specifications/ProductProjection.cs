using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

internal static class ProductProjection
{
    public static readonly System.Linq.Expressions.Expression<Func<Product, ProductDto>> Expression = p => new ProductDto(
        p.Id, p.Sku, p.Name, p.CategoryId, p.Category != null ? p.Category.Name : null, p.BaseUomId, p.BaseUom.Code,
        p.SafetyStock, p.IsActive, p.Barcodes.Select(b => b.Barcode).ToList(),
        p.Conversions.Select(c => new UomConversionDto(c.FromUomId, c.ToUomId, c.Factor)).ToList());
}
