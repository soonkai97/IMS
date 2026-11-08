namespace Ims.Domain;

/// <summary>One line of a delivery as the order sees it: which order line, and how much of it (entered unit and base unit).</summary>
public readonly record struct DeliveryQuantity(int LineNo, decimal Qty, decimal BaseQty);
