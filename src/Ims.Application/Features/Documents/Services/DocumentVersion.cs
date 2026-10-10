using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The concurrency token of a document: its ROW_VERSION as base64. The API hands it out as <c>concurrencyToken</c> and as the ETag, and a client may
/// send it back (If-Match) to have a stale edit refused before anything is changed. Changing the lines moves it too (see AppDbContext).
/// </summary>
public static class DocumentVersion
{
    public static string Token(byte[]? rowVersion) => Convert.ToBase64String(rowVersion ?? []);

    /// <exception cref="StaleDocumentException">The caller sent a token and the document has changed since; says who changed it last and when.</exception>
    public static void EnsureCurrent(StockDocument document, string? expectedToken)
    {
        if (string.IsNullOrEmpty(expectedToken)) return;
        if (!string.Equals(Token(document.RowVersion), expectedToken, StringComparison.Ordinal))
            throw new StaleDocumentException(document.No, document.RecRevisor, document.RecRevDate);
    }
}
