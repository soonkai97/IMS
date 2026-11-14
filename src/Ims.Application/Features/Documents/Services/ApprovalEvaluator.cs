using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class ApprovalEvaluator(IReadRepository<ApprovalRule> rules, ICurrentUser current) : IApprovalEvaluator, IAppService
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
        // The decision is the Domain's (ApprovalPolicy.ReviewRefusal); this reads the rules in force only when they can change the answer, then maps the reason to its sentence.
        var rights = current.Rights();
        IReadOnlyList<ApprovalRule> active = ApprovalPolicy.NeedsRulesInForce(rights, isLarge, isReturn) ? await rules.ListAsync(new ActiveRulesForTypeSpec(type), ct) : [];
        return ApprovalPolicy.ReviewRefusal(rights, isLarge, active, type, totalQty, totalAmount, isReturn) switch
        {
            ReviewRefusalReason.None => null,
            ReviewRefusalReason.OwnerReviewsLargeOnly => Messages.OwnerReviewsLargeOnly,
            ReviewRefusalReason.AboveApprovalLimit => Messages.AboveApprovalLimit,
            var unknown => throw new ArgumentOutOfRangeException(nameof(unknown), unknown, null),
        };
    }

    public async Task<IReadOnlyList<ApprovalTrigger>> TriggersAsync(DocumentType type, decimal totalQty, decimal totalAmount, CancellationToken ct, bool isReturn = false) =>
        isReturn ? [] : (await rules.ListAsync(new ActiveRulesForTypeSpec(type), ct)).SelectMany(r => r.Triggers(type, totalQty, totalAmount)).ToList();
}
