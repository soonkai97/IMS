using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <remarks><c>CreatedBy</c> is the user name, <c>CreatedById</c> the id and <c>CreatedByName</c> the display name of whoever created the order (the naming of <c>DocumentDto</c>).</remarks>
/// <param name="TotalAmount">The sum of the line amounts, in money. Absent for a caller without <c>cost.view</c>.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
public record PurchaseOrderDto(Guid Id, string No, PurchaseOrderStatus Status, Guid WarehouseId, string WarehouseName, Guid SupplierId, string SupplierName, string? Remark,
    Guid CreatedById, string CreatedBy, string CreatedByName, DateTime CreatedAt,
    [property: VisibleWith(Permissions.CostView)] decimal? TotalAmount, IReadOnlyList<PurchaseOrderLineDto> Lines, [property: JsonIgnore] byte[] RowVersion)
{
    /// <summary>Changes whenever the order (header, status or lines) changes; opaque: compare it only for equality. Also sent as the ETag header; send it back as If-Match to refuse a stale save.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = "";
}
