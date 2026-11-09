using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ApprovalEvaluator(IReadRepository<ApprovalRule> rules, ICurrentUser current) : IApprovalEvaluator, IAppService
{
    // Receipts are valued at their entered cost; an unposted issue carries its estimated cost (see StockDocument.EstimateIssueCosts),
    // so the amount tested here is the one the approver later sees on the document. A return is held for approval whatever it is worth (ApprovalPolicy.ForReturn).
    public Task<ApprovalAssessment> AssessAsync(StockDocument document, CancellationToken ct)
    {
        if (!document.IsReturn) return AssessAsync(document.Type, document.TotalQty, document.TotalAmount, ct);
        var (required, isLarge) = ApprovalPolicy.ForReturn;
        return Task.FromResult(new ApprovalAssessment(required, document.TotalAmount, isLarge));
    }

    public async Task<ApprovalAssessment> AssessAsync(DocumentType type, decimal totalQty, decimal totalAmount, CancellationToken ct)
    {
        var active = await rules.ListAsync(new ActiveRulesForTypeSpec(type), ct);
        return new ApprovalAssessment(
            ApprovalPolicy.RequiresApproval(active, type, totalQty, totalAmount),
            totalAmount,
            ApprovalPolicy.IsLarge(active, type, totalQty, totalAmount));
    }

    public async Task<string?> ReviewRefusalAsync(DocumentType type, bool isLarge, decimal totalQty, decimal totalAmount, CancellationToken ct, bool isReturn = false)
    {
        // Routing first, by the approval tier the token holds: a large document goes to the large tier, any other to the standard tier (PRD 3.1).
        var rights = current.Rights();
        if (!ApprovalPolicy.MayReview(rights, isLarge))
            return rights.Has(Permissions.DocumentApproveLarge) && !rights.Has(Permissions.DocumentApprove) ? Messages.OwnerReviewsLargeOnly : Messages.AboveApprovalLimit;
        if (isReturn) return null; // a return has no limit: the rules in force do not apply to it

        // Then the limit by the rules in force now, which can be stricter than when the document was stored: a rule of the Large tier in force needs the large tier.
        var active = await rules.ListAsync(new ActiveRulesForTypeSpec(type), ct);
        return ApprovalPolicy.MayReviewUnderRulesInForce(rights, isLarge, active, type, totalQty, totalAmount) ? null : Messages.AboveApprovalLimit;
    }

    public async Task<IReadOnlyList<ApprovalTrigger>> TriggersAsync(DocumentType type, decimal totalQty, decimal totalAmount, CancellationToken ct, bool isReturn = false) =>
        isReturn ? [] : (await rules.ListAsync(new ActiveRulesForTypeSpec(type), ct)).SelectMany(r => r.Triggers(type, totalQty, totalAmount)).ToList();
}
