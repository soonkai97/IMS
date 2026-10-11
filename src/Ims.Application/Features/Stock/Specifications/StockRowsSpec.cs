using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <summary>Stock rows ordered by SKU. <c>paged</c> false returns every row (exports, summaries).</summary>
public sealed class StockRowsSpec : Specification<StockBalance, StockRow>
{
    public StockRowsSpec(StockFilter f, IReadOnlyCollection<int>? scope, ProductSearch? search, bool paged)
    {
        StockFilters.Apply(Query, f, scope, search);
        switch (f.SortBy, f.Desc)
        {
            case (StockSort.QtyOnHand, false): Query.OrderBy(b => b.QtyOnHand).ThenBy(b => b.Product.Sku).ThenBy(b => b.Warehouse.Code); break;
            case (StockSort.QtyOnHand, true): Query.OrderByDescending(b => b.QtyOnHand).ThenBy(b => b.Product.Sku).ThenBy(b => b.Warehouse.Code); break;
            case (StockSort.Value, false): Query.OrderBy(b => b.QtyOnHand * b.AvgCost).ThenBy(b => b.Product.Sku).ThenBy(b => b.Warehouse.Code); break;
            case (StockSort.Value, true): Query.OrderByDescending(b => b.QtyOnHand * b.AvgCost).ThenBy(b => b.Product.Sku).ThenBy(b => b.Warehouse.Code); break;
            case (StockSort.Warehouse, _): Query.OrderBy(b => b.Warehouse.Code).ThenBy(b => b.Product.Sku); break;
            case (StockSort.Name, false): Query.OrderBy(b => b.Product.Name).ThenBy(b => b.Product.Sku).ThenBy(b => b.Warehouse.Code); break;
            case (StockSort.Name, true): Query.OrderByDescending(b => b.Product.Name).ThenBy(b => b.Product.Sku).ThenBy(b => b.Warehouse.Code); break;
            case (_, true): Query.OrderByDescending(b => b.Product.Sku).ThenBy(b => b.Warehouse.Code); break;
            default: Query.OrderBy(b => b.Product.Sku).ThenBy(b => b.Warehouse.Code); break;
        }
        if (paged)
        {
            var (page, size) = Paging.Normalize(f.Page, f.PageSize, Limits.MaxStockPageSize);
            Query.Skip((page - 1) * size).Take(size);
        }
        else
        {
            Query.Take(Limits.MaxExportRows); // exports and summaries are capped: memory can never grow with the data
        }
        Query.Select(b => new StockRow(b.ProductId, b.Product.Sku, b.Product.Name, b.Product.Category != null ? b.Product.Category.Name : null,
            b.Product.BaseUom.Code, b.WarehouseId, b.Warehouse.Code, b.Warehouse.Name, b.QtyOnHand, b.QtyInTransit, b.AvgCost, b.QtyOnHand * b.AvgCost,
            b.Product.SafetyStock, b.Product.IsActive && (b.QtyOnHand < 0 || (b.Product.SafetyStock > 0 && b.QtyOnHand < b.Product.SafetyStock)), b.QtyOnHand < 0));
    }
}
