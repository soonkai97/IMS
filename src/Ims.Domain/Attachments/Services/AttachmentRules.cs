namespace Ims.Domain;

/// <summary>What may be stored. Types are detected from the leading bytes ("magic numbers"); the extension and content type are derived from that, not from the upload.</summary>
public static class AttachmentRules
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] WebpTag = "WEBP"u8.ToArray();
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();

    private const int WebpTagOffset = 8;

    /// <returns>The detected kind, or null when the bytes are not an allowed file.</returns>
    public static FileKind? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(Jpeg)) return FileKind.Jpeg;
        if (header.StartsWith(Png)) return FileKind.Png;
        if (header.StartsWith(Pdf)) return FileKind.Pdf;
        if (header.StartsWith(Riff) && header.Length >= WebpTagOffset + WebpTag.Length && header.Slice(WebpTagOffset, WebpTag.Length).SequenceEqual(WebpTag))
            return FileKind.Webp;
        return null;
    }

    public static string ContentTypeOf(FileKind kind) => kind switch
    {
        FileKind.Jpeg => "image/jpeg",
        FileKind.Png => "image/png",
        FileKind.Webp => "image/webp",
        _ => "application/pdf"
    };

    public static string ExtensionOf(FileKind kind) => kind switch
    {
        FileKind.Jpeg => ".jpg",
        FileKind.Png => ".png",
        FileKind.Webp => ".webp",
        _ => ".pdf"
    };

    public static bool IsImage(FileKind kind) => kind != FileKind.Pdf;

    private static readonly byte[][] ActivePdfTokens =
        ["/JavaScript"u8.ToArray(), "/JS"u8.ToArray(), "/Launch"u8.ToArray(), "/EmbeddedFile"u8.ToArray(), "/RichMedia"u8.ToArray()];

    /// <summary>
    /// A PDF that can run scripts, launch programs or carry embedded files is refused: the file is handed to colleagues who open it
    /// locally. This is a conservative token scan, not a malware scanner; a scanning hook can be added behind the storage port.
    /// </summary>
    public static bool HasActivePdfContent(ReadOnlySpan<byte> pdf)
    {
        foreach (var token in ActivePdfTokens)
            if (pdf.IndexOf(token) >= 0) return true;
        return false;
    }

    /// <summary>
    /// A display name that is safe to store and to put in a Content-Disposition header: file name only (no path), no control or
    /// reserved characters, bounded length, with the extension matching the detected kind.
    /// </summary>
    public static string SafeDisplayName(string? clientName, FileKind kind)
    {
        var name = Path.GetFileNameWithoutExtension((clientName ?? "").Replace('\\', '/').Split('/').Last());
        var cleaned = new string(name.Where(c => !char.IsControl(c) && !"<>:\"/\\|?*;,%".Contains(c)).ToArray()).Trim().Trim('.');
        if (cleaned.Length == 0) cleaned = "file";
        var ext = ExtensionOf(kind);
        var max = Limits.AttachmentNameMaxLength - ext.Length;
        return (cleaned.Length > max ? cleaned[..max] : cleaned) + ext;
    }
}
