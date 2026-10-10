using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

internal static class StockFilters
{
    /// <param name="search">The resolved <see cref="StockFilter.Search"/> (see <see cref="ProductSearch"/>); null = no text search.</param>
    public static void Apply(ISpecificationBuilder<StockBalance> query, StockFilter f, IReadOnlyCollection<int>? scope, ProductSearch? search)
    {
        query.AsNoTracking();
        if (scope is not null) query.Where(b => scope.Contains(b.WarehouseId));
        if (f.WarehouseId is { } w) query.Where(b => b.WarehouseId == w);
        if (f.ProductId is { } p) query.Where(b => b.ProductId == p);
        if (f.CategoryId is { } c) query.Where(b => b.Product.CategoryId == c);
        if (search is { } s)
        {
            if (s.BarcodeProductId is { } byBarcode) query.Where(b => b.Product.Sku.Contains(s.Text) || b.Product.Name.Contains(s.Text) || b.Product.Id == byBarcode);
            else query.Where(b => b.Product.Sku.Contains(s.Text) || b.Product.Name.Contains(s.Text));
        }
        // Same condition as the low-stock list (without the never-stocked products, who have no balance row to list here).
        if (f.LowOnly) query.Where(b => b.Product.IsActive && (b.QtyOnHand < 0 || (b.Product.SafetyStock > 0 && b.QtyOnHand < b.Product.SafetyStock)));
    }
}
