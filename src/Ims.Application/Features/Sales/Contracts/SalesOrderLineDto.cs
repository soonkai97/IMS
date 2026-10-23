using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <param name="LineNo">1 to n inside the order.</param>
/// <param name="UnitPrice">The price per ENTERED unit (the unit in <c>UomCode</c>), in money. Absent for a caller without <c>so.manage</c>.</param>
/// <param name="Amount">The value of the line, in money: <c>round(qty x unitPrice, 2)</c>. The order total is the sum of these. Absent for a caller without <c>so.manage</c>.</param>
public record SalesOrderLineDto(int LineNo, Guid ProductId, string Sku, string ProductName, decimal Qty, Guid UomId, string UomCode, decimal BaseQty,
    [property: VisibleWith(Permissions.SoManage), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? UnitPrice, [property: VisibleWith(Permissions.SoManage), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Amount, string? Remark);
