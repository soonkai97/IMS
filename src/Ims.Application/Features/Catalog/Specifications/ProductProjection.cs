using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

internal static class ProductProjection
{
    /// <summary>The product, its unit and its category leave as public ids: the navigation is joined, the integer keys stay inside.</summary>
    public static readonly System.Linq.Expressions.Expression<Func<Product, ProductDto>> Expression = p => new ProductDto(
        p.PublicId, p.Sku, p.Name, p.Category != null ? p.Category.PublicId : (Guid?)null, p.Category != null ? p.Category.Name : null, p.BaseUom.PublicId, p.BaseUom.Code,
        p.SafetyStock, p.IsActive, p.Barcodes.Select(b => b.Barcode).ToList(),
        p.Conversions.Select(c => new UomConversionDto(c.FromUom.PublicId, c.ToUom.PublicId, c.Factor)).ToList());
}
