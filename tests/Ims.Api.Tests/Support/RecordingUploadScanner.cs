using Ims.Application.Features.Attachments;

namespace Ims.Api.Tests.Support;

/// <summary>A scanner for tests: remembers what it was asked to scan and refuses files that contain a marker text.</summary>
public sealed class RecordingUploadScanner(string? refuseWhenContains = null, bool fail = false) : IUploadScanner
{
    private readonly List<(UploadScanRequest Request, byte[] Content)> _scans = [];

    public IReadOnlyList<(UploadScanRequest Request, byte[] Content)> Scans
    {
        get { lock (_scans) return [.. _scans]; }
    }

    public async Task<UploadScanResult> ScanAsync(UploadScanRequest file, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        await file.Content.CopyToAsync(copy, ct);
        var content = copy.ToArray();
        lock (_scans) _scans.Add((file, content));
        if (fail) throw new InvalidOperationException("scanner engine unavailable");
        return refuseWhenContains is not null && System.Text.Encoding.ASCII.GetString(content).Contains(refuseWhenContains)
            ? UploadScanResult.Infected("Test.Signature\n")
            : UploadScanResult.Clean;
    }
}
