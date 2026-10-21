namespace Ims.Application;

/// <summary>A linked receipt asks, at posting, for more of an order line than remains (409, code <c>over_receipt</c>). The locked check: the one at save time is advisory.</summary>
public class OverReceiptException(string message, IReadOnlyList<OverReceiptLine> lines) : ConflictException(message)
{
    public IReadOnlyList<OverReceiptLine> Lines { get; } = lines;
}
