using Ims.Domain;
namespace Ims.Application.Features.Settings;

public record ApprovalRuleDto(int Id, DocumentType DocType, decimal? AmountThreshold, decimal? QtyThreshold, string RoleRequired, bool IsActive);
