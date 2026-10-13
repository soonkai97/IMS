using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <summary>The files of one owner. <c>ownerPublicId</c> is the id the client named: it is the owner's id in the answer, so nothing is looked up for it.</summary>
public sealed class AttachmentsByOwnerSpec : Specification<Attachment, AttachmentDto>
{
    public AttachmentsByOwnerSpec(AttachmentOwnerType ownerType, int ownerId, Guid ownerPublicId) =>
        Query.AsNoTracking().Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId)
            .OrderBy(a => a.Id)
            .Select(a => new AttachmentDto(a.PublicId, a.OwnerType, ownerPublicId, a.FileName, a.ContentType, a.SizeBytes, "", a.UploadedAt));
}
