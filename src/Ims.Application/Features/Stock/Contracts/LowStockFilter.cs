using Ims.Domain;
namespace Ims.Application.Features.Stock;

public record LowStockFilter(int? WarehouseId = null, int Page = 1, int PageSize = Limits.DefaultListPageSize);
