using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <remarks>
/// <c>XxxBy</c> is the user name, <c>XxxById</c> the user id and <c>XxxByName</c> the display name of whoever did it.
/// </remarks>
/// <param name="RequiresApproval">An approval rule fired when the document was submitted (false for a Draft).</param>
/// <param name="RequiresOverride">An issue was submitted although stock is short (negative stock is allowed): the approver must give a reason when approving, which forces approval whatever the value thresholds say. A fact about the moment of submission: it stays true after approval and posting; the stored reason is in <c>NegativeStockOverrideReason</c>.</param>
/// <param name="NegativeStockOverrideReason">Why the approver let stock go below zero; set when a document that <c>requiresOverride</c> is approved, otherwise null.</param>
/// <param name="PurchaseOrder">The purchase order this receipt was created from (id and number); null for every other document, and omitted while the Purchasing module is off. Set when the receipt is created and never changed.</param>
/// <param name="ReversedAt">When a Reversed document was reversed, with <c>ReversedBy</c> and <c>ReversedByName</c> the user who did it ("Reversed by {name} on {date}"); null otherwise. <c>ReversalReason</c> holds why.</param>
/// <param name="IsLarge">The approval rule that fired needs a role above Supervisor (for example the Owner).</param>
/// <param name="IsReturned">A Draft that a reviewer sent back (<c>RejectionReason</c> holds why, <c>ReturnedBy*</c> who).</param>
/// <param name="TotalAmount">Value of the document: receipts at their entered cost; an issue at its booked cost once posted, and before that at the average cost when it was last saved or submitted (see <c>AmountIsEstimate</c>). For a Submitted document this is the value the approval rules were tested against. Null for a caller without <c>cost.view</c> (the Warehouse staff).</param>
/// <param name="AllowedActions">What the caller may do right now (edit, submit, recall, approve, reject, cancel, post, reverse). The UI shows buttons from this list only. It already accounts for ownership (Warehouse staff and Clerk act only on their own documents), the approver the document is routed to (an Owner reviews only <c>isLarge</c> documents, a Supervisor the rest) and self-approval, so a listed action is one the API will accept.</param>
/// <param name="ApprovalTriggers">While the document is Submitted or Approved and an approval rule held it back: which thresholds it reaches (value or quantity), for the approver and the "sent for approval" message. Empty otherwise, and when only a negative-stock override (<c>requiresOverride</c>) sent it to approval.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON (the token is set by <see cref="IDocumentVersion"/> where the document is read).</param>
/// <param name="WarehouseKey">The integer key of the warehouse, for the server to look up stock without translating <c>WarehouseId</c> back with another query; not part of the JSON.</param>
/// <param name="CreatedByKey">The integer key of the user who created the document, for the ownership rules (compared with the caller's own integer key, never with a client id); not part of the JSON.</param>
/// <param name="SubmittedByKey">Same for the user who submitted it, for the self-approval rule; not part of the JSON.</param>
public record DocumentDto(Guid Id, DocumentType Type, string No, DocumentStatus Status, Guid WarehouseId, string WarehouseName,
    Guid? PartnerId, string? PartnerName, IssuePurpose? Purpose, string? Remark,
    bool RequiresApproval, bool RequiresOverride, bool IsLarge, bool IsReturned,
    string? RejectionReason, string? CancelReason, string? ReversalReason, string? NegativeStockOverrideReason,
    Guid CreatedById, string CreatedBy, string CreatedByName, DateTime CreatedAt,
    Guid? SubmittedById, string? SubmittedBy, string? SubmittedByName, DateTime? SubmittedAt,
    string? ApprovedBy, string? ApprovedByName, DateTime? ApprovedAt,
    string? PostedBy, string? PostedByName, DateTime? PostedAt,
    string? ReversedBy, string? ReversedByName, DateTime? ReversedAt,
    Guid? ReturnedById, string? ReturnedBy, string? ReturnedByName, DateTime? ReturnedAt,
    decimal TotalQty, [property: VisibleWith(Permissions.CostView)] decimal? TotalAmount, IReadOnlyList<DocumentLineDto> Lines, IReadOnlyList<string> AllowedActions,
    IReadOnlyList<ApprovalTriggerDto> ApprovalTriggers, [property: JsonIgnore] byte[] RowVersion, [property: JsonIgnore] int WarehouseKey,
    [property: JsonIgnore] int CreatedByKey, [property: JsonIgnore] int? SubmittedByKey, LinkedPurchaseOrder? PurchaseOrder = null)
{
    /// <summary>True for an issue that has not posted yet: its lines carry the average cost seen when it was last saved or submitted, and posting books the real cost. Label the amount "estimated".</summary>
    [Required]
    public bool AmountIsEstimate { get; init; } = Type == DocumentType.Issue && DocumentStatuses.IsOpen(Status);

    /// <summary>Changes whenever the document (header, status or lines) changes; opaque (a keyed hash bound to this document, not a counter): compare it only for equality. Also sent as the ETag header; send it back as If-Match to refuse a stale save.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = "";
}
