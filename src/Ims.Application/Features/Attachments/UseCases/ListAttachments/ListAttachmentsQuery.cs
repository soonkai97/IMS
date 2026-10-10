using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public record ListAttachmentsQuery(AttachmentOwnerType OwnerType, int OwnerId) : IQuery<IReadOnlyList<AttachmentDto>>;
