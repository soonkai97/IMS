using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <summary>One page of products below safety stock, the biggest shortfall first.</summary>
public sealed class LowStockPageSpec : Specification<StockBalance, LowStockAlert>
{
    public LowStockPageSpec(LowStockFilter f, IReadOnlyCollection<int>? scope)
    {
        StockFilters.Apply(Query, new StockFilter(f.WarehouseId, null, null, null, LowOnly: true), scope, search: null);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize, Limits.MaxStockPageSize);
        Query.OrderByDescending(b => b.Product.SafetyStock - b.QtyOnHand).ThenBy(b => b.Product.Sku)
            .Skip((page - 1) * size).Take(size)
            .Select(b => new LowStockAlert(b.ProductId, b.Product.Sku, b.Product.Name, b.WarehouseId, b.Warehouse.Code, b.Warehouse.Name,
                b.QtyOnHand, b.Product.SafetyStock, b.Product.SafetyStock - b.QtyOnHand));
    }
}
