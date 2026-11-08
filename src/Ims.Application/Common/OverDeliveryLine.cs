namespace Ims.Application;

/// <summary>One order line a posting would deliver too much of, in the order line's unit. Quantities only: no money.</summary>
public record OverDeliveryLine(int LineNo, string Sku, decimal Ordered, decimal Delivered, decimal Remaining, decimal Requested);
