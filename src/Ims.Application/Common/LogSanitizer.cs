namespace Ims.Application;

/// <summary>
/// Makes request-derived text safe to write into a log line. A client controls the path, and exception messages can echo input;
/// without this, an encoded line break would let a caller forge extra log entries (log forging, CWE-117).
/// </summary>
public static class LogSanitizer
{
    private const int MaxLength = 256;
    private const char Replacement = '_';

    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var text = value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (text.Length > MaxLength) text = text[..MaxLength];
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = char.IsControl(source[i]) ? Replacement : source[i];
        });
    }
}
