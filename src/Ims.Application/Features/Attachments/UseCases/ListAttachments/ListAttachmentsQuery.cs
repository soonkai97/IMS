using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public record ListAttachmentsQuery(AttachmentOwnerType OwnerType, Guid OwnerId) : IQuery<IReadOnlyList<AttachmentDto>>;
