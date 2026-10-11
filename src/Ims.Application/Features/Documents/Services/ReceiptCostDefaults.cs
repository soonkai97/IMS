using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ReceiptCostDefaults(IReadRepository<StockBalance> balances) : IReceiptCostDefaults, IAppService
{
    public async Task ApplyAsync(StockDocument document, CancellationToken ct)
    {
        if (document.Type != DocumentType.Receipt || !document.Lines.Any(l => l.CostFromAverage)) return;
        var ids = document.Lines.Where(l => l.CostFromAverage).Select(l => l.ProductId).Distinct().ToList();
        var averageCosts = (await balances.ListAsync(new BalancesSpec(document.WarehouseId, ids, tracked: false), ct)).ToDictionary(b => b.ProductId, b => b.AvgCost);
        document.ApplyKnownReceiptCosts(averageCosts);
    }
}
