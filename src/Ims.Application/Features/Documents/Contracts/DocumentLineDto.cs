namespace Ims.Application.Features.Documents;

/// <param name="Available">On hand in the document's warehouse right now, in base units (compare with <c>BaseQty</c>; lines of one product add up). Null once the document is Posted, Reversed or Cancelled, when the figure no longer says anything about this document.</param>
public record DocumentLineDto(int Id, int ProductId, string Sku, string ProductName, decimal Qty, int UomId, string UomCode,
    decimal BaseQty, decimal UnitCost, string? Remark, decimal? Available = null);
