using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <param name="Id">The attachment's public id.</param>
/// <param name="OwnerId">The public id of the owner: of a document when <c>OwnerType</c> is Document, of a product when it is Product.</param>
public record AttachmentDto(Guid Id, AttachmentOwnerType OwnerType, Guid OwnerId, string FileName, string ContentType, long SizeBytes,
    string UploadedBy, DateTime UploadedAt);
