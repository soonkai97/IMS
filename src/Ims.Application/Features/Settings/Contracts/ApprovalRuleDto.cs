using Ims.Domain;
namespace Ims.Application.Features.Settings;

/// <param name="AmountThreshold">Money. Null for a caller without <c>cost.view</c> (the Warehouse staff), and when the rule has no amount threshold.</param>
/// <param name="RoleRequired">Deprecated: the system role that holds the tier by default (<see cref="ApproverTierNames.RoleOf"/>), for a client that has not moved to <paramref name="ApproverTier"/>.</param>
public record ApprovalRuleDto(Guid Id, DocumentType DocType, [property: VisibleWith(Permissions.CostView)] decimal? AmountThreshold, decimal? QtyThreshold,
    ApproverTier ApproverTier, string RoleRequired, bool IsActive);
