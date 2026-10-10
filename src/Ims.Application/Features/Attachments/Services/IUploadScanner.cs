namespace Ims.Application.Features.Attachments;

/// <summary>
/// Extension point for scanning an uploaded file (malware scanner, content-disarm service, DLP) before it is stored. Called once per
/// upload, after the type check, the PDF active-content check and the image metadata removal, so it sees exactly the bytes that would be
/// stored. A file that is not clean is refused and never reaches storage.
/// <para>
/// The default adapter (<c>NoOpUploadScanner</c>) accepts everything. To plug in a real scanner, implement this interface in
/// Infrastructure (for example a ClamAV client) and register it in place of the default; see docs/security/upload-scanning.md.
/// </para>
/// <para>
/// A scanner that cannot reach its engine should throw rather than return clean: the upload then fails (fail closed) with the
/// generic server-error response. A scanner that prefers to fail open must decide that itself and log it.
/// </para>
/// </summary>
public interface IUploadScanner
{
    /// <param name="file">The content to inspect. The caller owns the stream; do not dispose it.</param>
    Task<UploadScanResult> ScanAsync(UploadScanRequest file, CancellationToken ct);
}
