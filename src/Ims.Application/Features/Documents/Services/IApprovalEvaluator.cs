using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IApprovalEvaluator
{
    Task<ApprovalAssessment> AssessAsync(StockDocument document, CancellationToken ct);

    /// <summary>
    /// Whether the caller may approve or reject a document held for approval: null when they may, otherwise the sentence that says why not.
    /// The same answer the approve and reject commands enforce and <c>allowedActions</c> shows.
    /// </summary>
    Task<string?> ReviewRefusalAsync(DocumentType type, bool isLarge, decimal totalQty, decimal totalAmount, CancellationToken ct);

    /// <summary>The same check for a loaded document; an issue is valued at the current average cost, as at submission.</summary>
    Task<string?> ReviewRefusalAsync(StockDocument document, CancellationToken ct);
}
