namespace Ims.Api;

/// <summary>Values of the <c>code</c> extension on a ProblemDetails. A client branches on these, never on the sentence.</summary>
public static class ProblemCodes
{
    /// <summary>409: posting or submitting needs more stock than is on hand (extensions productId, available, requested).</summary>
    public const string InsufficientStock = "insufficient_stock";
    /// <summary>409: a reversal would make one or more products negative (extension shortages).</summary>
    public const string ReversalBlocked = "reversal_blocked";
}
