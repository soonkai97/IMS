using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

internal static class ProductSearchFilter
{
    /// <summary>SKU or name contains the text, or the product owns the barcode that equals it (resolved up front, see <see cref="ProductSearch"/>).</summary>
    public static void Apply(ISpecificationBuilder<Product> query, ProductSearch? search)
    {
        if (search is not { } s) return;
        if (s.BarcodeProductId is { } byBarcode) query.Where(p => p.Sku.Contains(s.Text) || p.Name.Contains(s.Text) || p.Id == byBarcode);
        else query.Where(p => p.Sku.Contains(s.Text) || p.Name.Contains(s.Text));
    }
}
