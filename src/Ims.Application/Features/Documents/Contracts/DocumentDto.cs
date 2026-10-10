using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <remarks>
/// <c>XxxBy</c> is the user name, <c>XxxById</c> the user id and <c>XxxByName</c> the display name of whoever did it.
/// </remarks>
/// <param name="RequiresApproval">An approval rule fired when the document was submitted (false for a Draft).</param>
/// <param name="RequiresOverride">An issue was submitted although stock is short (negative stock is allowed): a supervisor must give a reason when posting. Still true after posting if an override was used.</param>
/// <param name="IsLarge">The approval rule that fired needs a role above Supervisor (for example the Owner).</param>
/// <param name="IsReturned">A Draft that a reviewer sent back (<c>RejectionReason</c> holds why, <c>ReturnedBy*</c> who).</param>
/// <param name="AllowedActions">What the caller may do right now (edit, submit, approve, reject, cancel, post, reverse). The UI shows buttons from this list only.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
public record DocumentDto(int Id, DocumentType Type, string No, DocumentStatus Status, int WarehouseId, string WarehouseName,
    int? PartnerId, string? PartnerName, IssuePurpose? Purpose, string? Remark,
    bool RequiresApproval, bool RequiresOverride, bool IsLarge, bool IsReturned,
    string? RejectionReason, string? CancelReason, string? ReversalReason, string? NegativeStockOverrideReason,
    int CreatedById, string CreatedBy, string CreatedByName, DateTime CreatedAt,
    int? SubmittedById, string? SubmittedBy, string? SubmittedByName, DateTime? SubmittedAt,
    string? ApprovedBy, string? ApprovedByName, DateTime? ApprovedAt,
    string? PostedBy, string? PostedByName, DateTime? PostedAt,
    int? ReturnedById, string? ReturnedBy, string? ReturnedByName, DateTime? ReturnedAt,
    decimal TotalQty, decimal TotalAmount, IReadOnlyList<DocumentLineDto> Lines, IReadOnlyList<string> AllowedActions,
    [property: JsonIgnore] byte[] RowVersion)
{
    /// <summary>Changes whenever the document (header, status or lines) changes. Also sent as the ETag header; send it back as If-Match to refuse a stale save.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = DocumentVersion.Token(RowVersion);
}
