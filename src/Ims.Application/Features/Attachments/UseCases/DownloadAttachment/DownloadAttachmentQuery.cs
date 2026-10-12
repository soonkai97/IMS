namespace Ims.Application.Features.Attachments;

public record DownloadAttachmentQuery(Guid Id) : IQuery<AttachmentDownload>;
