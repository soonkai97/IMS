using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public sealed class AttachmentsByOwnerSpec : Specification<Attachment, AttachmentDto>
{
    public AttachmentsByOwnerSpec(AttachmentOwnerType ownerType, int ownerId) =>
        Query.AsNoTracking().Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId)
            .OrderBy(a => a.Id)
            .Select(a => new AttachmentDto(a.Id, a.OwnerType, a.OwnerId, a.FileName, a.ContentType, a.SizeBytes, "", a.UploadedAt));
}
