using Ims.Domain;
namespace Ims.Application.Features.Settings;

/// <param name="RoleRequired">Deprecated (ADR roles-permissions 2.7): a system role name, mapped to a tier (Owner and Admin Large, the other system roles Standard). Send <paramref name="ApproverTier"/> instead; one of the two is required, and when both are sent they must agree.</param>
public record ApprovalRuleSaveRequest(DocumentType DocType, decimal? AmountThreshold, decimal? QtyThreshold, string? RoleRequired = null, bool IsActive = true, ApproverTier? ApproverTier = null);
