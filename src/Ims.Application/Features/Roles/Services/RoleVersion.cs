using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// The concurrency token of a role: a keyed hash (HMAC-SHA256, 16 bytes, base64url) of the role's public id and its ROW_VERSION, made like the document token (the raw ROW_VERSION is
/// one counter for the whole database, so it is not handed out). The key is derived (HKDF, own context) from the JWT signing key. The role's row version moves when a permission is
/// granted or revoked too (see AppDbContext), so one token covers the role and its permissions.
/// </summary>
public sealed class RoleVersion(ITokenValidationSettings settings) : IRoleVersion, IAppService
{
    private const string KeyContext = "Ims.RoleVersion.v1";
    private const int KeyBytes = 32;
    private const int TokenBytes = 16;
    private const int GuidBytes = 16;

    private readonly byte[] _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, settings.SigningKey, KeyBytes, salt: null, info: Encoding.ASCII.GetBytes(KeyContext));

    public string Token(Guid publicId, byte[] rowVersion)
    {
        var data = new byte[GuidBytes + rowVersion.Length];
        publicId.TryWriteBytes(data);
        rowVersion.CopyTo(data, GuidBytes);
        return Base64Url.EncodeToString(HMACSHA256.HashData(_key, data).AsSpan(0, TokenBytes));
    }

    public void EnsureCurrent(Role role, string? expectedToken)
    {
        if (string.IsNullOrEmpty(expectedToken)) return;
        var current = Encoding.ASCII.GetBytes(Token(role.PublicId, role.RowVersion));
        // Anything that is not exactly the current token (any length, any characters) is the same answer as a stale one.
        if (!CryptographicOperations.FixedTimeEquals(current, Encoding.UTF8.GetBytes(expectedToken))) throw new ConflictException(Messages.RoleChangedByOther);
    }
}
