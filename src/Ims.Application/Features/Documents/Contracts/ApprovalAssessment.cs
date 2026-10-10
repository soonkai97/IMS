namespace Ims.Application.Features.Documents;

public record ApprovalAssessment(bool Required, bool CallerMayApprove, decimal Amount);
