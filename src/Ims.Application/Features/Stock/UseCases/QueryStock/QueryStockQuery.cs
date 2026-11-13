namespace Ims.Application.Features.Stock;

public sealed record QueryStockQuery(StockFilter Filter) : IQuery<PagedResult<StockRow>>;
