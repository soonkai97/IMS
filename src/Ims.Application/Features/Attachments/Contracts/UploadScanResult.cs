namespace Ims.Application.Features.Attachments;

/// <summary>The verdict of an <see cref="IUploadScanner"/>. Built through <see cref="Clean"/> or <see cref="Infected"/>.</summary>
public sealed record UploadScanResult
{
    private const int MaxReasonLength = 200;

    private UploadScanResult(bool isClean, string? reason)
    {
        IsClean = isClean;
        Reason = reason;
    }

    /// <summary>False when the file must not be stored.</summary>
    public bool IsClean { get; }

    /// <summary>Why it was refused: for the log only (clients get a generic message). Control characters are removed and the length is capped.</summary>
    public string? Reason { get; }

    public static UploadScanResult Clean { get; } = new(true, null);

    public static UploadScanResult Infected(string reason)
    {
        var cleaned = new string(reason.Where(c => !char.IsControl(c)).ToArray());
        return new UploadScanResult(false, cleaned.Length > MaxReasonLength ? cleaned[..MaxReasonLength] : cleaned);
    }
}
