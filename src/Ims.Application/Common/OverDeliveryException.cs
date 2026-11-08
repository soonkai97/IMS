namespace Ims.Application;

/// <summary>A linked delivery asks, at posting, for more of an order line than remains (409, code <c>over_delivery</c>). The locked check: the one at save time is advisory.</summary>
public class OverDeliveryException(string message, IReadOnlyList<OverDeliveryLine> lines) : ConflictException(message)
{
    public IReadOnlyList<OverDeliveryLine> Lines { get; } = lines;
}
