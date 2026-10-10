namespace Ims.Application.Features.Attachments;

public record DownloadAttachmentQuery(int Id) : IQuery<AttachmentDownload>;
