namespace Ims.Application.Features.Stock;

public record QueryStockQuery(StockFilter Filter) : IQuery<PagedResult<StockRow>>;
