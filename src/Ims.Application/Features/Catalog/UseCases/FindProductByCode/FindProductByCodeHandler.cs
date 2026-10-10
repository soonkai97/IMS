using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>Lookup by barcode or SKU, used by the document line picker (and by scanning in Phase 2). An unknown code is a result, not an error.</summary>
public class FindProductByCodeHandler(IReadRepository<Product> products) : IQueryHandler<FindProductByCodeQuery, ProductLookupResult>
{
    public async Task<ProductLookupResult> HandleAsync(FindProductByCodeQuery q, CancellationToken ct) =>
        await products.FirstOrDefaultAsync(new ProductByCodeSpec(q.Code.Trim()), ct) is { } product
            ? new ProductLookupResult(true, product)
            : new ProductLookupResult(false, null);
}
