using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>Lookup by barcode or SKU, used by the document line picker (and by scanning in Phase 2).</summary>
public class FindProductByCodeHandler(IReadRepository<Product> products) : IQueryHandler<FindProductByCodeQuery, ProductDto?>
{
    public Task<ProductDto?> HandleAsync(FindProductByCodeQuery q, CancellationToken ct) =>
        products.FirstOrDefaultAsync(new ProductByCodeSpec(q.Code.Trim()), ct);
}
