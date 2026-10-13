using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public class DownloadAttachmentHandler(IReadRepository<Attachment> attachments, IAttachmentOwners owners, IFileStorage storage)
    : IQueryHandler<DownloadAttachmentQuery, AttachmentDownload>
{
    public async Task<AttachmentDownload> HandleAsync(DownloadAttachmentQuery q, CancellationToken ct)
    {
        var a = await attachments.FirstOrDefaultAsync(new ByPublicIdSpec<Attachment>(q.Id, tracked: false), ct) ?? throw new NotFoundException(Messages.AttachmentNotFound);
        try { await owners.For(a.OwnerType).EnsureCanReadAsync(a.OwnerId, ct); }
        catch (NotFoundException) { throw new NotFoundException(Messages.AttachmentNotFound); } // an owner the caller cannot see: same answer as a missing file (SEC-11)
        var stream = await storage.OpenReadAsync(a.StoragePath, ct);
        return new AttachmentDownload(stream, a.FileName, a.ContentType, AttachmentRules.IsImage(a.Kind));
    }
}
