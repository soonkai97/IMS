using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class IssueValuation(IReadRepository<StockBalance> balances) : IIssueValuation, IAppService
{
    public async Task EstimateAsync(StockDocument document, CancellationToken ct)
    {
        if (document.Type != DocumentType.Issue || document.Lines.Count == 0) return;
        var ids = document.Lines.Select(l => l.ProductId).Distinct().ToList();
        var averageCosts = (await balances.ListAsync(new BalancesSpec(document.WarehouseId, ids, tracked: false), ct)).ToDictionary(b => b.ProductId, b => b.AvgCost);
        document.EstimateIssueCosts(averageCosts);
    }
}
