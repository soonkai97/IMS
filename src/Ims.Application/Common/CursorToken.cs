using System.Buffers.Binary;
using System.Buffers.Text;
using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// The opaque cursor of a seek-paged list: base64url of one version byte and the 8-byte position (the internal <c>Id</c> of the last row of the page).
/// Clients pass it back unchanged and never parse it. It is not signed and not encrypted: a forged value only moves the start of a page inside the filter
/// and scope the server still applies. Decoding yields exactly the key the seek predicate (<c>Id &lt; position</c>) uses, so the query plan does not change.
/// </summary>
public static class CursorToken
{
    private const string CursorField = "cursor";
    private const byte Version = 1;
    private const int PayloadLength = 1 + sizeof(long);

    public static string Encode(long position)
    {
        Span<byte> bytes = stackalloc byte[PayloadLength];
        bytes[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(bytes[1..], position);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>False for anything that <see cref="Encode"/> did not make: too long, not base64url, truncated, another version, or a position below 1.</summary>
    public static bool TryDecode(string? token, out long position)
    {
        position = 0;
        if (string.IsNullOrEmpty(token) || token.Length > Limits.CursorMaxLength) return false;

        // TryDecodeFromChars throws on a bad character, so the shape is checked first.
        if (!Base64Url.IsValid(token, out var length) || length != PayloadLength) return false;

        Span<byte> bytes = stackalloc byte[PayloadLength];
        if (Base64Url.DecodeFromChars(token, bytes) != PayloadLength || bytes[0] != Version) return false;

        position = BinaryPrimitives.ReadInt64BigEndian(bytes[1..]);
        return position >= 1;
    }

    /// <summary>The position inside a cursor; null when no cursor was given (the first page).</summary>
    /// <exception cref="ValidationException">A cursor was given and is not one this server made.</exception>
    public static long? Decode(string? token)
    {
        if (token is null) return null;
        return TryDecode(token, out var position)
            ? position
            : throw new ValidationException(Messages.CursorInvalid, new Dictionary<string, string[]> { [CursorField] = [Messages.CursorInvalid] });
    }
}
