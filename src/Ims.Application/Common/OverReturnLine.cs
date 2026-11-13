namespace Ims.Application;

/// <summary>One original line a return asks too much of: its position, product, the unit all the figures are in, and the quantities (original, already returned, still returnable, requested).</summary>
public record OverReturnLine(int LineNo, string Sku, string UomCode, decimal Original, decimal Returned, decimal Remaining, decimal Requested);
