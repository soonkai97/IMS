using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <remarks><c>CreatedBy</c> is the user name, <c>CreatedById</c> the id and <c>CreatedByName</c> the display name of the creator (the naming of <c>DocumentDto</c>). The workflow fields (submit, approve, reject, close) arrive with PL3.3.</remarks>
/// <param name="TotalQty">The total base quantity: what a quantity rule of the approval rules is tested against.</param>
/// <param name="TotalAmount">The sum of the line amounts, in money. Absent for a caller without <c>so.manage</c>: a selling price is not cost, so <c>cost.view</c> is not what decides it.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
/// <param name="Key">The integer key of the order; not part of the JSON.</param>
/// <param name="CreatedByKey">The integer key of the creator, for the ownership rules; not part of the JSON.</param>
public record SalesOrderDto(Guid Id, string No, SalesOrderStatus Status, Guid WarehouseId, string WarehouseName, Guid CustomerId, string CustomerName, string? Remark,
    Guid CreatedById, string CreatedBy, string CreatedByName, DateTime CreatedAt,
    decimal TotalQty, [property: VisibleWith(Permissions.SoManage), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? TotalAmount, IReadOnlyList<SalesOrderLineDto> Lines,
    [property: JsonIgnore] byte[] RowVersion, [property: JsonIgnore] int Key, [property: JsonIgnore] int CreatedByKey)
{
    /// <summary>Changes whenever the order (header, status or lines) changes; opaque: compare it only for equality. Also sent as the ETag header; send it back as If-Match to refuse a stale save.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = "";
}
