using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

internal static class StockFilters
{
    public static void Apply(ISpecificationBuilder<StockBalance> query, StockFilter f, IReadOnlyCollection<int>? scope)
    {
        query.AsNoTracking();
        if (scope is not null) query.Where(b => scope.Contains(b.WarehouseId));
        if (f.WarehouseId is { } w) query.Where(b => b.WarehouseId == w);
        if (f.ProductId is { } p) query.Where(b => b.ProductId == p);
        if (f.CategoryId is { } c) query.Where(b => b.Product.CategoryId == c);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            query.Where(b => b.Product.Sku.Contains(s) || b.Product.Name.Contains(s) || b.Product.Barcodes.Any(x => x.Barcode == s));
        }
        if (f.LowOnly) query.Where(b => b.Product.IsActive && b.Product.SafetyStock > 0 && b.QtyOnHand < b.Product.SafetyStock);
    }
}
