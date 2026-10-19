namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>One ledger row a posting run produced.</summary>
internal sealed record TraceLedger(int ProductId, decimal QtyDelta, decimal BalanceAfter, decimal UnitCost, string Reason);
