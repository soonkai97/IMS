using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

internal static class StockFilters
{
    /// <param name="search">The resolved <see cref="StockFilter.Search"/> (see <see cref="ProductSearch"/>); null = no text search.</param>
    public static void Apply(ISpecificationBuilder<StockBalance> query, StockFilter f, int? warehouseId, int? productId, int? categoryId, IReadOnlyCollection<int>? scope, ProductSearch? search)
    {
        query.AsNoTracking();
        if (scope is not null) query.Where(b => scope.Contains(b.WarehouseId));
        if (warehouseId is { } w) query.Where(b => b.WarehouseId == w);
        if (productId is { } p) query.Where(b => b.ProductId == p);
        if (categoryId is { } c) query.Where(b => b.Product.CategoryId == c);
        if (search is { } s)
        {
            if (s.BarcodeProductId is { } byBarcode) query.Where(b => b.Product.Sku.Contains(s.Text) || b.Product.Name.Contains(s.Text) || b.Product.Id == byBarcode);
            else query.Where(b => b.Product.Sku.Contains(s.Text) || b.Product.Name.Contains(s.Text));
        }
        // The list and the export with "low only" are served by ILowOnlyStockReader, which adds the never-stocked products (no balance row, so this table
        // cannot list them). This is the balance half of the same definition, so a caller that bypasses the reader still gets only low rows.
        if (f.LowOnly) query.Where(LowStockRule.IsLowBalance);
    }
}
