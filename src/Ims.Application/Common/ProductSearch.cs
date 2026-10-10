using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// A product search text plus the product whose barcode equals it. The barcode is resolved by one index seek BEFORE the list query is built,
/// because "sku OR name OR EXISTS(barcode)" in a single predicate makes SQL Server probe the barcode table once per product
/// (1M page reads for a stock search at 50,000 products); a plain <c>ProductId = @id</c> arm costs nothing. The rows returned are identical.
/// </summary>
public sealed record ProductSearch(string Text, int? BarcodeProductId)
{
    /// <summary>Null when there is no search text.</summary>
    public static async Task<ProductSearch?> ResolveAsync(IReadRepository<ProductBarcode> barcodes, string? raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        return new ProductSearch(text, await barcodes.FirstOrDefaultAsync(new ProductIdByBarcodeSpec(text), ct));
    }
}
