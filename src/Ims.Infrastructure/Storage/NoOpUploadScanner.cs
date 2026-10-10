using Ims.Application.Features.Attachments;

namespace Ims.Infrastructure.Storage;

/// <summary>
/// The default <see cref="IUploadScanner"/>: accepts every file. It exists so the upload path always has a scanner to call; replace the
/// registration in <c>AddFileStorage</c> with a real adapter (ClamAV, a cloud scanning API) to turn scanning on.
/// </summary>
public sealed class NoOpUploadScanner : IUploadScanner
{
    public Task<UploadScanResult> ScanAsync(UploadScanRequest file, CancellationToken ct) => Task.FromResult(UploadScanResult.Clean);
}
