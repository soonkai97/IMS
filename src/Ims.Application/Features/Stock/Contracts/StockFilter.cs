using Ims.Domain;
namespace Ims.Application.Features.Stock;

public record StockFilter(int? WarehouseId, int? ProductId, int? CategoryId, string? Search, bool LowOnly = false,
    int Page = 1, int PageSize = Limits.DefaultListPageSize, StockSort SortBy = StockSort.Sku, bool Desc = false);
