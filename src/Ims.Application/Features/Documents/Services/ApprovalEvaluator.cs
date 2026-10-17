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
            amount,
            ApprovalPolicy.IsLarge(active, document.Type, qty, amount));
    }

    public async Task<string?> ReviewRefusalAsync(DocumentType type, bool isLarge, decimal totalQty, decimal totalAmount, CancellationToken ct)
    {
        // Routing first, by the approval tier the token holds: a large document goes to the large tier, any other to the standard tier (PRD 3.1).
        var rights = current.Rights();
        if (!ApprovalPolicy.MayReview(rights, isLarge))
            return rights.Has(Permissions.DocumentApproveLarge) && !rights.Has(Permissions.DocumentApprove) ? Messages.OwnerReviewsLargeOnly : Messages.AboveApprovalLimit;

        // Then the limit by the rules in force now, which can be stricter than when the document was stored: a rule of the Large tier in force needs the large tier.
        var active = await rules.ListAsync(new ActiveRulesForTypeSpec(type), ct);
        return ApprovalPolicy.MayReviewUnderRulesInForce(rights, isLarge, active, type, totalQty, totalAmount) ? null : Messages.AboveApprovalLimit;
    }

    public async Task<IReadOnlyList<ApprovalTrigger>> TriggersAsync(DocumentType type, decimal totalQty, decimal totalAmount, CancellationToken ct) =>
        (await rules.ListAsync(new ActiveRulesForTypeSpec(type), ct)).SelectMany(r => r.Triggers(type, totalQty, totalAmount)).ToList();
}
