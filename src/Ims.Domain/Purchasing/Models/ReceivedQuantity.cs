namespace Ims.Domain;

/// <summary>What the Posted receipts linked to one order line have received so far, in the order line's unit (<see cref="Qty"/>) and in base units (<see cref="BaseQty"/>).</summary>
public readonly record struct ReceivedQuantity(decimal Qty, decimal BaseQty);
