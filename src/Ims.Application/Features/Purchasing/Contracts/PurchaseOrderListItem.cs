using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <param name="TotalAmount">The sum of the line amounts, in money. Null for a caller without <c>cost.view</c>.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
/// <param name="Key">The integer key of the order: the position the seek cursor of the list is made from; not part of the JSON.</param>
public record PurchaseOrderListItem(Guid Id, string No, PurchaseOrderStatus Status, string WarehouseName, string SupplierName, DateTime CreatedAt, string CreatedBy,
    int LineCount, [property: VisibleWith(Permissions.CostView)] decimal? TotalAmount, [property: JsonIgnore] byte[] RowVersion, [property: JsonIgnore] int Key)
{
    /// <summary>Same value as <c>concurrencyToken</c> on the full order: compare it to know whether an open order has gone stale.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = "";
}
