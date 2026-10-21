using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <param name="LineNo">1 to n inside the order.</param>
/// <param name="UnitPrice">The price per ENTERED unit (the unit in <c>UomCode</c>), in money. Null for a caller without <c>cost.view</c>.</param>
/// <param name="Amount">The value of the line, in money: <c>round(qty x unitPrice, 2)</c>. The order total is the sum of these. Null for a caller without <c>cost.view</c>.</param>
public record PurchaseOrderLineDto(int LineNo, Guid ProductId, string Sku, string ProductName, decimal Qty, Guid UomId, string UomCode, decimal BaseQty,
    [property: VisibleWith(Permissions.CostView)] decimal? UnitPrice, [property: VisibleWith(Permissions.CostView)] decimal? Amount, string? Remark);
