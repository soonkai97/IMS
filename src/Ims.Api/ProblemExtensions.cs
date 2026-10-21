namespace Ims.Api;

public static class ProblemExtensions
{
    public const string ProductId = "productId";
    public const string Available = "available";
    public const string Requested = "requested";
    public const string CorrelationId = "correlationId";
    public const string Errors = "errors";
    /// <summary>Machine-readable kind of a business conflict (<see cref="ProblemCodes"/>), for clients that must tell two 409s apart.</summary>
    public const string Code = "code";
    /// <summary>The products a blocked reversal would push below zero.</summary>
    public const string Shortages = "shortages";
    /// <summary>The order lines an over-receipt (409 <c>over_receipt</c>) would take too much of: line number, sku, ordered, received, remaining and requested quantity in the order line's unit. Quantities only, and only for a caller who may read purchase orders.</summary>
    public const string OverReceiptLines = "lines";
    /// <summary>Display name of the user who last changed a document that went stale, and when (UTC).</summary>
    public const string ChangedBy = "changedBy";
    public const string ChangedAt = "changedAt";
    public const string RetryAfterMinutes = "retryAfterMinutes";
    public const string RetryAfterSeconds = "retryAfterSeconds";
}
