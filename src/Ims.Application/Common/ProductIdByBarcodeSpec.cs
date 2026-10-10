using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application;

/// <summary>The product that owns a live barcode (barcodes are unique among live rows), or null when no product has it.</summary>
public sealed class ProductIdByBarcodeSpec : Specification<ProductBarcode, int?>
{
    public ProductIdByBarcodeSpec(string barcode) => Query.AsNoTracking().Where(b => b.Barcode == barcode).Select(b => (int?)b.ProductId);
}
