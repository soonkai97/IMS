using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ApprovalEvaluator(IReadRepository<ApprovalRule> rules, ICurrentUser current) : IApprovalEvaluator, IAppService
{
    public async Task<ApprovalAssessment> AssessAsync(StockDocument document, CancellationToken ct)
    {
        // Receipts are valued at their entered cost; an unposted issue carries its estimated cost (see StockDocument.EstimateIssueCosts),
        // so the amount tested here is the one the approver later sees on the document.
        var amount = document.TotalAmount;
        var active = await rules.ListAsync(new ActiveRulesForTypeSpec(document.Type), ct);
        var qty = document.TotalQty;
        return new ApprovalAssessment(
            ApprovalPolicy.RequiresApproval(active, document.Type, qty, amount),
            ApprovalPolicy.CanApprove(current.Role, active, document.Type, qty, amount),
            amount,
            ApprovalPolicy.IsLarge(active, document.Type, qty, amount));
    }

    public async Task<string?> ReviewRefusalAsync(DocumentType type, bool isLarge, decimal totalQty, decimal totalAmount, CancellationToken ct)
    {
        // Routing first: the Owner reviews only large documents, a Supervisor only the rest (PRD 3.1).
        if (!ApprovalPolicy.MayReview(current.Role, isLarge))
            return current.Role == Roles.Owner ? Messages.OwnerReviewsLargeOnly : Messages.AboveApprovalLimit;

        var active = await rules.ListAsync(new ActiveRulesForTypeSpec(type), ct);
        return ApprovalPolicy.CanApprove(current.Role, active, type, totalQty, totalAmount) ? null : Messages.AboveApprovalLimit;
    }

    public async Task<IReadOnlyList<ApprovalTrigger>> TriggersAsync(DocumentType type, decimal totalQty, decimal totalAmount, CancellationToken ct) =>
        (await rules.ListAsync(new ActiveRulesForTypeSpec(type), ct)).SelectMany(r => r.Triggers(type, totalQty, totalAmount)).ToList();
}
