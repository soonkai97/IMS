using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <remarks><c>XxxBy</c> is the user name, <c>XxxById</c> the id and <c>XxxByName</c> the display name of whoever did it (the naming of <c>DocumentDto</c>).</remarks>
/// <param name="RequiresApproval">An approval rule fired when the order was submitted (false for a Draft, and for an order released at once because no rule fired).</param>
/// <param name="IsLarge">The rule that fired needs the large tier (the Owner), not a Supervisor.</param>
/// <param name="IsReturned">A Draft that a reviewer sent back (<c>RejectionReason</c> holds why, <c>ReturnedBy*</c> who).</param>
/// <param name="ApprovedBy">Who approved it; null for an order released at once (no rule fired): it is Approved with no approver, and <c>approvedAt</c> is when it was released.</param>
/// <param name="TotalQty">The total base quantity: what a quantity rule of the approval rules is tested against.</param>
/// <param name="TotalAmount">The sum of the line amounts, in money. Null for a caller without <c>cost.view</c>.</param>
/// <param name="AllowedActions">What the caller may do right now (edit, submit, recall, approve, reject, cancel). The UI shows buttons from this list only. It already accounts for ownership, the approver the order is routed to (the Owner reviews only <c>isLarge</c> orders, a Supervisor the rest) and self-approval, so a listed action is one the API will accept.</param>
/// <param name="ApprovalTriggers">While the order is Submitted, or Approved after a rule held it: which thresholds it reaches. The amounts are null for a caller without <c>cost.view</c>. Empty otherwise.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
/// <param name="CreatedByKey">The integer key of the creator, for the ownership rules (compared with the caller's own integer key, never with a client id); not part of the JSON.</param>
/// <param name="SubmittedByKey">Same for the submitter, for the self-approval rule; not part of the JSON.</param>
/// <param name="ApprovedByKey">The integer key of the approver, to look their name up; not part of the JSON.</param>
/// <param name="ReturnedByKey">The integer key of the reviewer who sent it back, to look their name up; not part of the JSON.</param>
public record PurchaseOrderDto(Guid Id, string No, PurchaseOrderStatus Status, Guid WarehouseId, string WarehouseName, Guid SupplierId, string SupplierName, string? Remark,
    Guid CreatedById, string CreatedBy, string CreatedByName, DateTime CreatedAt,
    bool RequiresApproval, bool IsLarge, bool IsReturned, string? RejectionReason, string? CancelReason,
    DateTime? SubmittedAt, DateTime? ApprovedAt, DateTime? ReturnedAt,
    decimal TotalQty, [property: VisibleWith(Permissions.CostView)] decimal? TotalAmount, IReadOnlyList<PurchaseOrderLineDto> Lines,
    [property: JsonIgnore] byte[] RowVersion, [property: JsonIgnore] int CreatedByKey, [property: JsonIgnore] int? SubmittedByKey,
    [property: JsonIgnore] int? ApprovedByKey, [property: JsonIgnore] int? ReturnedByKey)
{
    /// <summary>Changes whenever the order (header, status or lines) changes; opaque: compare it only for equality. Also sent as the ETag header; send it back as If-Match to refuse a stale save.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = "";

    [Required]
    public IReadOnlyList<string> AllowedActions { get; init; } = [];

    [Required]
    public IReadOnlyList<ApprovalTriggerDto> ApprovalTriggers { get; init; } = [];

    public Guid? SubmittedById { get; init; }
    public string? SubmittedBy { get; init; }
    public string? SubmittedByName { get; init; }
    public string? ApprovedBy { get; init; }
    public string? ApprovedByName { get; init; }
    public Guid? ReturnedById { get; init; }
    public string? ReturnedBy { get; init; }
    public string? ReturnedByName { get; init; }
}
