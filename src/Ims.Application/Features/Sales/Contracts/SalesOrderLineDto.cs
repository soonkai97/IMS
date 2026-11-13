using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <param name="LineNo">1 to n inside the order.</param>
/// <param name="UnitPrice">The price per ENTERED unit (the unit in <c>UomCode</c>), in money. Absent for a caller without <c>so.manage</c>.</param>
/// <param name="Amount">The value of the line, in money: <c>round(qty x unitPrice, 2)</c>. The order total is the sum of these. Absent for a caller without <c>so.manage</c>.</param>
public sealed record SalesOrderLineDto(int LineNo, Guid ProductId, string Sku, string ProductName, decimal Qty, Guid UomId, string UomCode, decimal BaseQty,
    [property: VisibleWith(Permissions.SoManage), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? UnitPrice, [property: VisibleWith(Permissions.SoManage), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Amount, string? Remark)
{
    /// <summary>What Posted deliveries linked to the order have delivered of this line, in <c>uomCode</c>. Computed on every read, never stored: a Draft, Submitted, Approved or Cancelled delivery counts as nothing, and so does a Reversed one. Zero until a delivery is posted. No money.</summary>
    [Required]
    public decimal DeliveredQty { get; init; }

    /// <summary>What is still to deliver of this line, in <c>uomCode</c>: <c>qty</c> minus <c>deliveredQty</c>, never below zero. A delivery may not ask for more than this.</summary>
    [Required]
    public decimal RemainingQty { get; init; }

    /// <summary><c>deliveredQty</c> in base units: the unit the ledger and the over-delivery check use.</summary>
    [Required]
    public decimal DeliveredBaseQty { get; init; }

    /// <summary><c>remainingQty</c> in base units.</summary>
    [Required]
    public decimal RemainingBaseQty { get; init; }
}
