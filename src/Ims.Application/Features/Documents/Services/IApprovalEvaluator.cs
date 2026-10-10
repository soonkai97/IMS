using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IApprovalEvaluator
{
    Task<ApprovalAssessment> AssessAsync(StockDocument document, CancellationToken ct);
}
