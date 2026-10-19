namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>A stock balance after a step.</summary>
internal sealed record TraceBalance(int ProductId, decimal QtyOnHand, decimal AvgCost, int? LastReceiptLineId);
