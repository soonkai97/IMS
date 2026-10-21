namespace Ims.Application;

/// <summary>One order line a posting would receive too much of, in the order line's unit. Quantities only: no money.</summary>
public record OverReceiptLine(int LineNo, string Sku, decimal Ordered, decimal Received, decimal Remaining, decimal Requested);
