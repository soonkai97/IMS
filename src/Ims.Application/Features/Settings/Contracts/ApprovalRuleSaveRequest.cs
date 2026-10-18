using Ims.Domain;
namespace Ims.Application.Features.Settings;

/// <param name="ApproverTier">Required (the validator refuses a request without one): the approval a document that fires the rule needs. The deprecated <c>roleRequired</c> of earlier releases is no longer read; a client that still sends it is ignored.</param>
public record ApprovalRuleSaveRequest(DocumentType DocType, decimal? AmountThreshold, decimal? QtyThreshold, bool IsActive = true, ApproverTier? ApproverTier = null);
