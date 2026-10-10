using Ims.Domain;
namespace Ims.Application.Features.Settings;

public record ApprovalRuleSaveRequest(DocumentType DocType, decimal? AmountThreshold, decimal? QtyThreshold, string RoleRequired, bool IsActive);
