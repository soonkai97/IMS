using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public sealed class AttachmentsCountSpec : Specification<Attachment>
{
    public AttachmentsCountSpec(AttachmentOwnerType ownerType, int ownerId) =>
        Query.AsNoTracking().Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId);
}
