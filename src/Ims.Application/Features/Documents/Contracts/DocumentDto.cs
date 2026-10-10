using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <param name="AllowedActions">What the caller may do right now (edit, submit, approve, reject, cancel, post, reverse). The UI shows buttons from this list only.</param>
public record DocumentDto(int Id, DocumentType Type, string No, DocumentStatus Status, int WarehouseId, string WarehouseName,
    int? PartnerId, string? PartnerName, IssuePurpose? Purpose, string? Remark, bool RequiresApproval,
    string? RejectionReason, string? CancelReason, string? ReversalReason, string? NegativeStockOverrideReason,
    string CreatedBy, DateTime CreatedAt, int? SubmittedById, string? SubmittedBy, DateTime? SubmittedAt,
    string? ApprovedBy, DateTime? ApprovedAt, string? PostedBy, DateTime? PostedAt,
    decimal TotalQty, decimal TotalAmount, IReadOnlyList<DocumentLineDto> Lines, IReadOnlyList<string> AllowedActions);
