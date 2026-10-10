using Ims.Domain;
namespace Ims.Application.Features.Settings;

/// <param name="AmountThreshold">Money. Null for a caller without <c>cost.view</c> (the Warehouse staff), and when the rule has no amount threshold.</param>
public record ApprovalRuleDto(int Id, DocumentType DocType, [property: VisibleWith(Permissions.CostView)] decimal? AmountThreshold, decimal? QtyThreshold,
    string RoleRequired, bool IsActive);
