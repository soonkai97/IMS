namespace Ims.Application.Features.Documents;

/// <param name="IsLarge">A rule fired that only a role above Supervisor may satisfy.</param>
public sealed record ApprovalAssessment(bool Required, decimal Amount, bool IsLarge = false);
