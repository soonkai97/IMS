namespace Ims.Application.Features.Catalog;

/// <summary>
/// Answer to a barcode or SKU lookup. A code nobody owns is a normal outcome of scanning, not an error:
/// <c>Found</c> is false and <c>Product</c> is null (HTTP 200), so a scan miss does not look like a failed request.
/// </summary>
public record ProductLookupResult(bool Found, ProductDto? Product);
