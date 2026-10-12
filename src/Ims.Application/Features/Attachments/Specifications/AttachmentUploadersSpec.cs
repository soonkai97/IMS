using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public sealed class AttachmentUploadersSpec : Specification<Attachment, AttachmentUploader>
{
    public AttachmentUploadersSpec(AttachmentOwnerType ownerType, int ownerId) =>
        Query.AsNoTracking().Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId)
            .Select(a => new AttachmentUploader(a.PublicId, a.UploadedBy));
}
