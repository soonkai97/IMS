using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public record AttachmentDto(int Id, AttachmentOwnerType OwnerType, int OwnerId, string FileName, string ContentType, long SizeBytes,
    string UploadedBy, DateTime UploadedAt);
