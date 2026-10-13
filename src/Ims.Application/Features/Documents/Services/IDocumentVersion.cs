using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Makes and checks the concurrency token a client sees (<c>concurrencyToken</c>, the ETag, If-Match).</summary>
public interface IDocumentVersion
{
    /// <summary>The token of one version of one document. The same document and version always give the same token; it reveals nothing about the row version it stands for.</summary>
    string Token(Guid publicId, byte[]? rowVersion);

    /// <exception cref="StaleDocumentException">The caller sent a token and it is not the current one of this document: the document changed, or the token is old, altered or from another document. Says who changed it last and when.</exception>
    void EnsureCurrent(StockDocument document, string? expectedToken);
}
