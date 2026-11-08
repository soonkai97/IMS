namespace Ims.Domain;

/// <summary>What the Posted deliveries linked to one sales-order line have delivered so far, in the order line's unit (<see cref="Qty"/>) and in base units (<see cref="BaseQty"/>).</summary>
public readonly record struct DeliveredQuantity(decimal Qty, decimal BaseQty);
