using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ApprovalEvaluator(IReadRepository<ApprovalRule> rules, IReadRepository<StockBalance> balances, ICurrentUser current)
    : IApprovalEvaluator, IAppService
{
    public async Task<ApprovalAssessment> AssessAsync(StockDocument document, CancellationToken ct)
    {
        // Receipts are valued at their entered cost, issues at the current average cost.
        decimal amount;
        if (document.Type == DocumentType.Receipt) amount = document.TotalAmount;
        else
        {
            var ids = document.Lines.Select(l => l.ProductId).Distinct().ToList();
            var avg = (await balances.ListAsync(new BalancesSpec(document.WarehouseId, ids, tracked: false), ct)).ToDictionary(b => b.ProductId, b => b.AvgCost);
            amount = document.Lines.Sum(l => l.BaseQty * avg.GetValueOrDefault(l.ProductId));
        }

        var active = await rules.ListAsync(new ActiveRulesForTypeSpec(document.Type), ct);
        var qty = document.TotalQty;
        return new ApprovalAssessment(
            ApprovalPolicy.RequiresApproval(active, document.Type, qty, amount),
            ApprovalPolicy.CanApprove(current.Role, active, document.Type, qty, amount),
            amount);
    }
}
