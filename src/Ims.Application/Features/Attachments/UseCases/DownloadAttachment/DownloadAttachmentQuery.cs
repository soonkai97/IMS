namespace Ims.Application.Features.Attachments;

public sealed record DownloadAttachmentQuery(Guid Id) : IQuery<AttachmentDownload>;
