using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <param name="UnitCost">Per base unit. Null for a caller without <c>cost.view</c> (the Warehouse staff).</param>
/// <param name="Available">On hand in the document's warehouse right now, in base units (compare with <c>BaseQty</c>; lines of one product add up). Null once the document is Posted, Reversed or Cancelled, when the figure no longer says anything about this document.</param>
public record DocumentLineDto(int Id, int ProductId, string Sku, string ProductName, decimal Qty, int UomId, string UomCode,
    decimal BaseQty, [property: VisibleWith(Permissions.CostView)] decimal? UnitCost, string? Remark, decimal? Available = null);
