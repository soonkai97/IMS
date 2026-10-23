using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <param name="RequiresApproval">An approval rule fired when the order was submitted. With <c>status=Submitted</c> these are the orders waiting for a reviewer.</param>
/// <param name="IsLarge">The rule that fired needs the large tier (the Owner).</param>
/// <param name="TotalAmount">The sum of the line amounts, in money. Absent for a caller without <c>so.manage</c>.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
/// <param name="CreatedByKey">The integer key of the creator, for the ownership rules; not part of the JSON.</param>
/// <param name="SubmittedByKey">Same for the submitter, for the self-approval rule; not part of the JSON.</param>
/// <param name="Key">The integer key of the order: the position the seek cursor of the list is made from; not part of the JSON.</param>
public record SalesOrderListItem(Guid Id, string No, SalesOrderStatus Status, string WarehouseName, string CustomerName, DateTime CreatedAt, string CreatedBy,
    int LineCount, [property: VisibleWith(Permissions.SoManage), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? TotalAmount, bool RequiresApproval, bool IsLarge,
    [property: JsonIgnore] byte[] RowVersion, [property: JsonIgnore] int CreatedByKey, [property: JsonIgnore] int? SubmittedByKey, [property: JsonIgnore] int Key)
{
    /// <summary>Same value as <c>concurrencyToken</c> on the full order: compare it to know whether an open order has gone stale.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = "";

    /// <summary>
    /// The caller may approve or reject this order right now: it is Submitted and held for approval, routed to the caller's tier (the Owner reviews only <c>isLarge</c> orders, a
    /// Supervisor the rest, an Administrator all) and, unless self-approval is on, not submitted by the caller. A single order's <c>allowedActions</c> additionally tests the live rules.
    /// </summary>
    [Required]
    public bool CanAct { get; init; }
}
