namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>The stored state of a document line after a step: what it holds in base units, its costs and amount, and the receipt snapshots a reversal reads.</summary>
internal sealed record TraceLine(
    int LineId, int ProductId, decimal BaseQty, decimal UnitCost, decimal EnteredUnitCost, decimal Amount,
    decimal? AvgCostBefore, decimal? AvgCostAfter, int? PreviousLineId);
