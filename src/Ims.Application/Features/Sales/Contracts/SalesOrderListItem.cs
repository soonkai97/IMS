using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <param name="TotalAmount">The sum of the line amounts, in money. Absent for a caller without <c>so.manage</c>.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
/// <param name="CreatedByKey">The integer key of the creator, for the ownership rules; not part of the JSON.</param>
/// <param name="Key">The integer key of the order: the position the seek cursor of the list is made from; not part of the JSON.</param>
public record SalesOrderListItem(Guid Id, string No, SalesOrderStatus Status, string WarehouseName, string CustomerName, DateTime CreatedAt, string CreatedBy,
    int LineCount, [property: VisibleWith(Permissions.SoManage), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? TotalAmount,
    [property: JsonIgnore] byte[] RowVersion, [property: JsonIgnore] int CreatedByKey, [property: JsonIgnore] int Key)
{
    /// <summary>Same value as <c>concurrencyToken</c> on the full order: compare it to know whether an open order has gone stale.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = "";
}
