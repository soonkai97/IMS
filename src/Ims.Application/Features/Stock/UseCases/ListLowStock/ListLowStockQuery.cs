namespace Ims.Application.Features.Stock;

public sealed record ListLowStockQuery(LowStockFilter Filter) : IQuery<PagedResult<LowStockAlert>>;
