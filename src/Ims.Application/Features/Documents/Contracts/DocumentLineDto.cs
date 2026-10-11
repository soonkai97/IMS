using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <param name="UnitCost">The cost per ENTERED unit (the unit in <c>UomCode</c>), in money (2 decimals): what was typed for a receipt, or the average expressed per entered unit for an issue and for a receipt that took the average. Send it back unchanged to keep the line as it is: <c>qty x unitCost</c> is the line <c>amount</c>. Null for a caller without <c>cost.view</c> (the Warehouse staff).</param>
/// <param name="BaseUnitCost">The cost per BASE unit, for stock valuation, shown as a UNIT PRICE with 4 decimals (not money): a carton price spread over the pieces in it (1 carton of 3 at 10.00 is 3.3333 per piece; a carton of 1,000 at 3.00 is 0.0030). Amounts computed from it are money (2 decimals). Never send it back as <c>unitCost</c>. Null for a caller without <c>cost.view</c>.</param>
/// <param name="Amount">The value of the line, in money, stored when the line was saved: <c>round(qty x unitCost, 2)</c> for a receipt, <c>round(baseQty x average, 2)</c> for a line valued at the average. A document total is the sum of these. Null for a caller without <c>cost.view</c>.</param>
/// <param name="Available">On hand in the document's warehouse right now, in base units (compare with <c>BaseQty</c>; lines of one product add up). Null once the document is Posted, Reversed or Cancelled, when the figure no longer says anything about this document.</param>
public record DocumentLineDto(int Id, int ProductId, string Sku, string ProductName, decimal Qty, int UomId, string UomCode,
    decimal BaseQty, [property: VisibleWith(Permissions.CostView)] decimal? UnitCost, [property: VisibleWith(Permissions.CostView, UnitPrice = true)] decimal? BaseUnitCost,
    [property: VisibleWith(Permissions.CostView)] decimal? Amount, string? Remark, decimal? Available = null);
