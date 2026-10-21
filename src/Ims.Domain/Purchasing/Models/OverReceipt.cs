namespace Ims.Domain;

/// <summary>A receipt asks for more of an order line than remains. <see cref="Requested"/>, <see cref="Received"/> and <see cref="Remaining"/> are in the order line's unit.</summary>
public readonly record struct OverReceipt(int LineNo, decimal Ordered, decimal Received, decimal Remaining, decimal Requested);
