using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <param name="LowOnly">Only low stock, by the one definition (<see cref="LowStockRule"/>) that the low-stock list, its export, the Home count and the menu badge also use, so this list has exactly the rows of <c>GET /api/stock/low-stock</c> (same warehouse filter). That includes active products with a safety level that have never been stocked in a warehouse: they appear for each active warehouse in scope that has no balance row for them, with a quantity of 0 and no average cost (null). The other filters and the sort apply.</param>
/// <param name="SortBy">Sku (default), QtyOnHand, Value (needs <c>valuation.view</c>), Warehouse or Name; every order ends in SKU and warehouse code, so paging never repeats a row.</param>
public record StockFilter(int? WarehouseId, int? ProductId, int? CategoryId, string? Search, bool LowOnly = false,
    int Page = 1, int PageSize = Limits.DefaultListPageSize, StockSort SortBy = StockSort.Sku, bool Desc = false);
