using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <param name="UnitCost">Cost per base unit of the movement. Null for a caller without <c>valuation.view</c>.</param>
/// <param name="BalanceAfter">What this product held in this warehouse right after the movement (the sum of its ledger up to and including this row, in base units). Only given when the ledger is read for one product (<c>productId</c>); null otherwise (a list of several products has no single running balance). Stored with the row when it is written (under the same lock that serialises the postings of the product in the warehouse), so reading any page, however deep, costs the same and the answer cannot be overtaken by a posting.</param>
public record LedgerRow(long Id, DateTime OccurredAt, string Sku, string ProductName, string WarehouseCode, string WarehouseName, decimal QtyDelta,
    [property: VisibleWith(Permissions.ValuationView)] decimal? UnitCost, LedgerReason Reason, int SourceDocId, string SourceDocNo, string User,
    decimal? BalanceAfter = null);
