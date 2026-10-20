using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>
/// Lookup by barcode or SKU, used by the document line picker (and by scanning in Phase 2). An unknown code is a result, not an error.
/// The SKU is tried first (one seek on the unique SKU index), then the barcode (one seek on the unique barcode index, then the product by key).
/// One predicate over both columns ("Sku = code OR a barcode = code", TOP 1) made SQL Server walk the Product table until the first match, so the cost grew
/// with the position of the product (docs/dba/README.md 4d). A code that is one product's SKU and another's barcode belongs to the SKU's product.
/// </summary>
public class FindProductByCodeHandler(IReadRepository<Product> products, IReadRepository<ProductBarcode> barcodes, IModuleSwitches modules) : IQueryHandler<FindProductByCodeQuery, ProductLookupResult>
{
    public async Task<ProductLookupResult> HandleAsync(FindProductByCodeQuery q, CancellationToken ct)
    {
        var code = q.Code.Trim();
        var product = await products.FirstOrDefaultAsync(new ProductBySkuSpec(code), ct);
        if (product is null && await barcodes.FirstOrDefaultAsync(new ProductIdByBarcodeSpec(code), ct) is { } productId)
            product = await products.FirstOrDefaultAsync(new ProductDtoSpec(productId), ct);
        return product is null ? new ProductLookupResult(false, null) : new ProductLookupResult(true, await modules.ShapeAsync(product, ct));
    }
}
