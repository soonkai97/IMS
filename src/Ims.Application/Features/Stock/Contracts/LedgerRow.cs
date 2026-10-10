using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <param name="UnitCost">Cost per base unit of the movement. Null for a caller without <c>valuation.view</c>.</param>
public record LedgerRow(long Id, DateTime OccurredAt, string Sku, string ProductName, string WarehouseCode, string WarehouseName, decimal QtyDelta,
    [property: VisibleWith(Permissions.ValuationView)] decimal? UnitCost, LedgerReason Reason, int SourceDocId, string SourceDocNo, string User);
