using System.ComponentModel.DataAnnotations;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <param name="LineNo">1 to n inside the order.</param>
/// <param name="UnitPrice">The price per ENTERED unit (the unit in <c>UomCode</c>), in money. Null for a caller without <c>cost.view</c>.</param>
/// <param name="Amount">The value of the line, in money: <c>round(qty x unitPrice, 2)</c>. The order total is the sum of these. Null for a caller without <c>cost.view</c>.</param>
public record PurchaseOrderLineDto(int LineNo, Guid ProductId, string Sku, string ProductName, decimal Qty, Guid UomId, string UomCode, decimal BaseQty,
    [property: VisibleWith(Permissions.CostView)] decimal? UnitPrice, [property: VisibleWith(Permissions.CostView)] decimal? Amount, string? Remark)
{
    /// <summary>What Posted receipts linked to the order have received of this line, in <c>uomCode</c>. Computed on every read, never stored: a Draft, Submitted, Approved or Cancelled receipt counts as nothing, and so does a Reversed one. Zero until a receipt is posted.</summary>
    [Required]
    public decimal ReceivedQty { get; init; }

    /// <summary>What is still to receive of this line, in <c>uomCode</c>: <c>qty</c> minus <c>receivedQty</c>, never below zero. A receipt may not ask for more than this.</summary>
    [Required]
    public decimal RemainingQty { get; init; }

    /// <summary><c>receivedQty</c> in base units: the unit the ledger and the over-receipt check use.</summary>
    [Required]
    public decimal ReceivedBaseQty { get; init; }

    /// <summary><c>remainingQty</c> in base units.</summary>
    [Required]
    public decimal RemainingBaseQty { get; init; }
}
