using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Import;

/// <summary>Which existing product owns each of the barcodes found in the file.</summary>
public sealed class BarcodeOwnersSpec : Specification<ProductBarcode, BarcodeOwner>
{
    public BarcodeOwnersSpec(IReadOnlyCollection<string> codes) =>
        Query.AsNoTracking().Where(b => codes.Contains(b.Barcode)).Select(b => new BarcodeOwner(b.Barcode, b.ProductId));
}
