using Ims.Domain;
namespace Ims.Application.Features.Stock;

public record LedgerRow(long Id, DateTime OccurredAt, string Sku, string ProductName, string WarehouseCode, decimal QtyDelta,
    decimal UnitCost, LedgerReason Reason, int SourceDocId, string SourceDocNo, string User);
