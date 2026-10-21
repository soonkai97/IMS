using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IApprovalEvaluator
{
    /// <summary>Tests the document (its quantity and value as stored; an issue is valued by <c>StockDocument.EstimateIssueCosts</c>) against the active rules.</summary>
    Task<ApprovalAssessment> AssessAsync(StockDocument document, CancellationToken ct);

    /// <summary>The same test for something held by the rules of a <see cref="DocumentType"/> that is not a stock document (a purchase order: its total quantity and value).</summary>
    Task<ApprovalAssessment> AssessAsync(DocumentType type, decimal totalQty, decimal totalAmount, CancellationToken ct);

    /// <summary>
    /// Whether the caller may approve or reject a document held for approval: null when they may, otherwise the sentence that says why not.
    /// The same answer the approve and reject commands enforce and <c>allowedActions</c> shows.
    /// </summary>
    Task<string?> ReviewRefusalAsync(DocumentType type, bool isLarge, decimal totalQty, decimal totalAmount, CancellationToken ct);

    /// <summary>The thresholds of the active rules that this quantity and value reach: why the document needs approval.</summary>
    Task<IReadOnlyList<ApprovalTrigger>> TriggersAsync(DocumentType type, decimal totalQty, decimal totalAmount, CancellationToken ct);
}
