namespace Ims.Application.Features.Stock;

public class GetStockSummaryHandler(IStockSummaryReader summary, IWarehouseScope scope) : IQueryHandler<GetStockSummaryQuery, StockSummary>
{
    public async Task<StockSummary> HandleAsync(GetStockSummaryQuery q, CancellationToken ct)
    {
        var (value, skus, low) = await summary.GetAsync(scope.Ids(), ct);
        return new StockSummary(value, skus, low);
    }
}
