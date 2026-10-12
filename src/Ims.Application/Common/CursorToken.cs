using System.Buffers.Binary;
using System.Buffers.Text;
using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// The opaque cursor of a seek-paged list: base64url of the <see cref="ICursorProtector"/>-protected bytes of one version byte and the 8-byte position
/// (the internal <c>Id</c> of the last row of the page). Clients pass it back unchanged and never parse it. It is encrypted and authenticated, so it does
/// not reveal the global row counter and a made-up value is refused. Decoding yields exactly the key the seek predicate (<c>Id &lt; position</c>) uses, so
/// the query plan does not change.
/// </summary>
public static class CursorToken
{
    private const string CursorField = "cursor";
    private const byte Version = 1;
    private const int PayloadLength = 1 + sizeof(long);

    public static string Encode(ICursorProtector protector, string list, long position)
    {
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1), position);
        return Base64Url.EncodeToString(protector.Protect(list, payload));
    }

    /// <summary>
    /// False for anything <see cref="Encode"/> did not make for this list: too long (checked before any decoding), not base64url, a variant spelling
    /// of a valid token (whitespace), altered, truncated, another list, another version, or a position below 1.
    /// </summary>
    public static bool TryDecode(ICursorProtector protector, string list, string? token, out long position)
    {
        position = 0;
        if (string.IsNullOrEmpty(token) || token.Length > Limits.CursorMaxLength || !Base64Url.IsValid(token)) return false;

        var data = Base64Url.DecodeFromChars(token);
        if (Base64Url.EncodeToString(data) != token) return false;   // only the canonical spelling is accepted

        if (!protector.TryUnprotect(list, data, out var payload) || payload.Length != PayloadLength || payload[0] != Version) return false;

        position = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1));
        return position >= 1;
    }

    /// <summary>The position inside a cursor; null when no cursor was given (the first page).</summary>
    /// <exception cref="ValidationException">A cursor was given and is not one this server made for this list.</exception>
    public static long? Decode(ICursorProtector protector, string list, string? token)
    {
        if (token is null) return null;
        return TryDecode(protector, list, token, out var position)
            ? position
            : throw new ValidationException(Messages.CursorInvalid, new Dictionary<string, string[]> { [CursorField] = [Messages.CursorInvalid] });
    }
}
