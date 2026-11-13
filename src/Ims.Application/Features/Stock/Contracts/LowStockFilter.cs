using Ims.Domain;
namespace Ims.Application.Features.Stock;

public sealed record LowStockFilter(Guid? WarehouseId = null, int Page = 1, int PageSize = Limits.DefaultListPageSize);
