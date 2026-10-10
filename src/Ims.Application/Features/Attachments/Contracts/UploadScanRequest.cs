using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <param name="Content">Seekable stream positioned at 0, holding the exact bytes that would be stored.</param>
/// <param name="FileName">Sanitised display name (never the client's raw name).</param>
/// <param name="Kind">Type detected from the bytes.</param>
public record UploadScanRequest(Stream Content, string FileName, FileKind Kind, long Length);
