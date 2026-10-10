using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public class DownloadAttachmentHandler(IReadRepository<Attachment> attachments, IAttachmentOwners owners, IFileStorage storage)
    : IQueryHandler<DownloadAttachmentQuery, AttachmentDownload>
{
    public async Task<AttachmentDownload> HandleAsync(DownloadAttachmentQuery q, CancellationToken ct)
    {
        var a = await attachments.FirstOrDefaultAsync(new AttachmentByIdSpec(q.Id), ct) ?? throw new NotFoundException(Messages.AttachmentNotFound);
        await owners.For(a.OwnerType).EnsureCanReadAsync(a.OwnerId, ct);
        var stream = await storage.OpenReadAsync(a.StoragePath, ct);
        return new AttachmentDownload(stream, a.FileName, a.ContentType, AttachmentRules.IsImage(a.Kind));
    }
}
