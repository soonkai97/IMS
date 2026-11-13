namespace Ims.Application.Features.Attachments;

/// <summary>An opened file ready to stream to the client. The caller disposes <see cref="Content"/>.</summary>
public sealed record AttachmentDownload(Stream Content, string FileName, string ContentType, bool IsImage);
