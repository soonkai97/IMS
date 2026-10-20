using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The concurrency token of a document: a keyed hash (HMAC-SHA256, 16 bytes, base64url) of the document's public id and its ROW_VERSION. The raw ROW_VERSION is one counter for the
/// whole database (every insert or update in any table moves it), so handing it out would let a warehouse-scoped user measure what other warehouses do (decision D2). The hash cannot be
/// turned back into the counter, is bound to one document (the same version of another document gives another token) and is the same string every time for one version, so a client can
/// still tell whether the copy it holds is the current one by comparing strings. The key is derived (HKDF, own context) from the JWT signing key, the one secret every instance already shares.
/// A client sends the token back as If-Match; changing the lines moves the row version too (see AppDbContext).
/// </summary>
public sealed class DocumentVersion(ITokenValidationSettings settings) : IDocumentVersion, IAppService
{
    private const string KeyContext = "Ims.DocVersion.v1";
    private const int KeyBytes = 32;
    private const int TokenBytes = 16;
    private const int GuidBytes = 16;

    private readonly byte[] _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, settings.SigningKey, KeyBytes, salt: null, info: Encoding.ASCII.GetBytes(KeyContext));

    public string Token(Guid publicId, byte[]? rowVersion)
    {
        var data = new byte[GuidBytes + (rowVersion?.Length ?? 0)];
        publicId.TryWriteBytes(data);
        rowVersion?.CopyTo(data, GuidBytes);
        return Base64Url.EncodeToString(HMACSHA256.HashData(_key, data).AsSpan(0, TokenBytes));
    }

    public void EnsureCurrent(StockDocument document, string? expectedToken) => EnsureCurrent(document.PublicId, document.No, document, expectedToken);

    public void EnsureCurrent(Guid publicId, string no, AuditableEntity row, string? expectedToken)
    {
        if (string.IsNullOrEmpty(expectedToken)) return;
        var current = Encoding.ASCII.GetBytes(Token(publicId, row.RowVersion));
        // Anything that is not exactly the current token (any length, any characters) is the same answer as a stale one: never an exception of another kind.
        if (!CryptographicOperations.FixedTimeEquals(current, Encoding.UTF8.GetBytes(expectedToken)))
            throw new StaleDocumentException(no, row.RecRevisor, row.RecRevDate);
    }
}
