namespace Ims.Api;

/// <summary>Values of the <c>code</c> extension on a ProblemDetails. A client branches on these, never on the sentence.</summary>
public static class ProblemCodes
{
    /// <summary>409: posting or submitting needs more stock than is on hand (extensions productId, available, requested).</summary>
    public const string InsufficientStock = "insufficient_stock";
    /// <summary>409: a reversal would make one or more products negative (extension shortages).</summary>
    public const string ReversalBlocked = "reversal_blocked";
    /// <summary>409: the document was changed by someone else since the caller opened it (extensions changedBy, changedAt).</summary>
    public const string DocumentChanged = "document_changed";
    /// <summary>428: a document command was sent without the If-Match header.</summary>
    public const string IfMatchRequired = "if_match_required";
    /// <summary>401: the right password for a locked account (extension retryAfterMinutes).</summary>
    public const string AccountLocked = "account_locked";
    /// <summary>429: too many requests (extension retryAfterSeconds, header Retry-After).</summary>
    public const string RateLimited = "rate_limited";
}
