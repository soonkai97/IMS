namespace Ims.Domain;

/// <summary>A delivery asks for more of an order line than remains. <see cref="Requested"/>, <see cref="Delivered"/> and <see cref="Remaining"/> are in the order line's unit.</summary>
public readonly record struct OverDelivery(int LineNo, decimal Ordered, decimal Delivered, decimal Remaining, decimal Requested);
