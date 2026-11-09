namespace Ims.Domain;

/// <summary>What the Posted returns of one line of a document have taken back so far, in the line's own unit (<see cref="Qty"/>) and in base units (<see cref="BaseQty"/>).</summary>
public readonly record struct ReturnedQuantity(decimal Qty, decimal BaseQty);
