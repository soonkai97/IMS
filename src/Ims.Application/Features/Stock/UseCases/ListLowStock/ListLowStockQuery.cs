namespace Ims.Application.Features.Stock;

public record ListLowStockQuery(LowStockFilter Filter) : IQuery<PagedResult<LowStockAlert>>;
