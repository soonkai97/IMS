namespace Ims.Api;

/// <summary>Values of the <c>code</c> extension on a ProblemDetails. A client branches on these, never on the sentence.</summary>
public static class ProblemCodes
{
    /// <summary>409: posting or submitting needs more stock than is on hand (extensions productId, available, requested).</summary>
    public const string InsufficientStock = "insufficient_stock";
    /// <summary>409: a reversal would make one or more products negative (extension shortages).</summary>
    public const string ReversalBlocked = "reversal_blocked";
    /// <summary>409: a receipt from a purchase order asks, at posting, for more of an order line than remains (extension lines: line number, sku, ordered, received, remaining, requested; quantities only).</summary>
    public const string OverReceipt = "over_receipt";
    public const string OverDelivery = "over_delivery";
    /// <summary>409: a return asks, at posting, for more of a line of the returned document than can still be returned (extension lines: line position, sku, unit code, original, returned, remaining, requested; all in the unit of that line).</summary>
    public const string OverReturn = "over_return";
    /// <summary>409: a return line names an original line whose quantity in the unit of stock is used up by earlier returns (a tiny conversion factor): the line must be left out.</summary>
    public const string ReturnLineExhausted = "return_line_exhausted";
    /// <summary>409: the document was changed by someone else since the caller opened it (extensions changedBy, changedAt).</summary>
    public const string DocumentChanged = "document_changed";
    /// <summary>428: a document command was sent without the If-Match header.</summary>
    public const string IfMatchRequired = "if_match_required";
    /// <summary>401: the right password for a locked account (extension retryAfterMinutes).</summary>
    public const string AccountLocked = "account_locked";
    /// <summary>429: too many requests (extension retryAfterSeconds, header Retry-After).</summary>
    public const string RateLimited = "rate_limited";
}
