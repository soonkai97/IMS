using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <remarks>Same naming as <see cref="DocumentDto"/>: <c>CreatedBy</c> is the user name, <c>CreatedById</c> the id, <c>CreatedByName</c> the display name.</remarks>
/// <param name="TotalAmount">Same meaning as on the full document: an unposted issue is valued at the average cost seen when it was last saved or submitted (<c>AmountIsEstimate</c>). Null for a caller without <c>cost.view</c> (the Warehouse staff).</param>
/// <param name="RequiresApproval">An approval rule fired when the document was submitted. With <c>status=Submitted</c> this is the approvals inbox.</param>
/// <param name="IsReturned">A Draft that a reviewer sent back.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
public record DocumentListItem(int Id, DocumentType Type, string No, DocumentStatus Status, string WarehouseName,
    string? PartnerName, DateTime CreatedAt, DateTime? PostedAt, decimal TotalQty, [property: VisibleWith(Permissions.CostView)] decimal? TotalAmount, string CreatedBy,
    int CreatedById, string CreatedByName, int? SubmittedById, string? SubmittedBy, string? SubmittedByName, DateTime? SubmittedAt,
    bool RequiresApproval, bool RequiresOverride, bool IsLarge, bool IsReturned,
    [property: JsonIgnore] byte[] RowVersion)
{
    /// <summary>Same value as <c>concurrencyToken</c> on the full document: compare it to know whether an open document has gone stale.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = DocumentVersion.Token(RowVersion);

    /// <summary>True for an issue that has not posted yet: <c>totalAmount</c> is an estimate at the average cost seen when it was last saved or submitted.</summary>
    [Required]
    public bool AmountIsEstimate { get; init; } = Type == DocumentType.Issue && DocumentStatuses.IsOpen(Status);

    /// <summary>
    /// The caller may approve or reject this document right now: it is Submitted and held for approval, routed to the caller's role
    /// (an Owner reviews only <c>isLarge</c> documents, a Supervisor the rest, an Admin all) and, unless self-approval is on, not submitted by the caller.
    /// This is exactly the rule behind the "waiting for approval" count. A single document's <c>allowedActions</c> additionally tests the live approval rules.
    /// </summary>
    [Required]
    public bool CanAct { get; init; }
}
